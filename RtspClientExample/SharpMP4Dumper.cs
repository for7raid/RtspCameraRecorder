using CameraRecorder.Settings;
using CameraRecorder.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpISOBMFF;
using SharpMP4.Builders;
using SharpMP4.Common;
using SharpMP4.Tracks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CameraRecorder
{
    public class SharpMP4Dumper : IFramesDumper
    {
        private readonly ILogger<SharpMP4Dumper> _logger;
        private readonly IMp4Logger _mp4Logger;
        private readonly IEnumerable<IStorageSink> _sinks;

        public SharpMP4Dumper(ILogger<SharpMP4Dumper> logger,
        IMp4Logger mp4Logger,
        IEnumerable<IStorageSink> sinks,
        IOptions<CameraRecorderSettings> options)
        {
            _logger = logger;
            _mp4Logger = mp4Logger;
            _sinks = sinks;
        }
        public void ProcessFrames(List<VideoFrame> videoFrames, List<AudioFrame> audioFrames)
        {
            try
            {


                var firstFrame = videoFrames[0];
                var lastFrame = videoFrames[^1].Timestamp;
                var timestamp = firstFrame.Timestamp;
                var duration = (lastFrame - timestamp).TotalSeconds;
                string fileName = $"{timestamp:yyyy-MM-dd HH.mm.ss} {duration:00}sec.mp4";

                _logger.LogInformation(
                    "Запись завершена {Time:HH:mm:ss}, первый кадр: {FirstFrame:HH:mm:ss.f} ({UnitType}), длительность: {Duration}с, кадров: {Count}",
                    DateTime.Now, timestamp, firstFrame.UnitType, duration, videoFrames.Count);

                // Строим MP4 в MemoryStream
                using var mp4Stream = new MemoryStream();

                IMp4Builder mp4Builder = new Mp4Builder(new SingleStreamOutput(mp4Stream))
                {
                    Logger = _mp4Logger,
                    TemporaryStorageFactory = new TemporaryMemoryStorageFactory()
                };

                var videoTrack = new H265Track();
                mp4Builder.AddTrack(videoTrack);

                foreach (var frame in videoFrames)
                {
                    mp4Builder.ProcessTrackSample(videoTrack.TrackID, frame.Data);
                }



                mp4Builder.FinalizeMedia();

                // Раздаём всем sink-ам (fire-and-forget, каждый получает byte[])
                var fileData = mp4Stream.ToArray();
                foreach (var sink in _sinks)
                    sink.SaveAsync(fileName, fileData);

            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "Ошибка сохранения файлов.");
            }
        }
    }
}
