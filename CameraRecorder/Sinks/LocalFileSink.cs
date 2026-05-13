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
        contentValues.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        contentValues.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "video/mp4");
        contentValues.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, _settings.LocalRecordingsPath);
        Android.Net.Uri imageUri = resolver.Insert(Android.Provider.MediaStore.Video.Media.ExternalContentUri, contentValues);
           
        var os = resolver.OpenOutputStream(imageUri);
        await stream.CopyToAsync(os);
        os.Flush();
        os.Close();
        _logger.LogInformation("Файл сохранён локально: {Path}", imageUri);
    }
    else
    {
        Java.IO.File storagePath = Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryPictures);
        string path = System.IO.Path.Combine(storagePath.ToString(), "image.png");
       
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
