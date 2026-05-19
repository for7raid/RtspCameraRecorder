namespace CameraRecorder;
public interface IWavToAACConverter
{
    byte[] Convert(byte[] input);
}