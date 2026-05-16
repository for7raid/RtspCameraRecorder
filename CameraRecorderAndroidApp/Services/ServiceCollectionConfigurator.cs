using CameraRecorder;
using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMP4.Common;

namespace CameraRecorderAndroidApp.Services
{
    internal static class ServiceCollectionConfigurator
    {


        static ServiceCollectionConfigurator()
        {
            var services = new ServiceCollection();


            services.AddTransient<RingBufferVideoStorage>();
            services.AddTransient<RingBufferAudioStorage>();
            services.AddTransient<RTSPClient>();
            services.AddTransient<RtspRecorder>();
            services.AddTransient<RtspMotionDetector>();
            services.AddTransient<IMp4Logger, Mp4Logger>();

            services.AddTransient<IStorageSink, LocalFileSink_>();
            services.AddTransient<IStorageSink, FtpSink>();

            services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    //.AddFilter("CameraRecorder.MotionAnalyzers.MotionDetectionResult", LogLevel.Debug)
                    .AddDebug()
                    //.AddSimpleConsole(o =>
                    //{
                    //    o.SingleLine = false;
                    //})
                    //.AddFile($@"C:\temp\camera\log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt")
                    ;
            });

            var configuration = new ConfigurationBuilder()
                    .AddJsonFile("camerarecorder.settings.json", optional: true, reloadOnChange: true)
                    .Build();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddOptions();


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
