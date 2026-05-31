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
        var ftp = _options.Value.Ftp;

        if (ftp is not { Enabled: true })
        {
            _logger.LogDebug("FtpSink: FTP отключён, пропускаю");
            return;
        }

        if (string.IsNullOrWhiteSpace(ftp.Host))
        {
            _logger.LogDebug("FtpSink: хост не задан, пропускаю");
            return;
        }

        var remotePath = BuildRemotePath(ftp, fileName);

        _logger.LogDebug("FtpSink: загружаю {FileName} на {Path}", fileName, remotePath);

        try
        {
            using var client = CreateClient(ftp);
            await client.AutoConnect();
            await client.UploadBytes(data, remotePath, createRemoteDir: true);
            await client.Disconnect();

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
        var ftp = _options.Value.Ftp;

        if (ftp is not { Enabled: true })
            return (tmpDataFilePath, false);

        var remotePath = BuildRemotePath(ftp, fileName);

        try
        {
            using var client = CreateClient(ftp);
            await client.AutoConnect();
            await client.UploadFile(tmpDataFilePath, remotePath, createRemoteDir: true);
            await client.Disconnect();

            _logger.LogInformation("Файл загружен на FTP: {Path}", remotePath);
            return (tmpDataFilePath, false);
        }
        catch (FtpException ex)
        {
            _logger.LogError(ex, "FtpSink: ошибка FTP при загрузке {FileName}", fileName);
            return (tmpDataFilePath, false);
        }
    }

    // ── helpers ──

    private static AsyncFtpClient CreateClient(FtpSettings ftp)
    {
        var client = new AsyncFtpClient(ftp.Host, ftp.Login, ftp.Password);

        if (ftp.UseFtps)
        {
            client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            client.Config.ValidateAnyCertificate = true;
        }

        client.Config.ConnectTimeout = 15_000;
        client.Config.DataConnectionConnectTimeout = 15_000;
        client.Config.DataConnectionReadTimeout = 30_000;
        client.Config.SocketKeepAlive = true;
        client.Config.RetryAttempts = 2;

        return client;
    }

    private static string BuildRemotePath(FtpSettings ftp, string fileName)
    {
        var dir = (ftp.Directory ?? string.Empty).TrimEnd('/');
        var name = fileName.StartsWith('/') ? fileName : "/" + fileName;
        return dir + name;
    }
}
