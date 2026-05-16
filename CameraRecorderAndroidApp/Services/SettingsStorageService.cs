using CameraRecorder.Settings;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CameraRecorderAndroidApp.Services;

public class SettingsStorageService : IOptions<CameraRecorderSettings>, ISettingsStorageService
{
    private static readonly string FilePath =
        Path.Combine(Application.Context.FilesDir!.AbsolutePath, "camerarecorder.settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private CameraRecorderSettings _cached;

    public CameraRecorderSettings Value => _cached;

    public SettingsStorageService()
    {
        _cached = LoadFromFile();
    }


    public Task<CameraRecorderSettings> LoadAsync()
    {
        _cached = LoadFromFile();
        return Task.FromResult(_cached);
    }

    public async Task SaveAsync(CameraRecorderSettings settings)
    {
        _cached = settings;
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json);
    }

    // ── private ──
    private static CameraRecorderSettings LoadFromFile()
    {
        if (!File.Exists(FilePath))
            return CameraRecorderSettings.Default;

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<CameraRecorderSettings>(json, JsonOptions)
                   ?? new CameraRecorderSettings();
        }
        catch (JsonException)
        {
            return CameraRecorderSettings.Default;
        }
    }
}
