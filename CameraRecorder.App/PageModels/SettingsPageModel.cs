using CameraRecorder.App.Services;
using CameraRecorder.Settings;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CameraRecorder.App.PageModels;

public partial class SettingsPageModel : ObservableObject
{
    private readonly ISettingsStorageService _storage;

    // ── RTSP ──
    [ObservableProperty]
    private string _rtspUrl = string.Empty;

    [ObservableProperty]
    private string _rtspLogin = string.Empty;

    [ObservableProperty]
    private string _rtspPassword = string.Empty;

    // ── Локальное хранилище ──
    [ObservableProperty]
    private bool _localStorageEnabled = true;

    [ObservableProperty]
    private string _localRecordingsPath = string.Empty;

    // ── FTP ──
    [ObservableProperty]
    private bool _ftpEnabled;

    [ObservableProperty]
    private string _ftpHost = string.Empty;

    [ObservableProperty]
    private string _ftpLogin = string.Empty;

    [ObservableProperty]
    private string _ftpPassword = string.Empty;

    [ObservableProperty]
    private bool _useFtps;

    [ObservableProperty]
    private string _ftpDirectory = string.Empty;

    // ── Запись ──
    [ObservableProperty]
    private int _preMotionDurationSec = 10;

    [ObservableProperty]
    private int _postMotionDurationSec = 10;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    public SettingsPageModel(ISettingsStorageService storage)
    {
        _storage = storage;
    }

    [RelayCommand]
    private async Task Appearing()
    {
        try
        {
            IsLoading = true;
            var settings = await _storage.LoadAsync();

            RtspUrl = settings.RtspUrl;
            RtspLogin = settings.RtspLogin;
            RtspPassword = settings.RtspPassword;
            LocalStorageEnabled = settings.LocalStorageEnabled;
            LocalRecordingsPath = settings.LocalRecordingsPath;
            FtpEnabled = settings.FtpEnabled;
            FtpHost = settings.FtpHost;
            FtpLogin = settings.FtpLogin;
            FtpPassword = settings.FtpPassword;
            UseFtps = settings.UseFtps;
            FtpDirectory = settings.FtpDirectory;
            PreMotionDurationSec = settings.PreMotionDurationSec;
            PostMotionDurationSec = settings.PostMotionDurationSec;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            IsSaving = true;

            var settings = new CameraRecorderSettings
            {
                RtspUrl = RtspUrl,
                RtspLogin = RtspLogin,
                RtspPassword = RtspPassword,
                LocalStorageEnabled = LocalStorageEnabled,
                LocalRecordingsPath = LocalRecordingsPath,
                FtpEnabled = FtpEnabled,
                FtpHost = FtpHost,
                FtpLogin = FtpLogin,
                FtpPassword = FtpPassword,
                UseFtps = UseFtps,
                FtpDirectory = FtpDirectory,
                PreMotionDurationSec = PreMotionDurationSec,
                PostMotionDurationSec = PostMotionDurationSec
            };

            await _storage.SaveAsync(settings);
            await AppShell.DisplayToastAsync("Настройки сохранены");
            SemanticScreenReader.Announce("Настройки сохранены");
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task PickLocalPath()
    {
        try
        {
            var result = await FolderPicker.Default.PickAsync(CancellationToken.None);
            if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.Folder?.Path))
            {
                LocalRecordingsPath = result.Folder.Path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка выбора папки: {ex.Message}");
        }
    }
}
