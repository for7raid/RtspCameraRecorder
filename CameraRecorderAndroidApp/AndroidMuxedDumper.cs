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
        try
        {
            var firstFrame = videoFrames[0];
            var lastFrame = videoFrames[^1];
            var start = firstFrame.Timestamp;
            var stop = lastFrame.Timestamp;
            var duration = (stop - start).TotalSeconds;
            string fileName = $"{start:yyyy-MM-dd HH.mm.ss} {duration:00}sec.mp4";
            long basePts = (long)(firstFrame.Timestamp.Ticks / 10); // 1 tick = 0.1µs

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


            AddAudio(audioFrames, muxer, basePts);

            //muxer.Start();

            foreach (var frame in videoFrames)
            {

                if (frame.IsConfigFrame)
                    continue;

                ByteBuffer buffer = ByteBuffer.Wrap(frame.Data);
                MediaCodec.BufferInfo bufferInfo = new MediaCodec.BufferInfo();

              
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
                sink.SaveAsync(fileName, fileData);

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

    public void AddAudio(List<AudioFrame> audioFrames, MediaMuxer muxer, long videoBasePts)
    {
        string mime = MediaFormat.MimetypeAudioAac; 
        int sampleRate = 8000, channelCount = 1, bitRate = 32000;

        MediaFormat format = MediaFormat.CreateAudioFormat(mime, sampleRate, channelCount);
        format.SetInteger(MediaFormat.KeyBitRate, bitRate);
        format.SetInteger(MediaFormat.KeyAacProfile, (int)MediaCodecProfileType.Aacobjectlc);

        using MediaCodec encoder = MediaCodec.CreateEncoderByType(mime);
        encoder.Configure(format, null, null, MediaCodecConfigFlags.Encode);
        encoder.Start();

        var bufferInfo = new MediaCodec.BufferInfo();
        int audioTrackIndex = -1;
        bool muxerStarted = false;

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
                    long pts = (long)(af.Timestamp.Ticks / 10) - videoBasePts;
                    encoder.QueueInputBuffer(inputIndex, 0, pcm.Length, pts, MediaCodecBufferFlags.None);
                }
                else
                {
                    encoder.QueueInputBuffer(inputIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                }
            }

            // ── Забираем AAC на выход ──
            int outputIndex;
            while ((outputIndex = encoder.DequeueOutputBuffer(bufferInfo, 0)) >= 0)
            {
                if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.CodecConfig))
                {
                    encoder.ReleaseOutputBuffer(outputIndex, false);
                    continue;
                }

                ByteBuffer outBuf = encoder.GetOutputBuffer(outputIndex)!;
                outBuf.Position(bufferInfo.Offset);
                outBuf.Limit(bufferInfo.Offset + bufferInfo.Size);

                if (audioTrackIndex < 0)
                {
                    audioTrackIndex = muxer.AddTrack(encoder.OutputFormat);
                    muxer.Start();
                    muxerStarted = true;
                }

                muxer.WriteSampleData(audioTrackIndex, outBuf, bufferInfo);
                encoder.ReleaseOutputBuffer(outputIndex, false);

                if (bufferInfo.Flags.HasFlag(MediaCodecBufferFlags.EndOfStream))
                    break;
            }
        }

        encoder.Stop();
        encoder.Release();
    }

}
