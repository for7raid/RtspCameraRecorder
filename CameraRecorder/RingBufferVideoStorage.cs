using CameraRecorder.Settings;
using CameraRecorder.Sinks;
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
    private readonly int _maxDurationMs;
    private readonly ILogger<RingBufferVideoStorage> _logger;
    private readonly IMp4Logger _mp4Logger;
    private readonly IStorageSink[] _sinks;
    private readonly CameraRecorderSettings _settings;
    private long _currentBufferDurationMs = 0;

    private bool _isRecording;

    /// <summary>
    /// Текущая длительность кольцевого буфера
    /// </summary>
    public TimeSpan CurrentBufferDuration => TimeSpan.FromMilliseconds(_currentBufferDurationMs);


    public RingBufferVideoStorage(ILogger<RingBufferVideoStorage> logger, IMp4Logger mp4Logger, IEnumerable<IStorageSink> sinks, ISettingsProvider settingsProvider)
    {
        _logger = logger;
        _mp4Logger = mp4Logger;
        _sinks = sinks?.ToArray() ?? [];
        _settings = settingsProvider.GetSettings();
        _maxDurationMs = _settings.PreMotionDurationSec * 1000;
    }

    // Вызывается для каждого полученного кадра из SharpRTSP
    public void AddFrame(ReadOnlyMemory<byte> nalUnit, NalUnitType unitType)
    {
        var timestamp = DateTime.Now;

        var frame = new VideoFrame { Data = nalUnit.ToArray(), Timestamp = timestamp, UnitType = unitType };
        _buffer.Enqueue(frame);


        lock (_lockObj)
        {
            // Обновляем суммарную длительность буфера
            if (_buffer.TryPeek(out var oldestFrame))
            {
                _currentBufferDurationMs = (long)(timestamp - oldestFrame.Timestamp).TotalMilliseconds;

                if (!_isRecording)
                {
                    //Удаляем кадры, которые старее 10 секунд или кадры из середины
                    VideoFrame f;
                    while (_currentBufferDurationMs > _maxDurationMs && _buffer.TryDequeue(out f) || _buffer.TryPeek(out f) && f.UnitType != NalUnitType.H265_VPS && _buffer.TryDequeue(out f))
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

    // Строит MP4 в MemoryStream и отправляет во все sinks
    public async Task StopRecordAsync()
    {
        if (!_isRecording) return;

        List<VideoFrame> framesToSave;

        lock (_lockObj)
        {
            framesToSave = _buffer.ToList();
            _buffer.Clear();
            _isRecording = false;
        }

        if (framesToSave.Count == 0) return;

        var timestamp = framesToSave[0].Timestamp;
        string fileName = $"{timestamp:yyyy-MM-dd HH.mm.ss} {(int)(_currentBufferDurationMs / 1000)} sec.mp4";

        _logger.LogInformation(
            "Запись завершена {Time:HH:mm:ss}, первый кадр: {FirstFrame:HH:mm:ss.f} ({UnitType}), длительность: {Duration}мс, кадров: {Count}",
            DateTime.Now, timestamp, framesToSave[0].UnitType, _currentBufferDurationMs, framesToSave.Count);

        // Строим MP4 в MemoryStream
        using var mp4Stream = new MemoryStream();

        IMp4Builder mp4Builder = new Mp4Builder(new SingleStreamOutput(mp4Stream))
        {
            Logger = _mp4Logger,
            TemporaryStorageFactory = new TemporaryMemoryStorageFactory()
        };

        var videoTrack = new H265Track();
        mp4Builder.AddTrack(videoTrack);

        foreach (var frame in framesToSave)
        {
            mp4Builder.ProcessTrackSample(videoTrack.TrackID, frame.Data);
        }

        mp4Builder.FinalizeMedia();

        // Раздаём всем sink-ам
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.SaveAsync(fileName, mp4Stream, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения в {SinkName}", sink.Name);
            }
        }
    }
}
