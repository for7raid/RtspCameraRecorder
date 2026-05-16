namespace CameraRecorder;

public interface IH26xDecoder
{
    event EventHandler<DecodedFrameEventArgs> FrameDecoded;

    void DecodeFrame(byte[] h265Data, long timestampUs = 0, NalUnitType nalUnitType = NalUnitType.Unknown);
}