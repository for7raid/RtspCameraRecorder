using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SharpMP4.Common;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Syncfusion.Maui.Toolkit.Hosting;

namespace CameraRecorder.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .UseSkiaSharp()
                .ConfigureSyncfusionToolkit()
                .ConfigureMauiHandlers(handlers =>
                {
#if WINDOWS
    				Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping("KeyboardAccessibleCollectionView", (handler, view) =>
    				{
    					handler.PlatformView.SingleSelectionFollowsFocus = false;
    				});
#endif
                })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
                    fonts.AddFont("FluentSystemIcons-Regular.ttf", FluentUI.FontFamily);
                });

#if DEBUG
            builder.Logging.AddDebug();
            builder.Services.AddLogging(configure => configure.AddDebug());
#endif

            builder.Services.AddTransient<RingBufferVideoStorage>();
            builder.Services.AddTransient<RingBufferAudioStorage>();
            builder.Services.AddTransient<RTSPClient>();
            builder.Services.AddTransient<RtspRecorder>();
            builder.Services.AddTransient<RtspViewer>();
            builder.Services.AddTransient<IMp4Logger, Mp4Logger>();

            builder.Services.AddTransient<IStorageSink, LocalFileSink>();
            builder.Services.AddTransient<IStorageSink, FtpSink>();

            builder.Services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddFilter("CameraRecorder.MotionAnalyzer_", LogLevel.Debug)
                    .AddFile(Path.Combine(FileSystem.AppDataDirectory, "logs", $"log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}-{Environment.OSVersion}.txt"));
            });



            builder.Services.AddSingleton<ModalErrorHandler>();
            builder.Services.AddSingleton<MainPageModel>();
            builder.Services.AddSingleton<RecordingsPageModel>();
            builder.Services.AddSingleton<LogFilesPageModel>();
            builder.Services.AddSingleton<SettingsPageModel>();

            // Настройки: один инстанс — и для провайдера, и для хранилища
            builder.Services.AddSingleton<SettingsStorageService>();
            builder.Services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<SettingsStorageService>());
            builder.Services.AddSingleton<ISettingsStorageService>(sp => sp.GetRequiredService<SettingsStorageService>());

            return builder.Build();
        }
    }
}
