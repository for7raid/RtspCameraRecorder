using CameraRecorder;
using H264Sharp;
using System;
using System.Runtime.InteropServices;

namespace RtspClientExample
{
    internal class H264SharpDecoder : IH26xDecoder, IDisposable
    {
        private readonly H264Decoder _decoder;

        public event EventHandler<DecodedFrameEventArgs> FrameDecoded;
        YuvImage yuvImage = new YuvImage(640, 480);

        public H264SharpDecoder()
        {
            _decoder = new H264Decoder();
            _decoder.Initialize();

        }
        public void DecodeFrame(byte[] h26xData, long timestampUs = 0, NalUnitType nalUnitType = NalUnitType.Unknown)
        {
            DecodingState ds;

            var dec = _decoder.Decode(h26xData, 0, h26xData.Length, noDelay: true, out ds, ref yuvImage);
            if (dec && ds == DecodingState.dsErrorFree)
            {
                var yPlane = new byte[yuvImage.Width * yuvImage.Height];
                unsafe
                {
                    Marshal.Copy(yuvImage.ImageBytes, yPlane, 0, yPlane.Length);
                }

                // Создаём объект с декодированным кадром
                var decodedFrame = new DecodedVideoFrame
                {
                    Data = yPlane,
                    Width = yuvImage.Width,
                    Height = yuvImage.Height,
                    Stride = yuvImage.strideUV,
                    TimestampUs = timestampUs,
                    Format = "Y",
                };

                // Вызываем событие в UI потоке (или в потоке декодера)
                OnFrameDecoded(decodedFrame);

            }
        }

        /// <summary>
        /// Вызов события о декодировании кадра
        /// </summary>
        private void OnFrameDecoded(DecodedVideoFrame frame)
        {
            // Создаём аргументы события
            var args = new DecodedFrameEventArgs
            {
                Frame = frame,
                Decoder = this
            };

            // Вызываем событие (синхронно в потоке декодера)
            FrameDecoded?.Invoke(this, args);
        }

        public void Dispose()
        {
            _decoder.Dispose();
            yuvImage.Dispose();
        }
    }
}
