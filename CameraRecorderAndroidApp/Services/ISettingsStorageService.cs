using CameraRecorder.Settings;

namespace CameraRecorderAndroidApp.Services
{
    public interface ISettingsStorageService
    {
        Task<CameraRecorderSettings> LoadAsync();
        Task SaveAsync(CameraRecorderSettings settings);
    }
}