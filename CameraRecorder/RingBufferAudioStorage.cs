using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime;
using System.Text;

namespace CameraRecorder;

public class RingBufferAudioStorage
{
    private readonly ConcurrentQueue<AudioFrame> _buffer = new();
    private readonly object _lockObj = new();
    private readonly int _maxDurationMs;
    private readonly ILogger<RingBufferAudioStorage> _logger;
    private readonly IStorageSink[] _sinks;
    private readonly CameraRecorderSettings _settings;
    private long _currentBufferDurationMs = 0;

    private bool _isRecording;
    public RingBufferAudioStorage(ILogger<RingBufferAudioStorage> logger, IEnumerable<IStorageSink> sinks, ISettingsProvider settingsProvider)
    {
        _logger = logger;
        _sinks = sinks?.ToArray() ?? [];
        _settings = settingsProvider.GetSettings();
        _maxDurationMs = _settings.PreMotionDurationSec * 1000;
    }

    // Вызывается для каждого полученного кадра из SharpRTSP
    public void AddFrame(ReadOnlyMemory<byte> nalUnit)
    {
        var timestamp = DateTime.Now;

        var frame = new AudioFrame { Data = nalUnit.ToArray(), Timestamp = timestamp };
        _buffer.Enqueue(frame);

        if (!_isRecording)
        {
            lock (_lockObj)
            {
                // Обновляем суммарную длительность буфера
                if (_buffer.TryPeek(out var oldestFrame))
                {
                    _currentBufferDurationMs = (long)(timestamp - oldestFrame.Timestamp).TotalMilliseconds;

                    //Удаляем кадры, которые старее 10 секунд
                    while (_currentBufferDurationMs > _maxDurationMs && _buffer.TryDequeue(out var f))
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
        _logger.LogInformation($"Start audio recodring {DateTime.Now:yyyy-MM-dd HH:mm:ss}, firts frame {oldestFrame?.Timestamp::yyyy-MM-dd HH:mm:ss}, duration {_currentBufferDurationMs}, frames count {_buffer.Count}");
    }

    // Строит WAV в MemoryStream и отправляет во все sinks
    public async Task StopRecordAsync()
    {
        if (!_isRecording) return;

        List<AudioFrame> framesToSave;

        lock (_lockObj)
        {
            framesToSave = _buffer.ToList();
            _buffer.Clear();
            _isRecording = false;
        }

        if (framesToSave.Count == 0) return;

        var timestamp = framesToSave[0].Timestamp;
        string fileName = $"{timestamp:yyyy-MM-dd HH.mm.ss}.wav";

        _logger.LogInformation(
            "Запись аудио завершена {Time:HH:mm:ss}, первый кадр: {FirstFrame:HH:mm:ss.f}, длительность: {Duration}мс, кадров: {Count}",
            DateTime.Now, timestamp, _currentBufferDurationMs, framesToSave.Count);

        // Строим WAV в MemoryStream
        using var wavStream = new MemoryStream();

        int pcmDataSize = framesToSave.Sum(u => u.Data.Length) * 2;
        var header = BuildWavHeader(pcmDataSize);
        wavStream.Write(header);

        foreach (var item in framesToSave)
        {
            var bytes = NAudio.Codecs.MuLawDecoder.DecodeMuLawToPcm(item.Data);
            wavStream.Write(bytes);
        }

        // Раздаём всем sink-ам
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.SaveAsync(fileName, wavStream, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка сохранения аудио в {SinkName}", sink.Name);
            }
        }
    }

    public static byte[] BuildWavHeader(int dataSize, int sampleRate = 8000, short channels = 1, short bitsPerSample = 16)
    {
        byte[] header = new byte[44];

        // ChunkID: "RIFF"
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);

        // ChunkSize: размер файла - 8
        int chunkSize = dataSize + 36;
        BitConverter.GetBytes(chunkSize).CopyTo(header, 4);

        // Format: "WAVE"
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);

        // Subchunk1ID: "fmt "
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);

        // Subchunk1Size: 16 для PCM
        BitConverter.GetBytes(16).CopyTo(header, 16);

        // AudioFormat: 1 = PCM
        BitConverter.GetBytes((short)1).CopyTo(header, 20);

        // NumChannels
        BitConverter.GetBytes(channels).CopyTo(header, 22);

        // SampleRate
        BitConverter.GetBytes(sampleRate).CopyTo(header, 24);

        // ByteRate = SampleRate * NumChannels * BitsPerSample / 8
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        BitConverter.GetBytes(byteRate).CopyTo(header, 28);

        // BlockAlign = NumChannels * BitsPerSample / 8
        short blockAlign = (short)(channels * bitsPerSample / 8);
        BitConverter.GetBytes(blockAlign).CopyTo(header, 32);

        // BitsPerSample
        BitConverter.GetBytes(bitsPerSample).CopyTo(header, 34);

        // Subchunk2ID: "data"
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);

        // Subchunk2Size = размер данных
        BitConverter.GetBytes(dataSize).CopyTo(header, 40);

        return header;
    }
}
