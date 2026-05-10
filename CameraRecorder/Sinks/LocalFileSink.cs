using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;

namespace CameraRecorder.Sinks;

public sealed class LocalFileSink : IStorageSink
{
    private readonly CameraRecorderSettings _settings;
    private readonly ILogger<LocalFileSink> _logger;

    public string Name => "LocalFile";

    public LocalFileSink(ISettingsProvider settingsProvider, ILogger<LocalFileSink> logger)
    {
        _settings = settingsProvider.GetSettings();
        _logger = logger;
    }

    public async Task SaveAsync(string fileName, Stream stream, CancellationToken ct)
    {
        if (!_settings.LocalStorageEnabled)
        {
            _logger.LogDebug("LocalFileSink: локальное хранилище отключено, пропускаю");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.LocalRecordingsPath))
        {
            _logger.LogDebug("LocalFileSink: путь не задан, пропускаю");
            return;
        }

        var fullPath = Path.Combine(_settings.LocalRecordingsPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        stream.Position = 0;
        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(fs, ct);
        _logger.LogInformation("Файл сохранён локально: {Path}", fullPath);
    }
}
