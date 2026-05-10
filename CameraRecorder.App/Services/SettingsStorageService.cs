using CameraRecorder.Settings;
using System.Text.Json;

namespace CameraRecorder.App.Services;

public class SettingsStorageService : ISettingsStorageService
{
    private static readonly string FilePath =
        Path.Combine(FileSystem.AppDataDirectory, "camerarecorder.settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<CameraRecorderSettings> LoadAsync()
    {
        if (!File.Exists(FilePath))
            return new CameraRecorderSettings();

        try
        {
            var json = await File.ReadAllTextAsync(FilePath);
            return JsonSerializer.Deserialize<CameraRecorderSettings>(json, JsonOptions)
                   ?? new CameraRecorderSettings();
        }
        catch (JsonException)
        {
            return new CameraRecorderSettings();
        }
    }

    public async Task SaveAsync(CameraRecorderSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(FilePath, json);
    }
}
