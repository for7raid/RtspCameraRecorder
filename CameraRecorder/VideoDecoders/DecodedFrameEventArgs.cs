namespace CameraRecorder;

/// <summary>
/// Аргументы события декодирования кадра
/// </summary>
public class DecodedFrameEventArgs : EventArgs
{
    public DecodedVideoFrame Frame { get; set; }
    public IH26xDecoder Decoder { get; set; }
}