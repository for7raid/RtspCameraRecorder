using CameraRecorder;
using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using CameraRecorderAndroidApp.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using SharpMP4.Common;

namespace CameraRecorderAndroidApp.Services
{
    internal static class ServiceCollectionConfigurator
    {


        static ServiceCollectionConfigurator()
        {
            string logsDir = Path.Combine(Android.App.Application.Context.FilesDir!.AbsolutePath, "logs");
            Directory.CreateDirectory(logsDir);

            var services = new ServiceCollection();

            services.AddTransient<LocalFileSink>();
            services.AddTransient<RingBufferStorage>();
            services.AddTransient<RingBufferAudioStorage>();
            services.AddTransient<RTSPClient>();
            services.AddTransient<RtspRecorder>();
            services.AddTransient<RtspMotionDetector>();
            services.AddTransient<IMp4Logger, Mp4Logger>();

            services.AddTransient<IFramesDumper, AndroidMuxedDumper>();

            services.AddTransient<IStorageSink, LocalFileSink>();
            //services.AddTransient<IStorageSink, FtpSink>();

            services.AddSingleton<IWavToAACConverter, MuLawToAACConverter>();

            services.AddKeyedSingleton<IH26xDecoder>("OnScreenDecoder", (sp, _) => { return new H265Decoder(2650, 1440, sp.GetRequiredService<ILogger<H265Decoder>>()); });
            services.AddKeyedSingleton<IH26xDecoder>("OnBufferDecoder", (sp, _) => { return new H265Decoder(640, 480, sp.GetRequiredService<ILogger<H265Decoder>>()); });

            services.AddSingleton<LogWebServer>(sp => new LogWebServer(8080, logsDir, sp.GetRequiredService<ILogger<LogWebServer>>()));

            services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    //.AddFilter("CameraRecorder.MotionAnalyzers.MotionDetectionResult", LogLevel.Debug)
                    .AddDebug()
                    //.AddFile(Path.Combine(Application.Context.FilesDir!.AbsolutePath, $@"\logs\log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt"), fileSizeLimitBytes: 1_048_576 /*1Mb*/)
                    ;

                var log = new LoggerConfiguration()
                    .WriteTo.File(
                        path: Path.Combine(logsDir, $@"log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt"),
                        rollingInterval: RollingInterval.Infinite,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 1_048_576/*1Mb*/,
                        buffered: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(3))
                    .CreateLogger();
                builder.AddSerilog(log);

            });

            //var configuration = new ConfigurationBuilder()
            //        .AddJsonFile("camerarecorder.settings.json", optional: true, reloadOnChange: true)
            //        .Build();
            //services.AddSingleton<IConfiguration>(configuration);
            //services.AddOptions();


            //services.AddTransient(sp => Options.Create(CameraRecorderSettings.Default));

            services.AddSingleton<SettingsStorageService>();
            services.AddSingleton<IOptions<CameraRecorderSettings>>(sp => sp.GetRequiredService<SettingsStorageService>());
            services.AddSingleton<ISettingsStorageService>(sp => sp.GetRequiredService<SettingsStorageService>());


            // Строим провайдер
            Instance = services.BuildServiceProvider();
        }

        public static ServiceProvider Instance { get; private set; }
    }
}
