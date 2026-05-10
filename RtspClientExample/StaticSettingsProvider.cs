using CameraRecorder.Settings;
using Microsoft.Extensions.Configuration;

namespace CameraRecorder;

public class StaticSettingsProvider : ISettingsProvider
{
    private readonly CameraRecorderSettings _settings;
    private readonly IConfigurationRoot _config;

    public StaticSettingsProvider()
    {
        
        _config = new ConfigurationBuilder()
        .AddUserSecrets<StaticSettingsProvider>()
        .Build();
        
        _settings = new CameraRecorderSettings
        {
            RtspUrl = "rtsp://192.168.1.8:554/stream1",
            RtspLogin = "admin",
            RtspPassword = "123456",
            LocalRecordingsPath = $@"c:\temp\camera\",
            FtpHost = _config["FtpHost"],
            FtpLogin = _config["FtpLogin"],
            FtpPassword = _config["FtpPassword"],
            FtpDirectory = "/rec",
            UseFtps = false
        };

    }

    public CameraRecorderSettings GetSettings() => _settings;
}
