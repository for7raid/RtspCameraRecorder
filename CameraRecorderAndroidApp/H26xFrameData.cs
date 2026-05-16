using Android.Media;
using CameraRecorder;

namespace CameraRecorderAndroidApp;

/// <summary>
/// Входной кадр с H.26x данными
/// </summary>
public class H26xFrameData
{
    public byte[] Data { get; set; }
    public long TimestampUs { get; set; }
    public NalUnitType NalUnitType { get; internal set; }
    public MediaCodecBufferFlags CodeFlag { get; internal set; }
}
