using CameraRecorder.Settings;

namespace CameraRecorder;

public class StaticSettingsProvider : ISettingsProvider
{
    private readonly CameraRecorderSettings _settings;

    public StaticSettingsProvider()
    {
        _settings = new CameraRecorderSettings
        {
            RtspUrl = "rtsp://192.168.1.8:554/stream1",
            RtspLogin = "admin",
            RtspPassword = "123456",
            LocalRecordingsPath = $@"c:\temp\camera\",
            FtpHost = "ftp.example.com",
            FtpLogin = "ftpuser",
            FtpPassword = "ftppass",
            UseFtps = true
        };
    }

    public StaticSettingsProvider(CameraRecorderSettings settings)
    {
        _settings = settings;
    }

    public CameraRecorderSettings GetSettings() => _settings;
}
