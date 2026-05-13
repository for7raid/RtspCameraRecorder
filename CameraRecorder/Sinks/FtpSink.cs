using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

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

    public async Task SaveAsync(string fileName, Stream stream, CancellationToken ct)
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

        var uri = BuildUri(settings, fileName);

        _logger.LogDebug("FtpSink: загружаю {FileName} на {Uri}", fileName, uri);

        try
        {
#pragma warning disable SYSLIB0014
            var request = (FtpWebRequest)WebRequest.Create(uri);
#pragma warning restore SYSLIB0014
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(settings.FtpLogin, settings.FtpPassword);
            request.EnableSsl = settings.UseFtps;
            request.UsePassive = true;
            request.KeepAlive = false;
            request.Timeout = 30_000;
            request.ReadWriteTimeout = 30_000;

            // Отмена через токен
            using var ctr = ct.Register(() => request.Abort());

            stream.Position = 0;
            using var requestStream = await request.GetRequestStreamAsync();
            await stream.CopyToAsync(requestStream, ct);

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            _logger.LogInformation("Файл загружен на FTP: {Uri} — {Status}", uri, response.StatusDescription);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("FtpSink: загрузка отменена {FileName}", fileName);
        }
        catch (WebException ex) when (ex.Status == WebExceptionStatus.RequestCanceled)
        {
            _logger.LogWarning("FtpSink: загрузка отменена {FileName}", fileName);
        }
        catch (WebException ex)
        {
            var ftpResponse = ex.Response as FtpWebResponse;
            _logger.LogError(ex, "FtpSink: ошибка FTP ({Status}): {FileName}",
                ftpResponse?.StatusDescription ?? ex.Status.ToString(), fileName);
        }
    }

    private Uri BuildUri(CameraRecorderSettings settings, string fileName)
    {
        var scheme = settings.UseFtps ? "ftps" : "ftp";
        var host = settings.FtpHost.TrimEnd('/');
        var path = settings.FtpDirectory + (fileName.StartsWith('/') ? fileName : "/" + fileName);
        return new Uri($"{scheme}://{host}{path}");
    }
}
