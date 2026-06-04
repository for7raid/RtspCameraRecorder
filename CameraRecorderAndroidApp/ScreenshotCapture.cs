using Android.Graphics;
using Android.Views;
using CameraRecorder;
using CameraRecorder.Sinks;
using CameraRecorder.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CameraRecorderAndroidApp;

public class ScreenshotCapture : IScreenshotCapturer
{
    private readonly IEnumerable<IStorageSink> _sinks;
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly ILogger<ScreenshotCapture> _logger;
    private IH26xDecoder? _decoder;

    private TextureView? _textureView;
    private CancellationTokenSource? _cts;
    private DateTime _recordingStartedAt;

    public ScreenshotCapture(
        IEnumerable<IStorageSink> sinks,
        IOptions<CameraRecorderSettings> options,
        ILogger<ScreenshotCapture> logger)
    {
        _sinks = sinks;
        _options = options;
        _logger = logger;
    }

    public void SetTextureView(TextureView textureView, IH26xDecoder? decoder)
    {
        _textureView = textureView;
        _decoder = decoder;
    }

    public void OnRecordStarted(DateTime startedAt)
    {
        var settings = _options.Value.Screenshots;
        if (settings is not { Enabled: true } || settings.TimestampsSec.Length == 0)
            return;

        _recordingStartedAt = startedAt;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        foreach (int delaySec in settings.TimestampsSec)
        {
            if (delaySec <= 0) continue;
            _ = CaptureAfterDelay(delaySec, _cts.Token);
        }
    }

    public void OnRecordStopped()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task CaptureAfterDelay(int delaySec, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(delaySec), ct); }
        catch (OperationCanceledException) { return; }

        if (ct.IsCancellationRequested || _textureView == null)
            return;

        RunOnUiThread(() => DoCapture(delaySec));
    }

    private void DoCapture(int delaySec)
    {
        if (_textureView == null) return;

        var h265 = _decoder as H265Decoder;
        int w = h265?.VideoWidth ?? 640;
        int h = h265?.VideoHeight ?? 480;

        var bitmap = _textureView.GetBitmap(w, h);
        if (bitmap == null)
        {
            _logger.LogWarning("ScreenshotCapture: не удалось получить Bitmap с TextureView");
            return;
        }

        try
        {
            using var stream = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Jpeg, 85, stream);
            var jpegBytes = stream.ToArray();

            string fileName = $"{_recordingStartedAt:yyyy-MM-dd HH.mm.ss} {delaySec:00}sec.jpg";

            foreach (var sink in _sinks)
                _ = sink.SaveAsync(fileName, jpegBytes);

            _logger.LogInformation("ScreenshotCapture: сохранён {File} ({Size} КБ)",
                fileName, jpegBytes.Length / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScreenshotCapture: ошибка сохранения скриншота");
        }
        finally
        {
            bitmap.Recycle();
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_textureView?.Context is Android.App.Activity activity)
            activity.RunOnUiThread(action);
        else
            action();
    }
}
