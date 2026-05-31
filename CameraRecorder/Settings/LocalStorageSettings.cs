namespace CameraRecorder.Settings;

/// <summary>
/// Настройки локального хранилища
/// </summary>
public sealed class LocalStorageSettings
{
    /// <summary>Локальное хранилище активно</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Путь для сохранения записей</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Срок хранения файлов, не более (дней)</summary>
    public int MaxFileAgeDays { get; init; } = 20;

    /// <summary>Максимальный размер хранилища (МБ)</summary>
    public int MaxStorageSizeMb { get; init; } = 10 * 1024;
}
