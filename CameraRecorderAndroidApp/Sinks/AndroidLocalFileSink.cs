using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorderAndroidApp.Sinks;

public sealed class AndroidLocalFileSink : IStorageSink
{
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly ILogger<AndroidLocalFileSink> _logger;

    public string Name => "LocalFile";

    public AndroidLocalFileSink(IOptions<CameraRecorderSettings> options, ILogger<AndroidLocalFileSink> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SaveAsync(string fileName, byte[] data)
    {
        var local = _options.Value.LocalStorage;

        if (local is not { Enabled: true })
        {
            _logger.LogDebug("LocalFileSink: локальное хранилище отключено, пропускаю");
            return;
        }

        if (string.IsNullOrWhiteSpace(local.Path))
        {
            _logger.LogDebug("LocalFileSink: путь не задан, пропускаю");
            return;
        }

        using var stream = new MemoryStream(data);

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var mimeType = Path.GetExtension(fileName) switch
                {
                    ".wav" => "audio/wav",
                    ".aac" => "audio/aac",
                    _ => "video/mp4",
                };

                var context = Android.App.Application.Context;
                var resolver = context.ContentResolver!;
                Android.Content.ContentValues contentValues = new();
                contentValues.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
                contentValues.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, mimeType);
                contentValues.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, local.Path);

                var imageUri = resolver.Insert(Android.Provider.MediaStore.Video.Media.ExternalContentUri, contentValues);

                using var os = resolver.OpenOutputStream(imageUri);
                await stream.CopyToAsync(os!);
                _logger.LogInformation("Файл сохранён локально: {Path} {fileName}", imageUri, fileName);
            }
            else
            {
                string path = Path.Combine(local.Path, fileName);
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 81920, useAsync: true);
                await stream.CopyToAsync(fs);
                _logger.LogInformation("Файл сохранён локально: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalFileSink: ошибка сохранения {FileName}", fileName);
            return; // не чистим после ошибки
        }

        TruncateStorage(local);
    }

    public async Task<(string newFilePath, bool isMoved)> SaveAsync(string fileName, string tmpDataFilePath)
    {
        var local = _options.Value.LocalStorage;

        if (local is not { Enabled: true })
        {
            _logger.LogDebug("LocalFileSink: локальное хранилище отключено, пропускаю");
            return (tmpDataFilePath, false);
        }

        if (string.IsNullOrWhiteSpace(local.Path))
        {
            _logger.LogDebug("LocalFileSink: путь не задан, пропускаю.");
            return (tmpDataFilePath, false);
        }

        try
        {
            string path = Path.Combine(local.Path, fileName);

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                path = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, local.Path, fileName);
            }

            File.Move(tmpDataFilePath, path);
            _logger.LogInformation("Файл сохранён локально: {Path}.", path);
            tmpDataFilePath = path;

            TruncateStorage(local);
            return (path, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Операции над файлами недоступы.");
            await SaveAsync(fileName, File.ReadAllBytes(tmpDataFilePath));
            return (tmpDataFilePath, false);
        }
    }

    // ── очистка хранилища ──

    private void TruncateStorage(LocalStorageSettings local)
    {
        string? dir = null;

        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                dir = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, local.Path);
            }
            else
            {
                dir = local.Path;
            }

            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;

            var files = Directory.GetFiles(dir)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            if (files.Count == 0)
                return;

            int deletedCount = 0;
            long deletedBytes = 0;

            // 1. Удаляем файлы старше MaxFileAgeDays
            if (local.MaxFileAgeDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-local.MaxFileAgeDays);

                for (int i = files.Count - 1; i >= 0; i--)
                {
                    if (files[i].LastWriteTimeUtc < cutoff)
                    {
                        _logger.LogDebug("LocalFileSink: удаляю устаревший {File} ({Age} дн.)",
                            files[i].Name, (DateTime.UtcNow - files[i].LastWriteTimeUtc).Days);

                        deletedBytes += files[i].Length;
                        files[i].Delete();
                        deletedCount++;
                        files.RemoveAt(i);
                    }
                }
            }

            // 2. Если превышен лимит по размеру — удаляем старейшие файлы
            if (local.MaxStorageSizeMb > 0)
            {
                long maxBytes = (long)local.MaxStorageSizeMb * 1024 * 1024;
                long totalSize = files.Sum(f => f.Length);

                while (totalSize > maxBytes && files.Count > 0)
                {
                    var oldest = files[0];
                    _logger.LogDebug("LocalFileSink: удаляю по лимиту размера {File} ({Size} МБ, остаток {Remaining} МБ)",
                        oldest.Name, oldest.Length / (1024 * 1024), (totalSize - oldest.Length) / (1024 * 1024));

                    deletedBytes += oldest.Length;
                    totalSize -= oldest.Length;
                    oldest.Delete();
                    deletedCount++;
                    files.RemoveAt(0);
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "LocalFileSink: очистка хранилища — удалено {Count} файлов ({Size} МБ)",
                    deletedCount, deletedBytes / (1024 * 1024));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LocalFileSink: ошибка при очистке хранилища");
        }
    }
}
