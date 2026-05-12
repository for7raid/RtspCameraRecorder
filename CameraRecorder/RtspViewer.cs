using CameraRecorder.MotionAnalyzers;
using CameraRecorder.Settings;
using H264Sharp;
using H264SharpBitmapExtentions;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace CameraRecorder;

public class RtspViewer
{
    private readonly ILogger<RtspViewer> _logger;
    private readonly CameraRecorderSettings _settings;
    private readonly H264Decoder _decoder;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RTSPClient _client;
    private readonly ISettingsProvider _settingsProvider;
    private readonly AdaptiveMotionDetector _detector;
    private const string ProfileH264 = "H264";

    public event Action<byte[]>? FrameReceived;
    public bool StreamingFinished { get { return _client.StreamingFinished; } }

    public RtspViewer(ILoggerFactory loggerFactory,
        RTSPClient client,
        RingBufferVideoStorage bufferVideoStorage,
        RingBufferAudioStorage bufferAudioStorage,
        ISettingsProvider settingsProvider)
    {
        _loggerFactory = loggerFactory;
        _client = client;
        _settingsProvider = settingsProvider;
        _logger = _loggerFactory.CreateLogger<RtspViewer>();
        _settings = _settingsProvider.GetSettings();

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


    

    void ReceivedVideoData_H264(RTSPClient client, SimpleDataEventArgs dataArgs)
    {
        foreach (var nalUnitMem in dataArgs.Data)
        {
            var nalUnit = nalUnitMem.Span;
            if (nalUnit.Length > 5)
            {
                var nal_unit_type = (NalUnitType)(nalUnit[4] & 0x1F);
                var unit = nalUnitMem.Slice(5);

                RgbImage rgbOut = new RgbImage(H264Sharp.ImageFormat.Rgb, 640, 480);
                if (_decoder.Decode(nalUnitMem.ToArray(), 0, nalUnit.Length, noDelay: true, out DecodingState ds, ref rgbOut) && ds == DecodingState.dsErrorFree)
                {

                    FrameReceived?.Invoke(rgbOut.GetBytes());

                }

                _logger.LogDebug("NAL Type = {nal_unit_type}", nal_unit_type);
            }
        }
    }
    public void Start()
    {
        _client.Stop();
        if (!string.IsNullOrWhiteSpace(_settings.RtspUrl))
        {
            _client.Connect("rtsp://192.168.1.8:554/stream2", _settings.RtspLogin, _settings.RtspPassword, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);
        }
    }
}

