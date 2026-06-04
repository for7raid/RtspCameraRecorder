namespace CameraRecorder.Settings;

/// <summary>
/// Настройки скриншотов при записи
/// </summary>
public sealed class ScreenshotSettings
{
    /// <summary>Скриншоты активны</summary>
    public bool Enabled { get; init; }

    /// <summary>Массив задержек от начала записи в секундах (напр. [3, 5, 10])</summary>
    public int[] TimestampsSec { get; init; } = [3, 5, 10];
}
