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

            _logger.LogInformation("Файл загружен на FTP: {Path} ({Size} байт)", remotePath, data.Length);

            await TruncateStorage(client, ftp);
            await client.Disconnect();
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

            _logger.LogInformation("Файл загружен на FTP: {Path}", remotePath);

            await TruncateStorage(client, ftp);
            await client.Disconnect();

            return (tmpDataFilePath, false);
        }
        catch (FtpException ex)
        {
            _logger.LogError(ex, "FtpSink: ошибка FTP при загрузке {FileName}", fileName);
            return (tmpDataFilePath, false);
        }
    }

    // ── очистка хранилища ──

    private async Task TruncateStorage(AsyncFtpClient client, FtpSettings ftp)
    {
        var remoteDir = string.IsNullOrEmpty(ftp.Directory)
            ? "/"
            : ftp.Directory.TrimEnd('/');

        try
        {
            // Получаем список файлов с размерами и датами изменения
            var items = await client.GetListing(remoteDir);
            var files = items
                .Where(i => i.Type == FtpObjectType.File)
                .ToList();

            if (files.Count == 0)
                return;

            // Сортируем от старых к новым (MinValue = дата неизвестна, идут первыми)
            files.Sort((a, b) => a.Modified.CompareTo(b.Modified));

            int deletedCount = 0;
            long deletedBytes = 0;

            // 1. Удаляем файлы старше MaxFileAgeDays
            if (ftp.MaxFileAgeDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-ftp.MaxFileAgeDays);

                for (int i = files.Count - 1; i >= 0; i--)
                {
                    var fileDate = files[i].Modified;
                    // Пропускаем файлы без даты (сервер не вернул Modified)
                    if (fileDate > DateTime.MinValue && fileDate < cutoff)
                    {
                        _logger.LogDebug("FtpSink: удаляю устаревший {File} ({Age} дн.)",
                            files[i].Name, (DateTime.UtcNow - fileDate).Days);

                        await client.DeleteFile(files[i].FullName);
                        deletedCount++;
                        deletedBytes += files[i].Size;
                        files.RemoveAt(i);
                    }
                }
            }

            // 2. Если превышен лимит по размеру — удаляем старейшие файлы
            if (ftp.MaxStorageSizeMb > 0)
            {
                long maxBytes = (long)ftp.MaxStorageSizeMb * 1024 * 1024;
                long totalSize = files.Sum(f => f.Size);

                // Удаляем с начала списка (самые старые), пока не войдём в лимит
                while (totalSize > maxBytes && files.Count > 0)
                {
                    var oldest = files[0];
                    _logger.LogDebug("FtpSink: удаляю по лимиту размера {File} ({Size} МБ, остаток {Remaining} МБ)",
                        oldest.Name, oldest.Size / (1024 * 1024), (totalSize - oldest.Size) / (1024 * 1024));

                    await client.DeleteFile(oldest.FullName);
                    deletedCount++;
                    deletedBytes += oldest.Size;
                    totalSize -= oldest.Size;
                    files.RemoveAt(0);
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "FtpSink: очистка хранилища — удалено {Count} файлов ({Size} МБ)",
                    deletedCount, deletedBytes / (1024 * 1024));
            }
        }
        catch (FtpException ex)
        {
            _logger.LogWarning(ex, "FtpSink: ошибка при очистке хранилища");
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
