using Android.Runtime;
using CameraRecorder;
using CameraRecorder.RTSP;
using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using CameraRecorderAndroidApp.Activities;
using CameraRecorderAndroidApp.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace CameraRecorderAndroidApp.Services
{
    internal static class ServiceCollectionConfigurator
    {


        static ServiceCollectionConfigurator()
        {
            string logsDir = Path.Combine(Android.App.Application.Context.CacheDir!.AbsolutePath, "logs");
            Directory.CreateDirectory(logsDir);

            var services = new ServiceCollection();

            services.AddTransient<RingBufferStorage>();

            services.AddTransient<RTSPClient>();
            services.AddTransient<RtspRecorder>();
            services.AddTransient<RtspMotionDetector>();

            services.AddTransient<IFramesDumper, SharpMP4MuxerDumper>();

            services.AddTransient<IStorageSink, AndroidLocalFileSink>();
            services.AddTransient<IStorageSink, FtpSink>();

            services.AddTransient<IH26xDecoder, H265Decoder>();

            services.AddTransient<IScreenshotCapturer, ScreenshotCapture>();

            services.AddSingleton<LogWebServer>();
            services.AddSingleton<AlarmWebServer>();

            services.AddKeyedSingleton("LogsDirectory", (_, _) => logsDir);

            services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddFilter("CameraRecorderAndroidApp", LogLevel.Information)
                    //.AddFilter("CameraRecorder.MotionAnalyzers.MotionDetectionResult", LogLevel.Debug)
                    .AddDebug()
                    //.AddFile(Path.Combine(Application.Context.FilesDir!.AbsolutePath, $@"\logs\log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt"), fileSizeLimitBytes: 1_048_576 /*1Mb*/)
                    ;

                var log = new LoggerConfiguration()
                    .WriteTo.File(
                        path: Path.Combine(logsDir, $@"log.txt"),
                        rollingInterval: RollingInterval.Day,
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

            var logger = Instance.GetRequiredService<ILogger<MainActivity>>();

            // 1. Для исключений в .NET потоках (менее критично, но добавить стоит)
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                logger.LogCritical(args.ExceptionObject as Exception, "Критическая ошибка.");
                // Не пытаемся здесь "спасти" приложение
            };

            // 2. Для исключений в Task (например, забыли await)
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                logger.LogCritical(args.Exception, "Критическая ошибка.");
                args.SetObserved(); // Подавляем стандартное поведение (краш)
            };

            // 3. ГЛАВНОЕ: для исключений в Java/UI потоке
            AndroidEnvironment.UnhandledExceptionRaiser += (sender, args) =>
            {
                // Логируем ошибку
                logger.LogCritical(args.Exception, "Критическая ошибка.");


                // ОСТОРОЖНО: Говорим системе, что мы сами обработали ошибку
                // Это может предотвратить закрытие приложения, но может привести к багам.
                args.Handled = true;
            };
        }

        public static ServiceProvider Instance { get; private set; }
    }
}
