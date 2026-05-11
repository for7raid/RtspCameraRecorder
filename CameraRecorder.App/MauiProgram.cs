using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using SharpMP4.Common;
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
                .ConfigureSyncfusionToolkit()
                .ConfigureMauiHandlers(handlers =>
                {
#if WINDOWS
    				Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping("KeyboardAccessibleCollectionView", (handler, view) =>
    				{
    					handler.PlatformView.SingleSelectionFollowsFocus = false;
    				});

    				Microsoft.Maui.Handlers.ContentViewHandler.Mapper.AppendToMapping(nameof(Pages.Controls.CategoryChart), (handler, view) =>
    				{
    					if (view is Pages.Controls.CategoryChart && handler.PlatformView is Microsoft.Maui.Platform.ContentPanel contentPanel)
    					{
    						contentPanel.IsTabStop = true;
    					}
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
            builder.Services.AddTransient<IMp4Logger, Mp4Logger>();

            builder.Services.AddTransient<IStorageSink, LocalFileSink>();
            builder.Services.AddTransient<IStorageSink, FtpSink>();

            //builder.Configuration
            //        .AddJsonFile("appsettings.json", optional: false);


            builder.Services.AddLogging(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("RtspClientExample", LogLevel.Debug)
                    .AddFilter("Rtsp", LogLevel.Debug)
                    .AddFilter("CameraRecorder.MotionAnalyzer_", LogLevel.Debug)
                    .AddFile(Path.Combine(FileSystem.AppDataDirectory, "logs", $"log-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.txt"));
            });


            builder.Services.AddSingleton<ProjectRepository>();
            builder.Services.AddSingleton<TaskRepository>();
            builder.Services.AddSingleton<CategoryRepository>();
            builder.Services.AddSingleton<TagRepository>();
            builder.Services.AddSingleton<SeedDataService>();
            builder.Services.AddSingleton<ModalErrorHandler>();
            builder.Services.AddSingleton<MainPageModel>();
            builder.Services.AddSingleton<ProjectListPageModel>();
            builder.Services.AddSingleton<ManageMetaPageModel>();
            builder.Services.AddSingleton<SettingsPageModel>();

            // Настройки: один инстанс — и для провайдера, и для хранилища
            builder.Services.AddSingleton<SettingsStorageService>();
            builder.Services.AddSingleton<ISettingsProvider>(sp => sp.GetRequiredService<SettingsStorageService>());
            builder.Services.AddSingleton<ISettingsStorageService>(sp => sp.GetRequiredService<SettingsStorageService>());

            builder.Services.AddTransientWithShellRoute<ProjectDetailPage, ProjectDetailPageModel>("project");
            builder.Services.AddTransientWithShellRoute<TaskDetailPage, TaskDetailPageModel>("task");

            return builder.Build();
        }
    }
}
