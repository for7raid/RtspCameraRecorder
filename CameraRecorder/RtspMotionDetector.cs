using CameraRecorder.MotionAnalyzers;
using CameraRecorder.RTSP;
using CameraRecorder.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorder;

public class RtspMotionDetector
{
    private readonly ILogger<RtspMotionDetector> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RTSPClient _client;
    private readonly IH26xDecoder _h26XDecoder;
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly AdaptiveMotionDetector _detector;
    private const string ProfileH264 = "H264";
    private const string ProfileH265 = "H265";

    public event Action<byte[], ulong>? FrameReceived;
    public event Action? MotionDetected;
    public event Action? MotionEnded;
    public event Action<string>? DetectionLog;



    private DateTime? _lastMotionTime;
    public bool StreamingFinished { get { return _client.StreamingFinished; } }
    public RtspMotionDetector(ILoggerFactory loggerFactory,
        RTSPClient client,
        [FromKeyedServices("OnBufferDecoder")] IH26xDecoder h26XDecoder,
        IOptions<CameraRecorderSettings> options)
    {
        _loggerFactory = loggerFactory;
        _client = client;
        _h26XDecoder = h26XDecoder;
        _options = options;
        _logger = _loggerFactory.CreateLogger<RtspMotionDetector>();

        MotionDetectorSettings settings = new()
        {
            PixelFormat = PixelFormat.Y,
            Width = 640,
            Height = 480,
            BlockSize = 6,                        // Компромисс: 6×6 (106×80 ≈ 8 500 блоков)
            FrameBufferSize = 30,

            // Ключевые параметры
            ChangedBlocksRatioThreshold = 0.008,  // 0.8%
            SigmaThreshold = 3,                  // 3 сигмы: баланс чувствительности и помехоустойчивости

            MinFramesBeforeDetection = 10,
            StatsRecalculationPeriod = 30,

            // Фильтры
            EnableSpikeFilter = true,
            MinMotionDuration = 4,                // 2 кадра подряд для подтверждения
        };

        _detector = new(settings, _loggerFactory.CreateLogger<AdaptiveMotionDetector>());

        client.SetupVideoPayload(ProfileH264, ReceivedVideoData_H264);
        client.SetupVideoPayload(ProfileH265, ReceivedVideoData_H265);

        _client.SetupMessageCompleted += (_, _) =>
        {
            _logger.LogInformation("Setup Completed");
            _client.Play();
        };

        _h26XDecoder.FrameDecoded += _h26XDecoder_FrameDecoded;


        Task.Run(MotionTimer);
    }

    private async void MotionTimer()
    {
        while (true)
        {
            if (_lastMotionTime.HasValue)
            {
                if ((DateTime.Now - _lastMotionTime.Value).TotalSeconds > _options.Value.PostMotionDurationSec)
                {
                    _lastMotionTime = null;
                    MotionEnded?.Invoke();
                    //_logger.LogInformation("Motion ended");

                }

            }

            await Task.Delay(1000);
        }

    }

    private void _h26XDecoder_FrameDecoded(object? sender, DecodedFrameEventArgs e)
    {
        var motion = _detector.DetectMotion(e.Frame.ToY(), (ulong)e.Frame.TimestampUs);
        DetectionLog?.Invoke(motion.ToString());
        if (motion.HasMotion)
        {
            _lastMotionTime = DateTime.Now;
            MotionDetected?.Invoke();
            //_logger.LogInformation("Motion detected");
        }
    }

    public void Stop()
    {
        _client.Stop();
    }

    void ReceivedVideoData_H264(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var nalUnitMem in dataArgs.Data)
        {
            var nalUnit = nalUnitMem.Span;
            if (nalUnit.Length > 5)
            {
                var nal_unit_type = (NalUnitType)(nalUnit[4] & 0x1F);

                _h26XDecoder.DecodeFrame(nalUnitMem.ToArray(), (long)dataArgs.RtpTimestamp, nal_unit_type);

                _logger.LogDebug("NAL Type = {nal_unit_type}", nal_unit_type);
            }
        }
    }
    void ReceivedVideoData_H265(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var nalUnitMem in dataArgs.Data)
        {
            var nalUnit = nalUnitMem.Span;
            if (nalUnit.Length > 5)
            {
                var nal_unit_type = (NalUnitType)((nalUnit[4] >> 1) & 0x3F);

                _h26XDecoder.DecodeFrame(nalUnitMem.ToArray(), (long)dataArgs.RtpTimestamp, nal_unit_type);

                _logger.LogDebug("NAL Type = {nal_unit_type}", nal_unit_type);
            }
        }
    }
    public void Start()
    {
        _client.Stop();
        if (!string.IsNullOrWhiteSpace(_options.Value.RtspSubStreamUrl))
        {
            _client.Connect(_options.Value.RtspSubStreamUrl, _options.Value.RtspLogin, _options.Value.RtspPassword, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_ONLY);
        }
    }
}

