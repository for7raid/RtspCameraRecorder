using Android.Media;
using CameraRecorder;
using CameraRecorder.Sinks;
using Java.Nio;
using Microsoft.Extensions.Logging;

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
        Task.Run(() =>
        {
            try
            {
                var firstFrame = videoFrames[0];
                var lastFrame = videoFrames[^1];
                var startTimestamp = firstFrame.Timestamp;
                var stop = lastFrame.Timestamp;
                var duration = (stop - startTimestamp).TotalSeconds;
                string fileName = $"{startTimestamp:yyyy-MM-dd HH.mm.ss} {duration:00}sec.mp4";
                //long basePts = (long)(firstFrame.Timestamp.Ticks / 10); // 1 tick = 0.1µs

                _logger.LogInformation(
                    "Запись завершена {Time:HH:mm:ss}, первый кадр: {FirstFrame:HH:mm:ss.f} ({UnitType}), длительность: {Duration}с, кадров: {Count}",
                    DateTime.Now, startTimestamp, firstFrame.UnitType, duration, videoFrames.Count);

                var tmpFile = Path.GetTempFileName();
                using MediaMuxer muxer = new MediaMuxer(tmpFile, MuxerOutputType.Mpeg4);

                MediaFormat videoFormat = GetVideoFormat(videoFrames);
                int videoTrackIndex = muxer.AddTrack(videoFormat);

                MediaFormat format = GetAudioFormat();
                int audioTrackIndex = muxer.AddTrack(format);

                muxer.Start();

                AddAudio(audioFrames, muxer, audioTrackIndex, startTimestamp);
                AddVideo(videoFrames, muxer, videoTrackIndex, startTimestamp);

                muxer.Stop();
                muxer.Release();

                // Раздаём всем sink-ам (fire-and-forget, каждый получает byte[])
                foreach (var sink in _sinks)
                    sink.SaveAsync(fileName, tmpFile);

                if (File.Exists(tmpFile))
                    File.Delete(tmpFile);

            }
            catch (Exception ex)
            {
                var firstVideoFrame = videoFrames.FirstOrDefault();
                var firstAudioFrame = audioFrames.FirstOrDefault();
                _logger.LogError(ex, $"Ошибка сохранения файлов. video {firstVideoFrame?.Timestamp:mm:ss.ffff}, audio {firstAudioFrame?.Timestamp:mm:ss.ffff}");
            }
        });
    }

    private MediaFormat GetVideoFormat(List<VideoFrame> videoFrames)
    {
        byte[] sps = videoFrames.FirstOrDefault(f => f.UnitType == NalUnitType.H265_SPS)?.Data,
                pps = videoFrames.FirstOrDefault(f => f.UnitType == NalUnitType.H265_PPS)?.Data,
                vps = videoFrames.FirstOrDefault(f => f.UnitType == NalUnitType.H265_VPS)?.Data;

        if (sps == null || pps == null || vps == null)
        {
            throw new Exception("Не найдены SPS/PPS в видеокадрах");
        }

        var videoFormat = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoHevc, 2650, 1440);
        videoFormat.SetByteBuffer("csd-0", ByteBuffer.Wrap(ConcatenateVpsSpsPps(vps, sps, pps)));
        return videoFormat;
    }

    private static void AddVideo(List<VideoFrame> videoFrames, MediaMuxer muxer, int videoTrackIndex, DateTime videoBasePts)
    {
        foreach (var frame in videoFrames)
        {

            if (frame.IsConfigFrame)
                continue;

            ByteBuffer buffer = ByteBuffer.Wrap(frame.Data);
            MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo
            {
                // Для каждого кадра:
                PresentationTimeUs = (long)(frame.Timestamp - videoBasePts).TotalMicroseconds,
                Size = frame.Data.Length,
                Flags = frame.IsKeyFrame ? MediaCodecBufferFlags.SyncFrame : MediaCodecBufferFlags.None
            };

            muxer.WriteSampleData(videoTrackIndex, buffer, bufferInfo);
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

    private void AddAudio(List<AudioFrame> audioFrames, MediaMuxer muxer, int audioTrackIndex, DateTime videoBasePts)
    {
        try
        {
            string mime = MediaFormat.MimetypeAudioAac;
            MediaFormat format = GetAudioFormat(mime);

            using MediaCodec encoder = MediaCodec.CreateEncoderByType(mime);
            encoder.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            encoder.Start();

            var bufferInfo = new MediaCodec.BufferInfo();

            for (int i = 0; i <= audioFrames.Count; i++)
            {
                bool isEnd = i == audioFrames.Count;

                // ── Подаём PCM на вход ──
                int inputIndex = encoder.DequeueInputBuffer(10000);
                if (inputIndex >= 0)
                {
                    ByteBuffer inputBuf = encoder.GetInputBuffer(inputIndex)!;
                    inputBuf.Clear();

                    if (!isEnd)
                    {
                        var af = audioFrames[i];
                        byte[] pcm = NAudio.Codecs.MuLawDecoder.DecodeMuLawToPcm(af.Data);
                        inputBuf.Put(pcm);
                        long pts = (long)(af.Timestamp - videoBasePts).TotalMicroseconds;
                        encoder.QueueInputBuffer(inputIndex, 0, pcm.Length, pts, MediaCodecBufferFlags.None);
                    }
                    else
                    {
                        encoder.QueueInputBuffer(inputIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                    }
                }

                // ── Забираем AAC на выход ──
                int outputIndex;
                while ((outputIndex = encoder.DequeueOutputBuffer(bufferInfo, 10000)) >= 0)
                {
                    if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.CodecConfig))
                    {
                        encoder.ReleaseOutputBuffer(outputIndex, false);
                        continue;
                    }

                    ByteBuffer outBuf = encoder.GetOutputBuffer(outputIndex)!;
                    outBuf.Position(bufferInfo.Offset);
                    outBuf.Limit(bufferInfo.Offset + bufferInfo.Size);

                    muxer.WriteSampleData(audioTrackIndex, outBuf, bufferInfo);
                    encoder.ReleaseOutputBuffer(outputIndex, false);

                    if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.EndOfStream))
                        break;
                }
            }

            encoder.Stop();
            encoder.Release();

        }
        catch (Exception ex)
        {

            _logger.LogError(ex, $"Ошибка обработки аудио");
        }

    }

    private MediaFormat GetAudioFormat(string mime = MediaFormat.MimetypeAudioAac)
    {
        int sampleRate = 8000, channelCount = 1, bitRate = 32000;

        MediaFormat format = MediaFormat.CreateAudioFormat(mime, sampleRate, channelCount);
        format.SetInteger(MediaFormat.KeyBitRate, bitRate);
        format.SetInteger(MediaFormat.KeyChannelCount, channelCount);
        format.SetInteger(MediaFormat.KeyAacProfile, (int)MediaCodecProfileType.Aacobjectlc);
        var sampleIndex = GetADTSFrequencyIndex(sampleRate);
        var audioProfile = (int)MediaCodecProfileType.Aacobjectlc;
        var csd = ByteBuffer.Allocate(2);
        csd.Put((sbyte)((audioProfile << 3) | (sampleIndex >> 1)));
        csd.Position(1);
        csd.Put((sbyte)((sampleIndex << 7 & 0x80) | (channelCount << 3)));
        csd.Flip();
        format.SetByteBuffer("csd-0", csd);
        return format;
    }

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
}
