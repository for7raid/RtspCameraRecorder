namespace CameraRecorder
{
    public interface IFramesDumper
    {
        void ProcessFrames(List<VideoFrame> videoFrames, List<AudioFrame> audioFrames);
    }
}