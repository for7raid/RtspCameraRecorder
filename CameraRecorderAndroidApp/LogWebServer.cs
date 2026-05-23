using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CameraRecorderAndroidApp;

public class LogWebServer
{
    private readonly HttpListener _listener = new();
    private readonly string _logsDir;
    private readonly ILogger<LogWebServer> _logger;

    public LogWebServer(int port, string logsDir, ILogger<LogWebServer> logger)
    {
        _logsDir = logsDir;
        _logger = logger;
        _listener.Prefixes.Add($"http://+:{port}/");
        _listener.Prefixes.Add($"http://*:{port}/");
    }

    public void Start()
    {
        _listener.Start();

        _logger.LogInformation("Веб-сервер логов запущен на порту {Port}",
            _listener.Prefixes.First().Replace("+:", ""));
        _ = ListenLoop();
    }

    public void Stop()
    {
        _listener.Stop();
        _logger.LogInformation("Веб-сервер логов остановлен");
    }

    private async Task ListenLoop()
    {
        while (_listener.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = Task.Run(async () => await HandleRequest(ctx));
            }
            catch (HttpListenerException) { break; }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url!.AbsolutePath.TrimEnd('/');
        var remote = ctx.Request.RemoteEndPoint?.ToString() ?? "?";

        if (path == "/logs")
        {
            _logger.LogInformation("Запрос списка логов от {Remote}", remote);
            ServeLogList(ctx);
        }
        else if (path.StartsWith("/logs/") && ctx.Request.HttpMethod == "GET")
        {
            var fileName = Uri.UnescapeDataString(path.Substring(6));
            _logger.LogInformation("Запрос файла лога '{File}' от {Remote}", fileName, remote);
            ServeLogFile(ctx, fileName);
        }
        else if (path.StartsWith("/logs/") && ctx.Request.HttpMethod == "DELETE")
        {
            var fileName = Uri.UnescapeDataString(path.Substring(6));
            _logger.LogInformation("Запрос удаления лога '{File}' от {Remote}", fileName, remote);
            ServeDeleteLog(ctx, fileName);
        }
        else if (path == "" && ctx.Request.HttpMethod == "POST")
        {
            await HandleAlarmSignal(ctx);
        }
        else
        {
            _logger.LogWarning("Неизвестный запрос '{Path}' от {Remote}", path, remote);
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
    }

    private async Task HandleAlarmSignal(HttpListenerContext ctx)
    {
        try
        {
            Stream stream = ctx.Request.InputStream;


            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string jsonString = Encoding.UTF8.GetString(buffer, 0, bytesRead).Replace("\0", "");
                _logger.LogInformation(jsonString);
                var payload = JsonSerializer.Deserialize<CameraEvent>(jsonString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload?.Type == "Motion Detect")
                {
                    if (payload.Status == 1)
                    {
                        _logger.LogInformation("Detect motion on camera");
                    }
                    else if (payload.Status == 0)
                    {
                        _logger.LogInformation("Ent motion on camera");
                    }
                }

            }

            ctx.Response.StatusCode = 202;
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error communicating with client");
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
        }
    }

    private void ServeLogList(HttpListenerContext ctx)
    {
        if (!Directory.Exists(_logsDir))
        {
            _logger.LogDebug("Директория логов не найдена: {Dir}", _logsDir);
            WriteHtml(ctx, LoadTemplate().Replace("{{ROWS}}", "<tr><td colspan='3'>Нет логов</td></tr>"));
            return;
        }

        var files = Directory.GetFiles(_logsDir, "*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => LoadRowTemplate()
                .Replace("{{URL}}", Uri.EscapeDataString(f.Name))
                .Replace("{{NAME}}", f.Name)
                .Replace("{{SIZE}}", FormatSize(f.Length)))
            .ToArray();

        var rows = files.Length > 0 ? string.Join("\n", files) : "<tr><td colspan='2'>Нет логов</td></tr>";
        WriteHtml(ctx, LoadTemplate().Replace("{{ROWS}}", rows));
    }

    private static string LoadTemplate()
    {
        using var stream = Android.App.Application.Context.Resources!.OpenRawResource(Resource.Raw.logs_template);
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadRowTemplate()
    {
        using var stream = Android.App.Application.Context.Resources!.OpenRawResource(Resource.Raw.logs_row_template);
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B" :
        bytes < 1048576 ? $"{bytes / 1024.0:F1} KB" :
        $"{bytes / 1048576.0:F1} MB";

    private static void WriteHtml(HttpListenerContext ctx, string html)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    private void ServeLogFile(HttpListenerContext ctx, string fileName)
    {
        var path = Path.Combine(_logsDir, fileName);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Файл лога не найден: {File}", fileName);
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        ctx.Response.ContentType = "text/plain; charset=utf-8";
        using var fs = File.OpenRead(path);
        fs.CopyTo(ctx.Response.OutputStream);
        ctx.Response.Close();
    }

    private void ServeDeleteLog(HttpListenerContext ctx, string fileName)
    {
        var filePath = Path.Combine(_logsDir, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Лог удалён: {File}", fileName);
            ctx.Response.StatusCode = 200;
        }
        else
        {
            _logger.LogWarning("Файл для удаления не найден: {File}", fileName);
            ctx.Response.StatusCode = 404;
        }
        ctx.Response.Close();
    }

    sealed record CameraEvent(string Type, int Status);
}
