using CameraRecorder.Settings;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace CameraRecorder;

public class StaticSettingsProvider
{
    private readonly CameraRecorderSettings _settings;
    private readonly IConfigurationRoot _config;

    public StaticSettingsProvider()
    {

        _config = new ConfigurationBuilder()
        .AddUserSecrets<StaticSettingsProvider>()
        .AddJsonFile("camerarecorder.settings.json", optional: true, reloadOnChange: true)
        .Build();

        _settings = new CameraRecorderSettings
        {
            RtspUrl = _config["RtspUrl"] ?? "rtsp://192.168.1.8:554/stream1",
            RtspLogin = _config["RtspLogin"] ?? "admin",
            RtspPassword = _config["RtspPassword"] ?? "123456",
            //LocalStorageEnabled = _config.Get<bool>("LocalStorageEnabled") ?? true,
            LocalRecordingsPath = _config["LocalRecordingsPath"] ?? $@"c:\temp\camera\",
            //FtpEnabled = _config["FtpEnabled"] ?? false,
            FtpHost = _config["FtpHost"],
            FtpLogin = _config["FtpLogin"],
            FtpPassword = _config["FtpPassword"],
            FtpDirectory = _config["FtpDirectory"] ?? "/rec",
            //UseFtps = _config["UseFtps"] ?? false
        };

    }

    public CameraRecorderSettings GetSettings() => _settings;
}
