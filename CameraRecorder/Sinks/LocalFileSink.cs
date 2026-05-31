using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorder.Sinks;

public sealed class LocalFileSink_ : IStorageSink
{
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly ILogger<LocalFileSink_> _logger;

    public string Name => "LocalFile";

    public LocalFileSink_(IOptions<CameraRecorderSettings> options, ILogger<LocalFileSink_> logger)
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

        var fullPath = System.IO.Path.Combine(local.Path, fileName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);

        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(fs);
        _logger.LogInformation("Файл сохранён локально: {Path}", fullPath);

        TruncateStorage(local);
    }

    public async Task<(string newFilePath, bool isMoved)> SaveAsync(string fileName, string tmpDataFilePath)
    {
        await SaveAsync(fileName, File.ReadAllBytes(tmpDataFilePath));
        return (tmpDataFilePath, false);
    }

    // ── очистка хранилища ──

    private void TruncateStorage(LocalStorageSettings local)
    {
        var dir = local.Path;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;

        try
        {
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
