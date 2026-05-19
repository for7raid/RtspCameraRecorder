using CameraRecorder;
using CameraRecorder.MotionAnalyzers;
using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMP4.Common;
using System;
using System.Diagnostics;
using System.Threading;

namespace RtspClientExample
{
    public class Recorder
    {
        private static ILogger logger = null!;
        static void Main()
        {

            var services = new ServiceCollection();


            services.AddTransient<RingBufferStorage>();
            services.AddTransient<RingBufferAudioStorage>();
            services.AddTransient<RTSPClient>();
            services.AddTransient<RtspRecorder>();
            services.AddTransient<RtspMotionDetector>();
            services.AddTransient<IMp4Logger, Mp4Logger>();
            services.AddSingleton<StaticSettingsProvider>();
            services.AddSingleton<IH26xDecoder, H264SharpDecoder>();
            //            services.AddTransient(sp => Options.Create(sp.GetRequiredService<StaticSettingsProvider>().GetSettings()));

            services.AddTransient<IStorageSink, LocalFileSink_>();
            services.AddTransient<IStorageSink, FtpSink>();

            services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddFilter("CameraRecorder.MotionAnalyzer_", LogLevel.Debug)
                    .AddSimpleConsole(o =>
                    {
                        o.SingleLine = false;
                    })
                    .AddFile($@"C:\temp\camera\log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt")
                    ;
            });

            var configuration = new ConfigurationBuilder()
                    .AddUserSecrets<Recorder>()
                    .AddJsonFile("camerarecorder.settings.json", optional: true, reloadOnChange: true)
                    .Build();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOptions();
            services.Configure<CameraRecorderSettings>(configuration);


            // Строим провайдер
            var serviceProvider = services.BuildServiceProvider();

            // Получаем сервис
            var recorder = serviceProvider.GetRequiredService<RtspRecorder>();
            var rtspMotionDetector = serviceProvider.GetRequiredService<RtspMotionDetector>();
            logger = serviceProvider.GetRequiredService<ILogger<Recorder>>();

            rtspMotionDetector.MotionDetected += () => recorder.StartRecord();
            rtspMotionDetector.MotionEnded += () => recorder.StopRecordAsync();

            recorder.Start();
            rtspMotionDetector.Start();

            Console.WriteLine("Press ENTER to exit");
            Stopwatch stopwatch = new Stopwatch();
            ConsoleKeyInfo key = default;
            while (key.Key != ConsoleKey.Q && !recorder.StreamingFinished)
            {
                while (!Console.KeyAvailable && !recorder.StreamingFinished)
                {
                    // Avoid maxing out CPU on systems that instantly return null for ReadLine
                    Thread.Sleep(250);
                }
                if (Console.KeyAvailable)
                {
                    key = Console.ReadKey();
                }
                if (key.Key == ConsoleKey.R)
                {
                    logger.LogInformation($"Start recording {DateTime.Now:HH:mm:ss}");
                    recorder.StartRecord();
                    stopwatch.Start();
                }
                else if (key.Key == ConsoleKey.S)
                {
                    stopwatch.Stop();
                    logger.LogInformation($"Stop recording {stopwatch.Elapsed}");
                    recorder.StopRecordAsync();
                }
            }

            recorder.Stop();

            Console.ReadLine();
        }
    }
}
