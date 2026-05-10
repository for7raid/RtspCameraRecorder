using CameraRecorder.Settings;

namespace CameraRecorder.App.Services;

public interface ISettingsStorageService
{
    Task<CameraRecorderSettings> LoadAsync();
    Task SaveAsync(CameraRecorderSettings settings);
}
