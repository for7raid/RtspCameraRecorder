using Microsoft.Extensions.Logging;
using SharpISOBMFF;
using SharpMP4.Builders;
using SharpMP4.Common;
using SharpMP4.Tracks;
using System.Collections.Concurrent;

namespace CameraRecorder;

public class RingBufferVideoStorage
{
    private readonly ConcurrentQueue<VideoFrame> _buffer = new();
    private readonly object _lockObj = new();
    private readonly int _maxDurationMs = 10 * 1000;
    private readonly ILogger<RingBufferVideoStorage> _logger;
    private readonly IMp4Logger _mp4Logger;
    private long _currentBufferDurationMs = 0;

    private bool _isRecording;


    public RingBufferVideoStorage(ILogger<RingBufferVideoStorage> logger, IMp4Logger mp4Logger)
    {
        _logger = logger;
        _mp4Logger = mp4Logger;
    }

    // Вызывается для каждого полученного кадра из SharpRTSP
    public void AddFrame(ReadOnlyMemory<byte> nalUnit, int unitType)
    {
        var timestamp = DateTime.Now;

        var frame = new VideoFrame { Data = nalUnit.ToArray(), Timestamp = timestamp, UnitType = unitType };
        _buffer.Enqueue(frame);

        if (!_isRecording)
        {
            lock (_lockObj)
            {
                // Обновляем суммарную длительность буфера
                if (_buffer.TryPeek(out var oldestFrame))
                {
                    _currentBufferDurationMs = (long)(timestamp - oldestFrame.Timestamp).TotalMilliseconds;

                    //Удаляем кадры, которые старее 10 секунд или кадры из середины
                    VideoFrame f;
                    while (_currentBufferDurationMs > _maxDurationMs && _buffer.TryDequeue(out f) || _buffer.TryPeek(out f) && f.UnitType != 32 && _buffer.TryDequeue(out f))
                    {
                        _currentBufferDurationMs = (long)(timestamp - f.Timestamp).TotalMilliseconds;
                    }
                }
            }
        }
    }

    public void StartRecord()
    {
        _isRecording = true;
        _buffer.TryPeek(out var oldestFrame);
        _logger.LogInformation($"Start video recodring {DateTime.Now:yyyy-MM-dd HH:mm:ss}, firts frame {oldestFrame?.Timestamp::yyyy-MM-dd HH:mm:ss}, duration {_currentBufferDurationMs}, frames count {_buffer.Count}");
    }

    // Сохраняет текущий буфер в MP4 файл
    public void StopRecord(string outputDirectory)
    {
        if (!_isRecording) return;

        List<VideoFrame> framesToSave;

        lock (_lockObj)
        {
            framesToSave = _buffer.ToList(); //.SkipWhile(u => u.UnitType != 32)
            _buffer.Clear();
            _isRecording = false;
        }

        if (framesToSave.Count > 0)
        {
            _logger.LogInformation($"Record video finished {DateTime.Now:HH:mm:ss}, firts frame {framesToSave[0].Timestamp:HH:mm:ss.f}={framesToSave[0].UnitType}, duration {_currentBufferDurationMs}, frames count {framesToSave.Count}");

            string fileName = Path.Combine(outputDirectory, $"{framesToSave[0].Timestamp:yyyy-MM-dd HH.mm.ss}.mp4");

            using var _outputStream = new FileStream(fileName, FileMode.Create, FileAccess.Write);

            IMp4Builder mp4Builder = new Mp4Builder(new SingleStreamOutput(_outputStream))
            {
                Logger = _mp4Logger,
                TemporaryStorageFactory = new TemporaryMemoryStorageFactory()
            };

            var videoTrack = new H265Track();
            mp4Builder.AddTrack(videoTrack);

            foreach (var frame in framesToSave)
            {
                mp4Builder?.ProcessTrackSample(videoTrack.TrackID, frame.Data);
            }

            mp4Builder.FinalizeMedia();
        }


    }
}