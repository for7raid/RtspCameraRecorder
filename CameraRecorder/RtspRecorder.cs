using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;

namespace CameraRecorder;

public class RtspRecorder
{
    private readonly ILogger<RtspRecorder> _logger;
    private readonly CameraRecorderSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RTSPClient _client;
    private readonly RingBufferAudioStorage _bufferAudioStorage;
    private readonly ISettingsProvider _settingsProvider;
    private readonly RingBufferVideoStorage _bufferVideoStorage;
    private const string ProfileH264 = "H264";
    private const string ProfileH265 = "H265";
    private const string ProfilePCMU = "PCMU";
    MotionAnalyzer _motionAnalyzer;
    private DateTime? _lastMotionTime;
    private bool _isRecording;

    private CancellationTokenSource? _durationCts;
    private Task? _durationLoop;

    /// <summary>
    /// Событие: запись началась (при переходе из состояния ожидания)
    /// </summary>
    public event Action? RecordingStarted;

    /// <summary>
    /// Событие: запись остановлена (при переходе из состояния записи)
    /// </summary>
    public event Action? RecordingStopped;

    /// <summary>
    /// Событие: изменилась длительность записи (вызывается ~раз в секунду)
    /// </summary>
    public event Action<TimeSpan>? RecordingDurationChanged;

    /// <summary>
    /// Текущая длительность записи. Если запись не идёт — TimeSpan.Zero.
    /// </summary>
    public TimeSpan RecordingDuration =>
        _isRecording ? _bufferVideoStorage.CurrentBufferDuration : TimeSpan.Zero;

    public bool StreamingFinished { get { return _client.StreamingFinished; } }
    public RtspRecorder(ILoggerFactory loggerFactory,
        RTSPClient client,
        RingBufferVideoStorage bufferVideoStorage,
        RingBufferAudioStorage bufferAudioStorage,
        ISettingsProvider settingsProvider)
    {
        _loggerFactory = loggerFactory;
        _client = client;
        _bufferAudioStorage = bufferAudioStorage;
        _settingsProvider = settingsProvider;
        _bufferVideoStorage = bufferVideoStorage;
        _logger = _loggerFactory.CreateLogger<RtspRecorder>();
        _settings = _settingsProvider.GetSettings();

        _motionAnalyzer = new(VideoCodec.H265, loggerFactory.CreateLogger<MotionAnalyzer>(), MotionSensitivity.SlowHand);

        client.SetupAudioPayload(ProfilePCMU, ReceiveAudioPCMx);
        client.SetupVideoPayload(ProfileH265, ReceivedVideoData_H265);
        client.SetupVideoPayload(ProfileH264, ReceivedVideoData_H264);

        _client.SetupMessageCompleted += (_, _) =>
        {
            _logger.LogInformation("Setup Completed");
            _client.Play();
        };
    }

    public void Stop()
    {
        StopDurationLoop();
        _client.Stop();
    }

    public void StartRecord()
    {
        _bufferVideoStorage.StartRecord();
        _bufferAudioStorage.StartRecord();

        if (!_isRecording)
        {
            _isRecording = true;
            _logger.LogInformation("Запись началась");
            RecordingStarted?.Invoke();
            StartDurationLoop();
        }
    }

    public async Task StopRecordAsync()
    {
        StopDurationLoop();

        await _bufferVideoStorage.StopRecordAsync();
        await _bufferAudioStorage.StopRecordAsync();

        if (_isRecording)
        {
            _isRecording = false;
            _logger.LogInformation("Запись остановлена");
            RecordingStopped?.Invoke();
            RecordingDurationChanged?.Invoke(TimeSpan.Zero);
        }
    }
    void ReceiveAudioPCMx(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var data in dataArgs.Data)
        {
            _bufferAudioStorage?.AddFrame(data);
        }
    }
    void ReceivedVideoData_H264(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var nalUnitMem in dataArgs.Data)
        {
            var nalUnit = nalUnitMem.Span;
            if (nalUnit.Length > 5)
            {
                var nal_unit_type = (NalUnitType)(nalUnit[4] & 0x1F);
                var unit = nalUnitMem.Slice(5);
                _bufferVideoStorage.AddFrame(unit, nal_unit_type);
                //_motionAnalyzer.Append(unit.ToArray());
                _logger.LogDebug("NAL Type = {nal_unit_type}", nal_unit_type);
            }
        }
    }
    async void ReceivedVideoData_H265(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var nalUnitMem in dataArgs.Data)
        {
            var nalUnit = nalUnitMem.Span;
            if (nalUnit.Length > 5)
            {
                var nal_unit_type = (NalUnitType)((nalUnit[4] >> 1) & 0x3F);
                var unit = nalUnitMem.Slice(4);
                _bufferVideoStorage.AddFrame(unit, nal_unit_type);

                //TODO Запускаем запись по движению, если сейчас нет записи вручную
                if (_motionAnalyzer.Append(unit.ToArray()))
                {
                    StartRecord();
                    _lastMotionTime = DateTime.Now;
                }

                if (_lastMotionTime.HasValue && (DateTime.Now - _lastMotionTime.Value).TotalSeconds > _settings.PostMotionDurationSec)
                {
                    await StopRecordAsync();
                    _lastMotionTime = null;
                }

                _logger.LogDebug("NAL Type = {nal_unit_type}", nal_unit_type);
            }
        }
    }

    public void Start()
    {
        _client.Stop();
        _client.Connect(_settings.RtspUrl, _settings.RtspLogin, _settings.RtspPassword, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);
    }

    // ── private: фоновый опрос длительности записи ──

    private void StartDurationLoop()
    {
        StopDurationLoop();

        _durationCts = new CancellationTokenSource();
        var ct = _durationCts.Token;

        _durationLoop = Task.Run(async () =>
        {
            var lastDuration = TimeSpan.Zero;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var current = RecordingDuration;
                if (current != lastDuration)
                {
                    lastDuration = current;
                    RecordingDurationChanged?.Invoke(current);
                }
            }
        }, ct);
    }

    private void StopDurationLoop()
    {
        if (_durationCts is not null)
        {
            _durationCts.Cancel();
            _durationCts.Dispose();
            _durationCts = null;
            _durationLoop = null;
        }
    }
}
