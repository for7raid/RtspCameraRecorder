using Microsoft.Extensions.Logging;
using SharpISOBMFF;
using SharpMP4.Builders;
using SharpMP4.Tracks;
using System.Diagnostics;

namespace RtspClientExample
{
    public class Recorder
    {
        private static ILogger logger = null!;

        private const string ProfileH264 = "H264";
        private const string ProfileH265 = "H265";


        private static readonly byte[] halStartCode = [0x00, 0x00, 0x00, 0x01];



        private static Stream? _outputStream;
        private static IMp4Builder? mp4Builder;
        private static RTSPClient client;
        public static void Init()
        {
           

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddDebug();
            });

            logger = loggerFactory.CreateLogger("Main");

            string url = "rtsp://192.168.1.8:554/stream1";

            string username = "admin";
            string password = "123456";


            // Create a RTSP Client
            client = new(loggerFactory);

            client.NewVideoStream += (_, args) =>
            {
                switch (args.StreamType)
                {
                    case "H264":
                        NewH264Stream(args, client);
                        break;
                    case "H265":
                        NewH265Stream(args, client);
                        break;
                    default:
                        logger.LogWarning("Unknow Video format {streamtype}", args.StreamType);
                        break;
                }
            };

            client.NewAudioStream += (_, arg) =>
            {
               

                switch (arg.StreamType)
                {
                    case "PCMU":
                        NewGenericAudio(client, "ul", "PCMU");

                      
                        break;
                    default:
                        logger.LogWarning("Unknow Audio format {streamtype}", arg.StreamType);
                        break;
                }
            };
            Stopwatch startTime = new Stopwatch();
            client.SetupMessageCompleted += (_, _) =>
            {
                logger.LogInformation("Setup completed");
                client.Play();
                startTime.Start();
            };

            string filesDir = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads).AbsolutePath; //Application.Context.FilesDir.AbsolutePath;
            logger.LogInformation($"Directory {filesDir}");
            _outputStream = new FileStream(Path.Combine(filesDir, "rec.mp4"), FileMode.Create, FileAccess.Write);
            mp4Builder = new Mp4Builder(new SingleStreamOutput(_outputStream)) { TemporaryStorageFactory = new TemporaryMemoryStorageFactory() };
            var videoTrack = new H265Track();
            var tid = videoTrack.TrackID;
            mp4Builder.AddTrack(videoTrack);
            //var audioTrack = new AACTrack(1, 8000, 8);
            //mp4Builder?.AddTrack(audioTrack);

            // Connect to RTSP Server
            Console.WriteLine("Connecting");

            client.Connect(url, username, password, RTSPClient.RTP_TRANSPORT.TCP, RTSPClient.MEDIA_REQUEST.VIDEO_AND_AUDIO);

            //client.Pause();
            //DateTime startTime = DateTime.Now.AddHours(-1);
            //client.Play(startTime, startTime.AddMinutes(1), 1.0);

            // Wait for user to terminate programme
            // Check for null which is returned when running under some IDEs
            // OR wait for the Streaming to Finish - eg an error on the RTSP socket

            Console.WriteLine("Press ENTER to exit");

            //ConsoleKeyInfo key = default;
            //while (key.Key != ConsoleKey.Enter && !client.StreamingFinished())
            //{
            //    while (!Console.KeyAvailable && !client.StreamingFinished())
            //    {
            //        // Avoid maxing out CPU on systems that instantly return null for ReadLine
            //        Thread.Sleep(250);
            //    }
            //    if (Console.KeyAvailable)
            //    {
            //        key = Console.ReadKey();
            //    }
            //}


            //mp4Builder?.FinalizeMedia();
            //_outputStream.Dispose();

            //client.Stop();

            //startTime.Stop();


            

            //Console.WriteLine($"Finished {startTime.Elapsed}");

            //Console.ReadLine();
        }

        public static void Stop()
        {
            mp4Builder?.FinalizeMedia();
            _outputStream.Dispose();
            client.Stop();
            logger.LogInformation("Recording stopped");
            
        }

        private static void NewGenericAudio(RTSPClient client, string extension, string stringType)
        {
            //string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            //string filename = "rtsp_capture_" + now + "." + extension;
            //FileStream fs_a = new(filename, FileMode.Create);
            void ReceiveAudioPCMx(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var data in dataArgs.Data)
                {
                    //fs_a.Write(data.Span);
                    //mp4Builder?.ProcessTrackSample(2, data.ToArray());
                }

            }
            ;
            client.SetupAudioPayload(stringType, ReceiveAudioPCMx);
        }


        private static void NewH265Stream(NewStreamEventArgs args, RTSPClient client)
        {
            //string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            //string filename = "rtsp_capture_" + now + ".265";
            //FileStream fs_v = new(filename, FileMode.Create);



            if (args.StreamConfigurationData is H265StreamConfigurationData h265StreamConfigurationData)
            {
                foreach (var data in h265StreamConfigurationData.OutOfBandNal)
                {
                    //WriteNalToFileIfNotEmpty(fs_v, data);
                }

            }
            void ReceivedVideoData_H265(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                //if (fs_v != null)
                {
                    foreach (var nalUnitMem in dataArgs.Data)
                    {
                        var nalUnit = nalUnitMem.Span;
                        // Output some H264 stream information
                        if (nalUnit.Length > 5)
                        {
                            int nal_unit_type = (nalUnit[4] >> 1) & 0x3F;
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
                            //logger.LogInformation("NAL Type = {nal_unit_type} {description}", nal_unit_type, description);
                        }
                        //fs_v.Write(nalUnit);
                        mp4Builder?.ProcessTrackSample(1, nalUnit.ToArray().Skip(4).ToArray());
                    }
                }
            }
            ;
            client.SetupVideoPayload(ProfileH265, ReceivedVideoData_H265);
        }

        private static void NewH264Stream(NewStreamEventArgs args, RTSPClient client)
        {
            //string now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            //string filename = "rtsp_capture_" + now + ".264";
            //FileStream fs_v = new(filename, FileMode.Create);
            if (args.StreamConfigurationData is H264StreamConfigurationData h264StreamConfigurationData)
            {
                foreach (var data in h264StreamConfigurationData.OutOfBandNal)
                {
                    //WriteNalToFileIfNotEmpty(fs_v, data);
                }
            }

            void ReceivedVideoData_H264(RTSPClient client, SimpleDataEventArgs dataArgs)
            {
                foreach (var nalUnitMem in dataArgs.Data)
                {
                    var nalUnit = nalUnitMem.Span;
                    // Output some H264 stream information
                    if (nalUnit.Length > 5)
                    {
                        int nal_ref_idc = (nalUnit[4] >> 5) & 0x03;
                        int nal_unit_type = nalUnit[4] & 0x1F;
                        string description = nal_unit_type switch
                        {
                            1 => "NON IDR NAL",
                            5 => "IDR NAL",
                            6 => "SEI NAL",
                            7 => "SPS NAL",
                            8 => "PPS NAL",
                            9 => "ACCESS UNIT DELIMITER NAL",
                            _ => "OTHER NAL",
                        };
                        logger.LogInformation("NAL Ref = {nal_ref_idc} NAL Type = {nal_unit_type} {description}", nal_ref_idc, nal_unit_type, description);
                    }
                    //fs_v.Write(nalUnit);
                }
            }
            ;
            client.SetupVideoPayload(ProfileH264, ReceivedVideoData_H264);
        }

        private static void WriteNalToFileIfNotEmpty(FileStream fs_v, ReadOnlySpan<byte> nal)
        {
            if (nal.IsEmpty) return;
            // Write Start Code
            fs_v.Write(halStartCode);
            fs_v.Write(nal);
        }
    }
}
