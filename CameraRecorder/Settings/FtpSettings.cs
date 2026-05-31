namespace CameraRecorder.Settings;

/// <summary>
/// Настройки FTP-хранилища
/// </summary>
public sealed class FtpSettings
{
    /// <summary>FTP-хранилище активно</summary>
    public bool Enabled { get; init; }

    /// <summary>Хост FTP-сервера</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Логин для FTP</summary>
    public string Login { get; init; } = string.Empty;

    /// <summary>Пароль для FTP</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Путь на FTP-сервере для загрузки файлов</summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>Использовать FTPS (FTP over SSL/TLS)</summary>
    public bool UseFtps { get; init; }

    /// <summary>Срок хранения файлов, не более (дней)</summary>
    public int MaxFileAgeDays { get; init; } = 10;

    /// <summary>Максимальный размер хранилища (МБ)</summary>
    public int MaxStorageSizeMb { get; init; } = 2048;
}
