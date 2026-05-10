using Microsoft.Extensions.Logging;

namespace CameraRecorder;

public class RtspRecorder
{
    private readonly ILogger<RtspRecorder> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RTSPClient _client;
    private readonly RingBufferAudioStorage _bufferAudioStorage;
    private readonly RingBufferVideoStorage _bufferVideoStorage;
    private string _outputDirecroty;
    private const string ProfileH265 = "H265";

    public bool StreamingFinished { get { return _client.StreamingFinished; } }

    public RtspRecorder(ILoggerFactory loggerFactory,
        RTSPClient client,
        RingBufferAudioStorage bufferAudioStorage,
        RingBufferVideoStorage bufferVideoStorage)
    {
        _loggerFactory = loggerFactory;
        _client = client;
        _bufferAudioStorage = bufferAudioStorage;
        _bufferVideoStorage = bufferVideoStorage;
        _logger = _loggerFactory.CreateLogger<RtspRecorder>();

        _client.NewVideoStream += (_, args) =>
        {
            switch (args.StreamType)
            {
                case ProfileH265:
                    NewH265Stream(args, client);
                    break;
                default:
                    _logger.LogWarning("Unknow Video format {streamtype}", args.StreamType);
                    break;
            }
        };

        _client.NewAudioStream += (_, arg) =>
        {
            switch (arg.StreamType)
            {
                case "PCMU":
                    NewGenericAudio(client, "ul", "PCMU");
                    break;
                default:
                    _logger.LogWarning("Unknow Audio format {streamtype}", arg.StreamType);
                    break;
            }
        };

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

    public void StopRecord()
    {
        _bufferVideoStorage.StopRecord(_outputDirecroty);
        _bufferAudioStorage.StopRecord(_outputDirecroty);
    }


    private void NewGenericAudio(RTSPClient client, string extension, string stringType)
    {

        void ReceiveAudioPCMx(RTSPClient client, SimpleDataEventArgs dataArgs)
        {
            foreach (var data in dataArgs.Data)
            {
                _bufferAudioStorage?.AddFrame(data);
            }
        }
        client.SetupAudioPayload(stringType, ReceiveAudioPCMx);
    }


    private void NewH265Stream(NewStreamEventArgs args, RTSPClient client)
    {
        void ReceivedVideoData_H265(RTSPClient client, SimpleDataEventArgs dataArgs)
        {

            foreach (var nalUnitMem in dataArgs.Data)
            {
                var nalUnit = nalUnitMem.Span;
                // Output some H264 stream information
                if (nalUnit.Length > 5)
                {
                    var nal_unit_type = (nalUnit[4] >> 1) & 0x3F;
                    string description = nal_unit_type switch
                    {
                        1 => "NON IDR NAL",
                        19 => "IDR NAL",
                        32 => "VPS NAL",
                        33 => "SPS NAL",
                        34 => "PPS NAL",
                        39 => "SEI NAL",
                        _ => "OTHER NAL",
                    };
                    _logger.LogDebug("NAL Type = {nal_unit_type} {description}", nal_unit_type, description);

                    _bufferVideoStorage.AddFrame(nalUnitMem.Slice(4), nal_unit_type);
                }


            }
        }
        ;
        client.SetupVideoPayload(ProfileH265, ReceivedVideoData_H265);
    }
    public void Start(string url, string username, string password, string outputDirecroty)
    {
        _outputDirecroty = outputDirecroty;
        _client.Connect(url, username, password, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);
    }
}