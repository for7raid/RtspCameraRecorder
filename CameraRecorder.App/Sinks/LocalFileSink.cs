using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorder.Sinks;

public sealed class LocalFileSink : IStorageSink
{
    private readonly CameraRecorderSettings _settings;
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly ILogger<LocalFileSink> _logger;

    public string Name => "LocalFile";

    public LocalFileSink(IOptions<CameraRecorderSettings> options, ILogger<LocalFileSink> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SaveAsync(string fileName, Stream stream, CancellationToken ct)
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

        stream.Position = 0;

#if ANDROID
        var context = Platform.CurrentActivity;

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            Android.Content.ContentResolver resolver = context.ContentResolver;
            Android.Content.ContentValues contentValues = new();
            contentValues.Put(Android.Provider.MediaStore.Video.Media.InterfaceConsts.DisplayName, fileName);
            //contentValues.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
            contentValues.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
            contentValues.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "video/mp4");
            contentValues.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, _settings.LocalRecordingsPath);
            Android.Net.Uri imageUri = resolver.Insert(Android.Provider.MediaStore.Video.Media.ExternalContentUri, contentValues);

            using var os = resolver.OpenOutputStream(imageUri);
            await stream.CopyToAsync(os);
            _logger.LogInformation("Файл сохранён локально: {Path} {fileName}", imageUri, fileName);
        }
        else
        {
            string path = System.IO.Path.Combine(_settings.LocalRecordingsPath, fileName);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
               bufferSize: 81920, useAsync: true);
            await stream.CopyToAsync(fs, ct);
            _logger.LogInformation("Файл сохранён локально: {Path}", path);
        }

#endif
#if WINDOWS
        var fullPath = Path.Combine(_settings.LocalRecordingsPath, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        await stream.CopyToAsync(fs, ct);
        _logger.LogInformation("Файл сохранён локально: {Path}", fullPath);
#endif

    }
}
