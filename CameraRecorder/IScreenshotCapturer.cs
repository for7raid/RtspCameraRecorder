namespace CameraRecorder;

/// <summary>
/// Контракт захвата скриншотов. Вызывается RtspRecorder-ом при старте/остановке записи.
/// </summary>
public interface IScreenshotCapturer
{
    void OnRecordStarted(DateTime startedAt);
    void OnRecordStopped();
}
