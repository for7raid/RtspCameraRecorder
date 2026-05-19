namespace CameraRecorder;
public class AudioFrame
{
    public byte[] Data { get; set; }
    public DateTime Timestamp { get; set; }
    public ulong RtpTimestamp { get; set; }
}