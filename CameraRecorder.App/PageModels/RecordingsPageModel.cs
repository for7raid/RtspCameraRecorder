using CameraRecorder.App.Models;
using CameraRecorder.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CameraRecorder.App.PageModels;

public partial class RecordingsPageModel : ObservableObject
{
    private readonly ISettingsProvider _settingsProvider;

    [ObservableProperty]
    private ObservableCollection<VideoFileInfo> _recordings = [];

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isLoading;

    public RecordingsPageModel(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    [RelayCommand]
    private async Task Appearing()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenFile(VideoFileInfo? file)
    {
        if (file is null) return;

        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(file.FullPath)
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка открытия: {ex.Message}");
            await AppShell.DisplaySnackbarAsync($"Не удалось открыть {file.FileName}");
        }
    }

    [RelayCommand]
    private async Task DeleteFile(VideoFileInfo? file)
    {
        if (file is null) return;

        try
        {
            File.Delete(file.FullPath);
            Recordings.Remove(file);
            IsEmpty = Recordings.Count == 0;
            await AppShell.DisplayToastAsync($"Удалён: {file.FileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка удаления: {ex.Message}");
            await AppShell.DisplaySnackbarAsync($"Не удалось удалить {file.FileName}");
        }
    }

    [RelayCommand]
    private async Task DeleteAll()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Удалить всё?",
            "Все записи будут удалены безвозвратно.",
            "Удалить", "Отмена");

        if (!confirm) return;

        try
        {
            foreach (var file in Recordings.ToList())
            {
                File.Delete(file.FullPath);
            }
            Recordings.Clear();
            IsEmpty = true;
            await AppShell.DisplayToastAsync("Все записи удалены");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка удаления: {ex.Message}");
        }
    }

    private Task LoadAsync()
    {
        try
        {
            IsLoading = true;

            var settings = _settingsProvider.GetSettings();
            var dir = settings.LocalRecordingsPath;
#if ANDROID

    Recordings = new ObservableCollection<VideoFileInfo>();

    if (OperatingSystem.IsAndroidVersionAtLeast(29))
    {
    var contentResolver = Platform.AppContext?.ContentResolver;
    Android.Net.Uri collectionUri = Android.Provider.MediaStore.Video.Media.ExternalContentUri;
    
    // Какие колонки хотим получить
    string[] projection = { Android.Provider.MediaStore.Video.Media.InterfaceConsts.Data,
                            Android.Provider.MediaStore.Video.Media.InterfaceConsts.DisplayName,
                            Android.Provider.MediaStore.Video.Media.InterfaceConsts.Size, 
                            Android.Provider.MediaStore.Video.Media.InterfaceConsts.DateAdded
                            };
    // Filter to only get videos from DCIM/Camera
    string selection = Android.Provider.MediaStore.Video.Media.InterfaceConsts.RelativePath + " LIKE ?";
    string[] selectionArgs = new string[]{ settings.LocalRecordingsPath + "%"};  
    // Sort by date taken
    string sortOrder = Android.Provider.MediaStore.Video.Media.InterfaceConsts.DateAdded + " DESC";

    using var cursor = contentResolver?.Query(
        collectionUri, 
        projection, 
        selection,
        selectionArgs, sortOrder);
    
        if (cursor != null)
        {
            var dataColumn = cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Video.Media.InterfaceConsts.Data);
            var titleColumn = cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Video.Media.InterfaceConsts.DisplayName);
            var sizeColumn = cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Video.Media.InterfaceConsts.Size);
            var dateTakenColumn = cursor.GetColumnIndexOrThrow(Android.Provider.MediaStore.Video.Media.InterfaceConsts.DateAdded);
            
            while (cursor.MoveToNext())
            {
                var filePath = cursor.GetString(dataColumn);
                var fileTitle = cursor.GetString(titleColumn);
                var fileSize = cursor.GetLong(sizeColumn);
                var fileDate = cursor.GetLong(dateTakenColumn);

                if (fileSize > 0)
                {
                   Recordings.Add(new VideoFileInfo
                    {
                        FileName = fileTitle,
                        FullPath = filePath,
                        SizeBytes = fileSize,
                        Created = DateTimeOffset.FromUnixTimeSeconds(fileDate).DateTime
                        
                    });
                }
            }
        }
    }
    else {
        DirectGetFiles(dir);
    }
    

#else
            DirectGetFiles(dir);
#endif
            IsEmpty = Recordings.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }

        return Task.CompletedTask;
    }

    private void DirectGetFiles(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            Recordings = [];
            return;
        }

        var files = Directory.GetFiles(dir, "*.*")
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".mp4" or ".wav";
            })
            .Select(f =>
            {
                var fi = new FileInfo(f);
                return new VideoFileInfo
                {
                    FileName = fi.Name,
                    FullPath = fi.FullName,
                    SizeBytes = fi.Length,
                    Created = fi.CreationTime
                };
            })
            .OrderByDescending(f => f.Created)
            .ToList();

        Recordings = new ObservableCollection<VideoFileInfo>(files);
    }
}
