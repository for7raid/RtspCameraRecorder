using CameraRecorder.Settings;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorder.Sinks;

public sealed class FtpSink : IStorageSink
{
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly ILogger<FtpSink> _logger;

    public string Name => "FTP";

    public FtpSink(IOptions<CameraRecorderSettings> options, ILogger<FtpSink> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SaveAsync(string fileName, byte[] data)
    {
        var settings = _options.Value;

        if (!settings.FtpEnabled)
        {
            _logger.LogDebug("FtpSink: FTP отключён, пропускаю");
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.FtpHost))
        {
            _logger.LogDebug("FtpSink: хост не задан, пропускаю");
            return;
        }

        var remotePath = BuildRemotePath(settings, fileName);

        _logger.LogDebug("FtpSink: загружаю {FileName} на {Path}", fileName, remotePath);

        try
        {
            using var ftp = CreateClient(settings);

            await ftp.AutoConnect();

            await ftp.UploadBytes(data, remotePath, createRemoteDir: true);

            await ftp.Disconnect();

            _logger.LogInformation("Файл загружен на FTP: {Path} ({Size} байт)", remotePath, data.Length);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FtpSink: загрузка отменена {FileName}", fileName);
        }
        catch (FtpException ex)
        {
            _logger.LogError(ex, "FtpSink: ошибка FTP: {FileName}", fileName);
        }
    }

    public async Task<(string newFilePath, bool isMoved)> SaveAsync(string fileName, string tmpDataFilePath)
    {
        var settings = _options.Value;
        var remotePath = BuildRemotePath(settings, fileName);

        try
        {
            using var ftp = CreateClient(settings);

            await ftp.AutoConnect();

            await ftp.UploadFile(tmpDataFilePath, remotePath, createRemoteDir: true);

            await ftp.Disconnect();

            _logger.LogInformation("Файл загружен на FTP: {Path}", remotePath);

            return (tmpDataFilePath, false);
        }
        catch (FtpException ex)
        {
            _logger.LogError(ex, "FtpSink: ошибка FTP при загрузке {FileName}",
                fileName);

            return (tmpDataFilePath, false);
        }
    }

    // ── helpers ──

    private static AsyncFtpClient CreateClient(CameraRecorderSettings settings)
    {
        var client = new AsyncFtpClient(settings.FtpHost, settings.FtpLogin, settings.FtpPassword);

        if (settings.UseFtps)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            client.Config.ValidateAnyCertificate = true;   // для самоподписанных сертификатов
        }

        client.Config.ConnectTimeout = 15_000;
        client.Config.DataConnectionConnectTimeout = 15_000;
        client.Config.DataConnectionReadTimeout = 30_000;
        client.Config.SocketKeepAlive = true;
        client.Config.RetryAttempts = 2;

        return client;
    }

    private static string BuildRemotePath(CameraRecorderSettings s, string fileName)
    {
        var dir = (s.FtpDirectory ?? string.Empty).TrimEnd('/');
        var name = fileName.StartsWith('/') ? fileName : "/" + fileName;
        return dir + name;
    }

    private static string? GetDirectoryName(string path)
    {
        var idx = path.LastIndexOf('/');
        return idx > 0 ? path[..idx] : null;
    }
}
