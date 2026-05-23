using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CameraRecorderAndroidApp;

public class LogWebServer
{
    private readonly HttpListener _listener = new();
    private readonly string _logsDir;
    private readonly string _recDir;
    private readonly ILogger<LogWebServer> _logger;

    public LogWebServer(int port, string logsDir, string recDir, ILogger<LogWebServer> logger)
    {
        _logsDir = logsDir;
        _logger = logger;
        _listener.Prefixes.Add($"http://+:{port}/");
        _listener.Prefixes.Add($"http://*:{port}/");

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            _recDir = Path.Combine(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath, recDir);
        }
        else
        {
            _recDir = recDir;
        }
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
        else if (path == "/rec")
        {
            _logger.LogInformation("Запрос списка записей от {Remote}", remote);
            ServeRecList(ctx);
        }
        else if (path == "/rec/playlist.m3u")
        {
            _logger.LogInformation("Запрос плейлиста от {Remote}", remote);
            ServeRecPlaylist(ctx);
        }
        else if (path.StartsWith("/rec/") && ctx.Request.HttpMethod == "GET")
        {
            var fileName = Uri.UnescapeDataString(path.Substring(5));
            ServeRecFile(ctx, fileName);
        }
        else if (path.StartsWith("/rec/") && ctx.Request.HttpMethod == "DELETE")
        {
            var fileName = Uri.UnescapeDataString(path.Substring(5));
            _logger.LogInformation("Запрос удаления записи '{File}' от {Remote}", fileName, remote);
            ServeDeleteRec(ctx, fileName);
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
            WriteHtml(ctx, LoadTemplate(Resource.Raw.logs_template).Replace("{{ROWS}}", "<tr><td colspan='3'>Нет логов</td></tr>"));
            return;
        }

        var files = Directory.GetFiles(_logsDir, "*.txt")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => LoadTemplate(Resource.Raw.logs_row_template)
                .Replace("{{URL}}", Uri.EscapeDataString(f.Name))
                .Replace("{{NAME}}", f.Name)
                .Replace("{{SIZE}}", FormatSize(f.Length)))
            .ToArray();

        var rows = files.Length > 0 ? string.Join("\n", files) : "<tr><td colspan='2'>Нет логов</td></tr>";
        WriteHtml(ctx, LoadTemplate(Resource.Raw.logs_template).Replace("{{ROWS}}", rows));
    }

    private static string LoadTemplate(int resourceId)
    {
        using var stream = Android.App.Application.Context.Resources!.OpenRawResource(resourceId);
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B" :
        bytes < 1048576 ? $"{bytes / 1024.0:F1} KB" :
        $"{bytes / 1048576.0:F1} MB";

    private static int ParseDurationFromFileName(string fileName)
    {
        // Ищем " 45sec" или " 45sec.mp4" в конце имени
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)sec", System.Text.RegularExpressions.RegexOptions.RightToLeft);
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
    }

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

    private void ServeRecList(HttpListenerContext ctx)
    {
        if (!Directory.Exists(_recDir))
        {
            WriteHtml(ctx, LoadTemplate(Resource.Raw.rec_template).Replace("{{ROWS}}", "<tr><td colspan='3'>Нет записей</td></tr>"));
            return;
        }

        var files = Directory.GetFiles(_recDir, "*.*")
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp4" or ".wav")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTime)
            .Select(f => LoadTemplate(Resource.Raw.rec_row_template)
                .Replace("{{URL}}", Uri.EscapeDataString(f.Name))
                .Replace("{{NAME}}", f.Name)
                .Replace("{{SIZE}}", FormatSize(f.Length)))
            .ToArray();

        var rows = files.Length > 0 ? string.Join("\n", files) : "<tr><td colspan='3'>Нет записей</td></tr>";
        WriteHtml(ctx, LoadTemplate(Resource.Raw.rec_template).Replace("{{ROWS}}", rows));
    }

    private void ServeRecFile(HttpListenerContext ctx, string fileName)
    {
        var path = Path.Combine(_recDir, fileName);
        if (!File.Exists(path))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var fileInfo = new FileInfo(path);
        long total = fileInfo.Length;

        ctx.Response.ContentType = fileName.EndsWith(".mp4") ? "video/mp4" : "audio/wav";
        ctx.Response.AddHeader("Accept-Ranges", "bytes");

        var range = ctx.Request.Headers["Range"];
        if (range != null && range.StartsWith("bytes="))
        {
            var parts = range.Substring(6).Split('-');
            long start = long.Parse(parts[0]);
            long end = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? long.Parse(parts[1]) : total - 1;
            end = Math.Min(end, total - 1);
            long length = end - start + 1;

            ctx.Response.StatusCode = 206;
            ctx.Response.AddHeader("Content-Range", $"bytes {start}-{end}/{total}");
            ctx.Response.ContentLength64 = length;

            using var fs = File.OpenRead(path);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[81920];
            long remaining = length;
            while (remaining > 0)
            {
                int read = fs.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
                if (read == 0) break;
                ctx.Response.OutputStream.Write(buf, 0, read);
                remaining -= read;
            }
        }
        else
        {
            ctx.Response.ContentLength64 = total;
            using var fs = File.OpenRead(path);
            fs.CopyTo(ctx.Response.OutputStream);
        }
        ctx.Response.Close();
    }

    private void ServeDeleteRec(HttpListenerContext ctx, string fileName)
    {
        var filePath = Path.Combine(_recDir, fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.LogInformation("Запись удалена: {File}", fileName);
            ctx.Response.StatusCode = 200;
        }
        else
        {
            _logger.LogWarning("Запись для удаления не найдена: {File}", fileName);
            ctx.Response.StatusCode = 404;
        }
        ctx.Response.Close();
    }

    private void ServeRecPlaylist(HttpListenerContext ctx)
    {
        if (!Directory.Exists(_recDir))
        {
            ctx.Response.StatusCode = 200;
            WriteText(ctx, "# Нет записей\n");
            return;
        }

        var cutoff = DateTime.Now.AddDays(-1);
        var files = Directory.GetFiles(_recDir, "*.*")
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".mp4" or ".wav")
            .Select(f => new FileInfo(f))
            .Where(f => f.CreationTime >= cutoff)
            .OrderBy(f => f.CreationTime)  // старые первыми
            .ToArray();

        var baseUrl = $"http://{ctx.Request.Url!.Host}:{ctx.Request.Url.Port}/rec/";
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");
        sb.AppendLine("#EXTENC: UTF-8");
        sb.AppendLine();
        foreach (var f in files)
        {
            // Длительность из имени файла: "2026-05-13 20.38.00 45sec.mp4" → 45
            int duration = ParseDurationFromFileName(f.Name);
            sb.AppendLine($"#EXTINF:{duration},{f.Name}");
            sb.AppendLine($"{baseUrl}{Uri.EscapeDataString(f.Name)}");
        }

        ctx.Response.ContentType = "audio/x-mpegurl; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    private static void WriteText(HttpListenerContext ctx, string text)
    {
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    sealed record CameraEvent(string Type, int Status);
}
