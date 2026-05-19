using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpISOBMFF;
using SharpMP4.Builders;
using SharpMP4.Common;
using SharpMP4.Tracks;
using System.Collections.Concurrent;

namespace CameraRecorder;

public class RingBufferStorage
{
    private readonly ConcurrentQueue<VideoFrame> _videoBuffer = new();
    private readonly ConcurrentQueue<AudioFrame> _audioBuffer = new();

    private readonly object _lockObj = new();
    private readonly ILogger<RingBufferStorage> _logger;
    private readonly IMp4Logger _mp4Logger;
    private readonly IOptions<CameraRecorderSettings> _options;
    private readonly IWavToAACConverter? _wavToAACConverter;
    private readonly IStorageSink[] _sinks;
    private long _currentBufferDurationMs = 0;

    private bool _isRecording;

    /// <summary>
    /// Текущая длительность кольцевого буфера
    /// </summary>
    public TimeSpan CurrentBufferDuration => TimeSpan.FromMilliseconds(_currentBufferDurationMs);


    public RingBufferStorage(ILogger<RingBufferStorage> logger,
        IMp4Logger mp4Logger,
        IEnumerable<IStorageSink> sinks,
        IOptions<CameraRecorderSettings> options,
        IWavToAACConverter? wavToAACConverter)
    {
        _logger = logger;
        _mp4Logger = mp4Logger;
        _options = options;
        _wavToAACConverter = wavToAACConverter;
        _sinks = sinks?.ToArray() ?? [];
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
                    while (_currentBufferDurationMs > maxDurationMs && _videoBuffer.TryDequeue(out f) || _videoBuffer.TryPeek(out f) && f.UnitType != NalUnitType.H265_VPS && _videoBuffer.TryDequeue(out f))
                    {
                        _currentBufferDurationMs = (long)(timestamp - f.Timestamp).TotalMilliseconds;
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


        lock (_lockObj)
        {
            if (!_isRecording)
            {
                if (_videoBuffer.TryPeek(out var oldestVideoFrame))
                {
                    while (_audioBuffer.TryDequeue(out var oldestAudioFrame) && oldestAudioFrame.Timestamp <= oldestVideoFrame.Timestamp)
                    {
                        //ничего не делаем, уже удалили
                    }

                }
            }
        }
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

    // Строит MP4 в MemoryStream и отправляет во все sinks
    async Task StopRecordAsync()
    {
        
        _isRecording = false;
        return;

        if (!_isRecording) return;

        List<VideoFrame> videoFramesToSave;
        List<AudioFrame> audioFramesToSave;

        lock (_lockObj)
        {
            videoFramesToSave = _videoBuffer.ToList();
            audioFramesToSave = _audioBuffer.ToList();
            _isRecording = false;
        }

        try
        {

            if (videoFramesToSave.Count == 0) return;

            var timestamp = videoFramesToSave[0].Timestamp;
            string fileName = $"{timestamp:yyyy-MM-dd HH.mm.ss} {(int)(_currentBufferDurationMs / 1000)}sec.mp4";

            _logger.LogInformation(
                "Запись завершена {Time:HH:mm:ss}, первый кадр: {FirstFrame:HH:mm:ss.f} ({UnitType}), длительность: {Duration}мс, кадров: {Count}",
                DateTime.Now, timestamp, videoFramesToSave[0].UnitType, _currentBufferDurationMs, videoFramesToSave.Count);

            // Строим MP4 в MemoryStream
            using var mp4Stream = new MemoryStream();

            IMp4Builder mp4Builder = new Mp4Builder(new SingleStreamOutput(mp4Stream))
            {
                Logger = _mp4Logger,
                TemporaryStorageFactory = new TemporaryMemoryStorageFactory()
            };

            var videoTrack = new H265Track();
            mp4Builder.AddTrack(videoTrack);

            foreach (var frame in videoFramesToSave)
            {
                mp4Builder.ProcessTrackSample(videoTrack.TrackID, frame.Data);
            }

            if (_wavToAACConverter != null)
            {
                // Строим WAV в MemoryStream
                using var wavStream = new MemoryStream();
                foreach (var item in audioFramesToSave)
                {
                    //var wav = NAudio.Codecs.MuLawDecoder.DecodeMuLawToPcm(item.Data);
                    wavStream.Write(item.Data);
                }
                wavStream.Position = 0;
                var bytes = wavStream.ToArray();
                var aacBytes = _wavToAACConverter.Convert(bytes);

                var aacTrack = new AACTrack(1, 44100, 16);
                mp4Builder.AddTrack(aacTrack);

                mp4Builder.ProcessRawSample(aacTrack.TrackID, aacBytes);
            }

            mp4Builder.FinalizeMedia();

            // Раздаём всем sink-ам (fire-and-forget, каждый получает byte[])
            var fileData = mp4Stream.ToArray();
            foreach (var sink in _sinks)
                sink.SaveAsync(fileName, fileData, CancellationToken.None);

        }
        catch (Exception ex)
        {

            throw;
        }
    }
}
