using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace CameraRecorder;

public class RingBufferAudioStorage
{
    private readonly ConcurrentQueue<AudioFrame> _buffer = new();
    private readonly object _lockObj = new();
    private readonly int _maxDurationMs = 10 * 1000;
    private readonly ILogger<RingBufferAudioStorage> _logger;
    private long _currentBufferDurationMs = 0;

    private bool _isRecording;
    public RingBufferAudioStorage(ILogger<RingBufferAudioStorage> logger)
    {
        _logger = logger;
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

    // Сохраняет текущий буфер в MP4 файл
    public void StopRecord(string outputDirectory)
    {
        if (!_isRecording) return;

        List<AudioFrame> framesToSave;

        lock (_lockObj)
        {
            framesToSave = _buffer.ToList();
            _buffer.Clear();
            _isRecording = false;
        }

        if (framesToSave.Count > 0)
        {
            _logger.LogInformation($"Record audio finished {DateTime.Now:HH:mm:ss}, firts frame {framesToSave[0].Timestamp:HH:mm:ss.f}, duration {_currentBufferDurationMs}, frames count {framesToSave.Count}");

            string fileName = Path.Combine(outputDirectory, $"{framesToSave[0].Timestamp:yyyy-MM-dd HH.mm.ss}.wav");

            using var _outputStream = new FileStream(fileName, FileMode.Create, FileAccess.Write);

            var header = BuildWavHeader(framesToSave.Sum(u => u.Data.Length) * 2);
            _outputStream.Write(header);

            foreach (var item in framesToSave)
            {
                var bytes = NAudio.Codecs.MuLawDecoder.DecodeMuLawToPcm(item.Data);
                _outputStream.Write(bytes);
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