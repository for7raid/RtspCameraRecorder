namespace CameraRecorder;

public class VideoFrame
{
    public byte[] Data { get; set; } // H.265 NAL Unit
    public DateTime Timestamp { get; set; }
    public NalUnitType UnitType { get; set; }
}