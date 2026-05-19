namespace CameraRecorder;

public class VideoFrame
{
    public byte[] Data { get; set; } // H.265 NAL Unit
    public DateTime Timestamp { get; set; }
    public ulong RtpTimestamp { get; set; }
    public NalUnitType UnitType { get; set; }

    public bool IsConfigFrame => UnitType is NalUnitType.H265_SPS or NalUnitType.H265_VPS or NalUnitType.H265_PPS;
    public bool IsKeyFrame => UnitType is NalUnitType.H265_IDR_W_RADL or NalUnitType.H265_IDR_N_LP;
}