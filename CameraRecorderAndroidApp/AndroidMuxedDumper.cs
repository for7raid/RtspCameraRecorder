using Android.Media;
using CameraRecorder;
using CameraRecorder.Sinks;
using CameraRecorderAndroidApp.Sinks;
using Java.Nio;
using Microsoft.Extensions.Logging;
using static Android.Renderscripts.ScriptGroup;

namespace CameraRecorderAndroidApp;

public class AndroidMuxedDumper : IFramesDumper
{
    private readonly ILogger<AndroidMuxedDumper> _logger;
    private readonly IEnumerable<IStorageSink> _sinks;

    public AndroidMuxedDumper(ILogger<AndroidMuxedDumper> logger, IEnumerable<IStorageSink> sinks)
    {
        _logger = logger;
        _sinks = sinks;
    }
    public void ProcessFrames(List<VideoFrame> videoFrames, List<AudioFrame> audioFrames)
    {
        try
        {
            long id = 0;

            var firstFrame = videoFrames[0];
            var lastFrame = videoFrames[^1];
            var start = firstFrame.Timestamp;
            var stop = lastFrame.Timestamp;
            var duration = (stop - start).TotalSeconds;
            string fileName = $"{start:yyyy-MM-dd HH.mm.ss} {duration:00}sec.mp4";

            _logger.LogInformation(
                "Запись завершена {Time:HH:mm:ss}, первый кадр: {FirstFrame:HH:mm:ss.f} ({UnitType}), длительность: {Duration}с, кадров: {Count}",
                DateTime.Now, start, firstFrame.UnitType, duration, videoFrames.Count);

            var tmpFile = Path.GetTempFileName();
            using MediaMuxer muxer = new MediaMuxer(tmpFile, MuxerOutputType.Mpeg4);


            byte[] sps = videoFrames.FirstOrDefault(f => f.UnitType == NalUnitType.H265_SPS)?.Data,
                    pps = videoFrames.FirstOrDefault(f => f.UnitType == NalUnitType.H265_PPS)?.Data,
                    vps = videoFrames.FirstOrDefault(f => f.UnitType == NalUnitType.H265_VPS)?.Data;
            if (sps == null || pps == null || vps == null)
            {
                _logger.LogError("Не найдены SPS/PPS в видеокадрах");
                return;
            }

            MediaFormat videoFormat = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoHevc, 2650, 1440);
            videoFormat.SetByteBuffer("csd-0", ByteBuffer.Wrap(ConcatenateVpsSpsPps(vps, sps, pps)));
            int videoTrackIndex = muxer.AddTrack(videoFormat);


            AddAudio(audioFrames, muxer);

            //muxer.Start();

            foreach (var frame in videoFrames)
            {

                if (frame.IsConfigFrame)
                    continue;

                ByteBuffer buffer = ByteBuffer.Wrap(frame.Data);
                MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();

                long basePts = (long)(firstFrame.Timestamp.Ticks / 10); // 1 tick = 0.1µs
                                                                        // Для каждого кадра:
                bufferInfo.PresentationTimeUs = (long)(frame.Timestamp.Ticks / 10) - basePts;

                bufferInfo.Size = frame.Data.Length;
                bufferInfo.Flags = frame.IsKeyFrame ? MediaCodecBufferFlags.SyncFrame : MediaCodecBufferFlags.None;

                muxer.WriteSampleData(videoTrackIndex, buffer, bufferInfo);
            }



            muxer.Stop();
            muxer.Release();

            var fileData = File.ReadAllBytes(tmpFile);
            // Раздаём всем sink-ам (fire-and-forget, каждый получает byte[])
            foreach (var sink in _sinks)
                sink.SaveAsync(fileName, fileData, CancellationToken.None);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сохранения файлов.");
        }
    }

    private static byte[] ConcatenateVpsSpsPps(byte[] vps, byte[] sps, byte[] pps)
    {
        if (vps == null || sps == null || pps == null)
            throw new ArgumentException("VPS, SPS и PPS не могут быть null");

        // Выделяем буфер для объединенных данных
        byte[] csd0 = new byte[vps.Length + sps.Length + pps.Length];

        // Копируем данные последовательно
        Array.Copy(vps, 0, csd0, 0, vps.Length);
        Array.Copy(sps, 0, csd0, vps.Length, sps.Length);
        Array.Copy(pps, 0, csd0, vps.Length + sps.Length, pps.Length);

        return csd0;
    }

    public void AddAudio(List<AudioFrame> audioFrames, MediaMuxer muxer)
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

            //double durationInMs = (input.Length * 2 / 16000) * 1000;
            //format.SetLong(MediaFormat.KeyDuration, (long)durationInMs);

            // Создаем энкодер
            using MediaCodec encoder = MediaCodec.CreateEncoderByType(mime);
            encoder.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            encoder.Start();

            // Получаем буферы энкодера
            var bufferInfo = new MediaCodec.BufferInfo();

            // 2. Чтение WAV и кодирование
            using var wavStream = new MemoryStream();
            using var aacStream = new MemoryStream();

            foreach (AudioFrame frame in audioFrames)
            {
                wavStream.Write(NAudio.Codecs.MuLawDecoder.DecodeMuLawToPcm(frame.Data));
            }

            wavStream.Position = 0;

            byte[] pcmBuffer = new byte[4096 * 2]; // Размер произвольный, но не слишком маленький
            bool isEndOfStream = false;
            int bytesRead;
            int audioTrackIndex = 0;// muxer.AddTrack(format);
            bool isStarted = false;

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

                    if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.CodecConfig))
                    {
                        encoder.ReleaseOutputBuffer(outputBufferIndex, false);
                    }
                    else if (outputBufferIndex >= 0)
                    {
                        ByteBuffer outputBuffer = encoder.GetOutputBuffer(outputBufferIndex);
                        outputBuffer.Position(bufferInfo.Offset);
                        outputBuffer.Limit(bufferInfo.Offset + bufferInfo.Size);

                        muxer.WriteSampleData(audioTrackIndex, outputBuffer, bufferInfo);
                        //aacStream.Write(aacChunk, 0, aacChunk.Length);


                        // Обязательно освобождаем буфер
                        encoder.ReleaseOutputBuffer(outputBufferIndex, false);

                        if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.EndOfStream))
                        {
                            break; // Выходим, как только получен флаг конца потока
                        }
                    }
                    else if (outputBufferIndex == (int)MediaCodecInfoState.OutputFormatChanged)
                    {
                        // КРИТИЧЕСКИЙ МОМЕНТ: Добавляем трек в Muxer только после смены формата

                        MediaFormat newFormat = encoder.OutputFormat;
                        audioTrackIndex = muxer.AddTrack(newFormat);
                        if (!isStarted)
                        {
                            muxer.Start();
                            isStarted = true;
                        }
                    }
                } while (outputBufferIndex >= 0);
            }


            // 3. Завершение работы
            encoder.Stop();
            encoder.Release();


        }
        catch (Exception ex)
        {

            throw;
        }
    }
}
