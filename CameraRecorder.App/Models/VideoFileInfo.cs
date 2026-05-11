namespace CameraRecorder.App.Models;

public class VideoFileInfo
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime Created { get; init; }

    public string FileType => Path.GetExtension(FileName).ToLowerInvariant() switch
    {
        ".mp4" => "🎬 Видео",
        ".wav" => "🎵 Аудио",
        _ => "📄 Файл"
    };

    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / (1024.0 * 1024.0):F1} MB"
    };

    public string CreatedDisplay => Created.ToString("dd.MM.yyyy HH:mm:ss");
}
