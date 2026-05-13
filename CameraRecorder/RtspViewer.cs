using CameraRecorder.MotionAnalyzers;
using CameraRecorder.Settings;
using H264Sharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorder;

public class RtspViewer
{
    private readonly ILogger<RtspViewer> _logger;
    private readonly H264Decoder _decoder;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RTSPClient _client;
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly AdaptiveMotionDetector _detector;
    private const string ProfileH264 = "H264";

    public event Action<byte[], ulong>? FrameReceived;
    public bool StreamingFinished { get { return _client.StreamingFinished; } }

    public RtspViewer(ILoggerFactory loggerFactory,
        RTSPClient client,
        RingBufferVideoStorage bufferVideoStorage,
        RingBufferAudioStorage bufferAudioStorage,
        IOptions<CameraRecorderSettings> options)
    {
        _loggerFactory = loggerFactory;
        _client = client;
        _options = options;
        _logger = _loggerFactory.CreateLogger<RtspViewer>();

        _decoder = new H264Decoder();
        _decoder.Initialize();

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




    RgbImage rgbOut = new RgbImage(H264Sharp.ImageFormat.Rgb, 640, 480);
    void ReceivedVideoData_H264(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var nalUnitMem in dataArgs.Data)
        {
            var nalUnit = nalUnitMem.Span;
            if (nalUnit.Length > 5)
            {
                var nal_unit_type = (NalUnitType)(nalUnit[4] & 0x1F);
                var unit = nalUnitMem.Slice(5);
                DecodingState ds;

                var dec = _decoder.Decode(nalUnitMem.ToArray(), 0, nalUnit.Length, noDelay: true, out ds, ref rgbOut);
                if (dec && ds == DecodingState.dsErrorFree)
                {

                    FrameReceived?.Invoke(rgbOut.GetBytes(), dataArgs.RtpTimestamp);

                }
                else
                {

                }

                _logger.LogDebug("NAL Type = {nal_unit_type}", nal_unit_type);
            }
        }
    }
    public void Start()
    {
        _client.Stop();
        if (!string.IsNullOrWhiteSpace(_options.Value.RtspUrl))
        {
            _client.Connect("rtsp://192.168.1.8:554/stream2", _options.Value.RtspLogin, _options.Value.RtspPassword, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);
        }
    }
}

