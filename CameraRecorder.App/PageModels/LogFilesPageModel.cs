using CameraRecorder.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CameraRecorder.App.PageModels;

public partial class LogFilesPageModel : ObservableObject
{
    private static readonly string LogsDir =
        Path.Combine(FileSystem.AppDataDirectory, "logs");

    [ObservableProperty]
    private ObservableCollection<LogFileInfo> _logFiles = [];

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand]
    private async Task Appearing()
    {
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await LoadFilesAsync();
    }

    [RelayCommand]
    private async Task OpenFile(LogFileInfo? file)
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
            System.Diagnostics.Debug.WriteLine($"Ошибка открытия файла: {ex.Message}");
            await AppShell.DisplaySnackbarAsync($"Не удалось открыть {file.FileName}");
        }
    }

    [RelayCommand]
    private async Task DeleteFile(LogFileInfo? file)
    {
        if (file is null) return;

        try
        {
            File.Delete(file.FullPath);
            LogFiles.Remove(file);
            IsEmpty = LogFiles.Count == 0;
            await AppShell.DisplayToastAsync($"Удалён: {file.FileName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка удаления файла: {ex.Message}");
            await AppShell.DisplaySnackbarAsync($"Не удалось удалить {file.FileName}");
        }
    }

    [RelayCommand]
    private async Task DeleteAll()
    {
        var confirm = await Shell.Current.DisplayAlertAsync(
            "Удалить всё?",
            "Все файлы логов будут удалены безвозвратно.",
            "Удалить", "Отмена");

        if (!confirm) return;


        foreach (var file in LogFiles.ToList())
        {
            try
            {
                File.Delete(file.FullPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка удаления: {ex.Message}");
            }
        }
        LogFiles.Clear();
        IsEmpty = true;
        await AppShell.DisplayToastAsync("Все логи удалены");

    }

    private Task LoadFilesAsync()
    {
        try
        {
            IsLoading = true;

            if (!Directory.Exists(LogsDir))
            {
                LogFiles = [];
                IsEmpty = true;
                return Task.CompletedTask;
            }

            var files = Directory.GetFiles(LogsDir, "*.txt")
                .Select(f =>
                {
                    var fi = new FileInfo(f);
                    return new LogFileInfo
                    {
                        FileName = fi.Name,
                        FullPath = fi.FullName,
                        SizeBytes = fi.Length,
                        LastModified = fi.LastWriteTime
                    };
                })
                .OrderByDescending(f => f.LastModified)
                .ToList();

            LogFiles = new ObservableCollection<LogFileInfo>(files);
            IsEmpty = LogFiles.Count == 0;
        }
        finally
        {
            IsLoading = false;
        }

        return Task.CompletedTask;
    }
}
