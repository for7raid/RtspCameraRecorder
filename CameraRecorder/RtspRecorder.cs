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
    private DateTime _lastMotionTime;

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
        _client.Stop();
    }

    public void StartRecord()
    {
        _bufferVideoStorage.StartRecord();
        _bufferAudioStorage.StartRecord();
    }

    public async Task StopRecordAsync()
    {
        await _bufferVideoStorage.StopRecordAsync();
        await _bufferAudioStorage.StopRecordAsync();
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
                if (_motionAnalyzer.Append(unit.ToArray()))
                {
                    StartRecord();
                    _lastMotionTime = DateTime.Now;
                }

                if ((DateTime.Now - _lastMotionTime).TotalSeconds > _settings.PostMotionDurationSec)
                {
                    await StopRecordAsync();
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
}
