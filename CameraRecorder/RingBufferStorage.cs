using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CameraRecorder;

public class RingBufferStorage
{
    private readonly ConcurrentQueue<VideoFrame> _videoBuffer = new();
    private readonly ConcurrentQueue<AudioFrame> _audioBuffer = new();

    private readonly object _lockObj = new();
    private readonly ILogger<RingBufferStorage> _logger;
    private readonly IOptions<CameraRecorderSettings> _options;
    private long _currentBufferDurationMs = 0;

    private bool _isRecording;

    /// <summary>
    /// Текущая длительность кольцевого буфера
    /// </summary>
    public TimeSpan CurrentBufferDuration => TimeSpan.FromMilliseconds(_currentBufferDurationMs);


    public RingBufferStorage(ILogger<RingBufferStorage> logger,
        IEnumerable<IStorageSink> sinks,
        IOptions<CameraRecorderSettings> options)
    {
        _logger = logger;
        _options = options;
    }

    // Вызывается для каждого полученного кадра из SharpRTSP
    public void AddVideoFrame(ReadOnlyMemory<byte> nalUnit, NalUnitType unitType, ulong RtpTimestamp)
    {
        var maxDurationMs = _options.Value.PreMotionDurationSec * 1000;
        var timestamp = DateTime.Now;

        var frame = new VideoFrame { Data = nalUnit.ToArray(), Timestamp = timestamp, UnitType = unitType, RtpTimestamp = RtpTimestamp };
        _videoBuffer.Enqueue(frame);


        lock (_lockObj)
        {
            // Обновляем суммарную длительность буфера
            if (_videoBuffer.TryPeek(out var oldestFrame))
            {
                _currentBufferDurationMs = (long)(timestamp - oldestFrame.Timestamp).TotalMilliseconds;

                if (!_isRecording)
                {
                    //Удаляем кадры, которые старее 10 секунд или кадры из середины
                    VideoFrame f;
                    while (_currentBufferDurationMs > maxDurationMs && _videoBuffer.TryDequeue(out f) || 
                        _videoBuffer.TryPeek(out f) && f.UnitType != NalUnitType.H265_VPS && _videoBuffer.TryDequeue(out f))
                    {
                        _currentBufferDurationMs = (long)(timestamp - f.Timestamp).TotalMilliseconds;
                    }

                    if (_videoBuffer.TryPeek(out var oldestVideoFrame))
                    {
                        while (_audioBuffer.TryDequeue(out var oldestAudioFrame) && oldestAudioFrame.Timestamp < oldestVideoFrame.Timestamp)
                        {
                            //ничего не делаем, уже удалили
                        }

                    }
                }
            }

        }
    }

    public void AddAudioFrame(ReadOnlyMemory<byte> nalUnit, ulong RtpTimestamp)
    {

        var timestamp = DateTime.Now;

        var frame = new AudioFrame { Data = nalUnit.ToArray(), Timestamp = timestamp, RtpTimestamp = RtpTimestamp };
        _audioBuffer.Enqueue(frame);


        //lock (_lockObj)
        //{
        //    if (!_isRecording)
        //    {
        //        if (_videoBuffer.TryPeek(out var oldestVideoFrame))
        //        {
        //            while (_audioBuffer.TryDequeue(out var oldestAudioFrame) && oldestAudioFrame.Timestamp <= oldestVideoFrame.Timestamp)
        //            {
        //                //ничего не делаем, уже удалили
        //            }

        //        }
        //    }
        //}
    }

    public void StartRecord()
    {
        if (_isRecording) return;
        _isRecording = true;
        _videoBuffer.TryPeek(out var oldestFrame);
        _logger.LogInformation($"Start video recodring {DateTime.Now:yyyy-MM-dd HH:mm:ss}, firts frame {oldestFrame?.Timestamp::yyyy-MM-dd HH:mm:ss}, duration {_currentBufferDurationMs}, frames count {_videoBuffer.Count}");
    }

    public (List<VideoFrame> videoFrames, List<AudioFrame> audioFrames) DumpAndStopRecord()
    {
        List<VideoFrame> videoFramesToSave;
        List<AudioFrame> audioFramesToSave;

        lock (_lockObj)
        {
            videoFramesToSave = _videoBuffer.ToList();
            audioFramesToSave = _audioBuffer.ToList();

            _isRecording = false;

            return (videoFramesToSave, audioFramesToSave);
        }
    }
}
