using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorderAndroidApp.Sinks;

public sealed class LocalFileSink : IStorageSink
{
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly ILogger<LocalFileSink> _logger;

    public string Name => "LocalFile";

    public LocalFileSink(IOptions<CameraRecorderSettings> options, ILogger<LocalFileSink> logger)
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

        try
        {

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {

                var mimeType = Path.GetExtension(fileName) switch
                {
                    ".wav" => "audio/wav",
                    ".aac" => "audio/aac",
                    _ => "video/mp4",
                };

                var context = Android.App.Application.Context;
                var resolver = context.ContentResolver!;
                Android.Content.ContentValues contentValues = new();
                contentValues.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
                contentValues.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, mimeType);
                contentValues.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Download");
                //contentValues.Put(Android.Provider.MediaStore.IMediaColumns.Data, Path.Combine(_settings.LocalRecordingsPath, fileName));
                var uri = Path.GetExtension(fileName) == ".mp4" ? Android.Provider.MediaStore.Video.Media.ExternalContentUri : Android.Provider.MediaStore.Audio.Media.ExternalContentUri;
                var imageUri = resolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, contentValues);

                using var os = resolver.OpenOutputStream(imageUri);
                await stream.CopyToAsync(os!);
                _logger.LogInformation("Файл сохранён локально: {Path} {fileName}", imageUri, fileName);
            }
            else
            {
                string path = Path.Combine(_settings.LocalRecordingsPath, fileName);
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                   bufferSize: 81920, useAsync: true);
                await stream.CopyToAsync(fs, ct);
                _logger.LogInformation("Файл сохранён локально: {Path}", path);
            }


        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LocalFileSink: ошибка сохранения {FileName}", fileName);
        }
    }
}
