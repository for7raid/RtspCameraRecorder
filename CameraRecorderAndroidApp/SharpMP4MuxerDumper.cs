using Android.Media;
using CameraRecorder;
using CameraRecorder.Sinks;
using Java.Nio;
using Microsoft.Extensions.Logging;
using SharpISOBMFF;
using SharpMP4.Builders;
using SharpMP4.Tracks;

namespace CameraRecorderAndroidApp;

public class SharpMP4MuxerDumper : IFramesDumper
{
    private readonly ILogger<SharpMP4MuxerDumper> _logger;
    private readonly IEnumerable<IStorageSink> _sinks;

    public SharpMP4MuxerDumper(ILogger<SharpMP4MuxerDumper> logger, IEnumerable<IStorageSink> sinks)
    {
        _logger = logger;
        _sinks = sinks;
    }
    public void ProcessFrames(List<VideoFrame> videoFrames, List<AudioFrame> audioFrames)
    {
        Task.Run(async () =>
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
                // Строим MP4 в MemoryStream
                using (var mp4Stream = File.Open(tmpFile, FileMode.Create))
                {

                    IMp4Builder mp4Builder = new Mp4Builder(new SingleStreamOutput(mp4Stream))
                    {
                        //Logger = _mp4Logger,
                        TemporaryStorageFactory = new TemporaryMemoryStorageFactory()
                    };

                    var videoTrack = new H265Track();
                    mp4Builder.AddTrack(videoTrack);

                    var audioTrack = new AACTrack(1, 8000, 16);
                    mp4Builder.AddTrack(audioTrack);

                    AddVideo(videoFrames, mp4Builder, videoTrack.TrackID, startTimestamp);
                    AddAudio(audioFrames, mp4Builder, audioTrack.TrackID, startTimestamp);

                    mp4Builder.FinalizeMedia();
                }

                var isFileMoved = false;
                // Раздаём всем sink-ам (fire-and-forget, каждый получает byte[])
                foreach (var sink in _sinks)
                {
                    var saveResult = await sink.SaveAsync(fileName, tmpFile);
                    tmpFile = saveResult.newFilePath;
                    isFileMoved |= saveResult.isMoved;
                }

                if (!isFileMoved && File.Exists(tmpFile))
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

    private static void AddVideo(List<VideoFrame> videoFrames, IMp4Builder muxer, uint videoTrackIndex, DateTime videoBasePts)
    {
        foreach (var frame in videoFrames)
        {
            muxer.ProcessTrackSample(videoTrackIndex, frame.Data.AsSpan().Slice(4).ToArray());

        }
    }

    private void AddAudio(List<AudioFrame> audioFrames, IMp4Builder muxer, uint audioTrackIndex, DateTime videoBasePts)
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
                    byte[] aacChunk = new byte[bufferInfo.Size];

                    outBuf.Position(bufferInfo.Offset);
                    outBuf.Get(aacChunk, 0, bufferInfo.Size);

                    muxer.ProcessTrackSample(audioTrackIndex, aacChunk);

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
