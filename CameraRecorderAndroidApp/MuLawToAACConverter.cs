using Android.Media;
using CameraRecorder;
using CameraRecorderAndroidApp.Sinks;
using Java.Nio;

namespace CameraRecorderAndroidApp;

public class MuLawToAACConverter : IWavToAACConverter
{
    private readonly LocalFileSink _localFileSink;

    public MuLawToAACConverter(LocalFileSink localFileSink)
    {
        _localFileSink = localFileSink;
    }
    public byte[] Convert(byte[] input)
    {
        try
        {


            // 1. Настройка AAC-энкодера
            string mime = "audio/mp4a-latm";
            int sampleRate = 8000; // Частота дискретизации WAV-файла
            int channelCount = 1;   // Количество каналов
            int bitRate = 128000;   // Битрейт AAC

            // Создаем и настраиваем формат для энкодера
            MediaFormat format = MediaFormat.CreateAudioFormat(mime, sampleRate, channelCount);
            format.SetInteger(MediaFormat.KeyBitRate, bitRate);
            format.SetInteger(MediaFormat.KeyChannelCount, channelCount);
            format.SetInteger(MediaFormat.KeyAacProfile, (int)MediaCodecProfileType.Aacobjectlc); // Профиль AAC-LC

            double durationInMs = (input.Length * 2 / 16000) * 1000;
            format.SetLong(MediaFormat.KeyDuration, (long)durationInMs);

            // Создаем энкодер
            using MediaCodec encoder = MediaCodec.CreateEncoderByType(mime);
            encoder.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            encoder.Start();

            // Получаем буферы энкодера
            var bufferInfo = new MediaCodec.BufferInfo();

            // 2. Чтение WAV и кодирование
            using var wavStream = new MemoryStream(NAudio.Codecs.MuLawDecoder.DecodeMuLawToPcm(input));
            using var aacStream = new MemoryStream();

            byte[] pcmBuffer = new byte[4096 * 2]; // Размер произвольный, но не слишком маленький
            bool isEndOfStream = false;
            int bytesRead;

            while (!isEndOfStream)
            {
                int inputBufferIndex = encoder.DequeueInputBuffer(10000);


                // --- Начало кодирования ---
                // Запрашиваем свободный входной буфер

                if (inputBufferIndex >= 0)
                {
                    ByteBuffer inputBuffer = encoder.GetInputBuffer(inputBufferIndex);
                    inputBuffer.Clear();

                    bytesRead = wavStream.Read(pcmBuffer, 0, inputBuffer.Capacity());
                    isEndOfStream = bytesRead <= 0;

                    if (bytesRead > 0)
                    {
                        // Копируем PCM-данные во входной буфер
                        inputBuffer.Put(pcmBuffer, 0, bytesRead);
                    }

                    encoder.QueueInputBuffer(inputBufferIndex, 0, bytesRead, 0,
                        // Важно: сообщаем, что это конец потока
                        flags: isEndOfStream ? MediaCodecBufferFlags.EndOfStream : MediaCodecBufferFlags.None);

                }

                // --- Получение готовых AAC-данных ---
                int outputBufferIndex;
                do
                {
                    outputBufferIndex = encoder.DequeueOutputBuffer(bufferInfo, 10000);

                    if (outputBufferIndex >= 0)
                    {
                        ByteBuffer outputBuffer = encoder.GetOutputBuffer(outputBufferIndex);
                        byte[] aacChunk = new byte[bufferInfo.Size];

                        outputBuffer.Position(bufferInfo.Offset);
                        outputBuffer.Get(aacChunk, 0, bufferInfo.Size);

                        // Записываем ADTS-заголовок и данные в файл
                        WriteADTSHeader(aacStream, bufferInfo.Size, sampleRate, channelCount);

                        aacStream.Write(aacChunk, 0, aacChunk.Length);


                        // Обязательно освобождаем буфер
                        encoder.ReleaseOutputBuffer(outputBufferIndex, false);

                        if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.EndOfStream))
                        {
                            break; // Выходим, как только получен флаг конца потока
                        }
                    }
                } while (outputBufferIndex >= 0);
            }


            // 3. Завершение работы
            encoder.Stop();
            encoder.Release();

            var cts = new CancellationTokenSource();

            wavStream.Position = 0;
            var wwbb = wavStream.ToArray();
            var h = BuildWavHeader(wwbb.Length);
            using var mm = new MemoryStream(h.Length + wwbb.Length);
            mm.Write(h);
            mm.Write(wwbb);
            _localFileSink.SaveAsync("source.wav", mm.ToArray(), cts.Token);

            aacStream.Position = 0;
            _localFileSink.SaveAsync("aac.aac", aacStream.ToArray(), cts.Token);

            aacStream.Position = 0;
            return aacStream.ToArray();
        }
        catch (Exception ex)
        {

            throw;
        }
    }

    // Вспомогательный метод для записи ADTS-заголовка
    private void WriteADTSHeader(System.IO.Stream stream, int aacDataSize, int sampleRate, int channels)
    {
        int profile = 1; // 1 для AAC LC (Low Complexity)
        int freqIndex = GetADTSFrequencyIndex(sampleRate);
        int chanConfig = channels;

        // Расчет общей длины пакета: заголовок (7 байт) + AAC-данные
        int packetLength = aacDataSize + 7;

        byte[] header = new byte[7];
        header[0] = 0xFF; // Syncword (первые 8 бит)
        header[1] = 0xF1; // Syncword (последние 4 бита), MPEG-4, Layer 0, protection absent
        header[2] = (byte)(((profile - 1) << 6) + (freqIndex << 2) + (chanConfig >> 2));
        header[3] = (byte)(((chanConfig & 0x3) << 6) + (packetLength >> 11));
        header[4] = (byte)((packetLength & 0x7FF) >> 3);
        header[5] = (byte)(((packetLength & 7) << 5) + 0x1F);
        header[6] = 0xFC;

        stream.Write(header, 0, 7);
    }

    // Получение индекса частоты дискретизации для ADTS
    private int GetADTSFrequencyIndex(int sampleRate)
    {
        return sampleRate switch
        {
            96000 => 0,
            88200 => 1,
            64000 => 2,
            48000 => 3,
            44100 => 4,
            32000 => 5,
            24000 => 6,
            22050 => 7,
            16000 => 8,
            12000 => 9,
            11025 => 10,
            8000 => 11,
            7350 => 12,
            _ => 4, // По умолчанию 44100
        };
    }

    public static byte[] BuildWavHeader(int dataSize, int sampleRate = 8000, short channels = 1, short bitsPerSample = 16)
    {
        byte[] header = new byte[44];

        // ChunkID: "RIFF"
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);

        // ChunkSize: размер файла - 8
        int chunkSize = dataSize + 36;
        BitConverter.GetBytes(chunkSize).CopyTo(header, 4);

        // Format: "WAVE"
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);

        // Subchunk1ID: "fmt "
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(header, 12);

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
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(header, 36);

        // Subchunk2Size = размер данных
        BitConverter.GetBytes(dataSize).CopyTo(header, 40);

        return header;
    }
}
