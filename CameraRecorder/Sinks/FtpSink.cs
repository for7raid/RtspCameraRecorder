using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;
using System.Configuration;
using System.Net;

namespace CameraRecorder.Sinks;

public sealed class FtpSink : IStorageSink
{
    private readonly CameraRecorderSettings _settings;
    private readonly ILogger<FtpSink> _logger;

    public string Name => "FTP";

    public FtpSink(ISettingsProvider settingsProvider, ILogger<FtpSink> logger)
    {
        _settings = settingsProvider.GetSettings();
        _logger = logger;
    }

    public async Task SaveAsync(string fileName, Stream stream, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.FtpHost))
        {
            _logger.LogDebug("FtpSink: хост не задан, пропускаю");
            return;
        }

        //_logger.LogInformation("Файл загружен на FTP: {Uri} — {Status}", uri, response.StatusDescription);
    }
}
