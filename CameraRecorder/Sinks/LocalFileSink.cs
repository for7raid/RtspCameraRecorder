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

    public async void SaveAsync(string fileName, byte[] data, CancellationToken ct)
    {
        var _settings = _options.Value;
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

        using var stream = new MemoryStream(data);


        var fullPath = Path.Combine(_settings.LocalRecordingsPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(fs, ct);
        _logger.LogInformation("Файл сохранён локально: {Path}", fullPath);

    }
}
