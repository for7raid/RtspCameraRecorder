namespace CameraRecorder.Settings;

public class CameraRecorderSettings
{
    /// <summary>
    /// Хост RTSP-потока (IP или домен)
    /// </summary>
    public string RtspUrl { get; init; } = string.Empty;

    /// <summary>
    /// Логин для RTSP-аутентификации
    /// </summary>
    public string RtspLogin { get; init; } = string.Empty;

    /// <summary>
    /// Пароль для RTSP-аутентификации
    /// </summary>
    public string RtspPassword { get; init; } = string.Empty;

    /// <summary>
    /// Локальный путь для хранения записей
    /// </summary>
    public string LocalRecordingsPath { get; init; } = string.Empty;

    /// <summary>
    /// Хост FTP-сервера
    /// </summary>
    public string FtpHost { get; init; } = string.Empty;

    /// <summary>
    /// Логин для FTP
    /// </summary>
    public string FtpLogin { get; init; } = string.Empty;

    /// <summary>
    /// Пароль для FTP
    /// </summary>
    public string FtpPassword { get; init; } = string.Empty;

    /// <summary>
    /// Использовать FTPS (FTP over SSL/TLS)
    /// </summary>
    public bool UseFtps { get; init; }

    /// <summary>
    /// Длительность записи до начала движения (размер кольцевого буфера, секунд)
    /// </summary>
    public int PreMotionDurationSec { get; init; } = 10;

    /// <summary>
    /// Длительность записи после окончания движения (секунд)
    /// </summary>
    public int PostMotionDurationSec { get; init; } = 10;
}
