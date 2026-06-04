namespace CameraRecorder.Settings;

public class CameraRecorderSettings
{
    /// <summary>
    /// Путь основного RTSP-потока (IP или домен)
    /// </summary>
    public string RtspMainStreamUrl { get; init; } = string.Empty;

    /// <summary>
    /// Путь вспомогательного RTSP-потока (IP или домен) для детекции движения
    /// </summary>
    public string RtspSubStreamUrl { get; init; } = string.Empty;

    /// <summary>
    /// Логин для RTSP-аутентификации
    /// </summary>
    public string RtspLogin { get; init; } = string.Empty;

    /// <summary>
    /// Пароль для RTSP-аутентификации
    /// </summary>
    public string RtspPassword { get; init; } = string.Empty;

    /// <summary>
    /// Настройки локального хранилища. Если null — локальное сохранение не используется.
    /// </summary>
    public LocalStorageSettings? LocalStorage { get; init; }

    /// <summary>
    /// Настройки скриншотов. Если null — скриншоты не делаются.
    /// </summary>
    public ScreenshotSettings? Screenshots { get; init; }

    /// <summary>
    /// Настройки FTP-хранилища. Если null — FTP не используется.
    /// </summary>
    public FtpSettings? Ftp { get; init; }

    /// <summary>
    /// Длительность записи до начала движения (размер кольцевого буфера, секунд)
    /// </summary>
    public int PreMotionDurationSec { get; init; } = 10;

    /// <summary>
    /// Длительность записи после окончания движения (секунд)
    /// </summary>
    public int PostMotionDurationSec { get; init; } = 10;

    public static CameraRecorderSettings Default { get; } = new()
    {
        RtspMainStreamUrl = "rtsp://192.168.1.8:554/stream1",
        RtspSubStreamUrl = "rtsp://192.168.1.8:554/stream2",
        RtspLogin = "admin",
        RtspPassword = "123456",
        LocalStorage = new LocalStorageSettings
        {
            Enabled = true,
            Path = "DCIM/camera",
        },
    };
}
