using CameraRecorder;
using FFmpeg.AutoGen;
using System;
using System.Runtime.InteropServices;

namespace RtspClientExample;

public unsafe class H265FFMpegDecoder : IH26xDecoder, IDisposable
{
    private AVCodecContext* _codecContext;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private bool _initialized;

    public event EventHandler<DecodedFrameEventArgs>? FrameDecoded;

    public H265FFMpegDecoder()
    {
        ffmpeg.RootPath = @"C:\Downloads\ffmpeg-master-latest-win64-gpl-shared\ffmpeg-master-latest-win64-gpl-shared\bin";

        // 1. Находим H.265 / HEVC декодер
        var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_HEVC);
        if (codec == null)
            throw new Exception("H.265 декодер не найден.");

        // 2. Аллоцируем контекст кодека
        _codecContext = ffmpeg.avcodec_alloc_context3(codec);

        // 3. Открываем кодек
        if (ffmpeg.avcodec_open2(_codecContext, codec, null) < 0)
            throw new Exception("Не удалось открыть H.265 кодек.");

        // 4. Аллоцируем кадр и пакет
        _frame = ffmpeg.av_frame_alloc();
        _packet = ffmpeg.av_packet_alloc();

        _initialized = true;
    }

    public void DecodeFrame(byte[] rawH265Data, long timestampUs = 0,
        NalUnitType nalUnitType = NalUnitType.Unknown)
    {
        if (!_initialized || _codecContext == null)
            return;

        // ── Шаг 1: подготавливаем пакет ──
        ffmpeg.av_packet_unref(_packet);                         // сброс предыдущего состояния

        // Копируем данные в буфер, которым владеет FFmpeg
        ffmpeg.av_new_packet(_packet, rawH265Data.Length);
        Marshal.Copy(rawH265Data, 0, (IntPtr)_packet->data, rawH265Data.Length);

        // ── Шаг 2: отправляем пакет в декодер ──
        int sendResult = ffmpeg.avcodec_send_packet(_codecContext, _packet);
        ffmpeg.av_packet_unref(_packet);                         // буфер больше не нужен

        if (sendResult < 0)
            return;

        // ── Шаг 3: получаем декодированные кадры ──
        while (true)
        {
            int recvResult = ffmpeg.avcodec_receive_frame(_codecContext, _frame);

            if (recvResult == ffmpeg.AVERROR(ffmpeg.EAGAIN) || recvResult == ffmpeg.AVERROR_EOF)
                break;

            if (recvResult < 0)
                break;

            // ── Шаг 4: копируем YUV420P в управляемый массив ──
            int width = _frame->width;
            int height = _frame->height;
            int ySize = width * height;
            int uvSize = ySize / 4;
            int totalSize = ySize + 2 * uvSize;

            byte[] yuvData = new byte[totalSize];

            // Y-плоскость
            CopyPlane(_frame->data[0], _frame->linesize[0], yuvData, 0,
                width, height);

            // U-плоскость
            CopyPlane(_frame->data[1], _frame->linesize[1], yuvData, ySize,
                width / 2, height / 2);

            // V-плоскость
            CopyPlane(_frame->data[2], _frame->linesize[2], yuvData, ySize + uvSize,
                width / 2, height / 2);

            var decodedFrame = new DecodedVideoFrame
            {
                Data = yuvData,
                Width = width,
                Height = height,
                Stride = width,          // плотно упаковано, stride = width
                SliceHeight = height,
                TimestampUs = timestampUs,
                Format = "I420"
            };

            FrameDecoded?.Invoke(this, new DecodedFrameEventArgs
            {
                Frame = decodedFrame,
                Decoder = this
            });
        }
    }

    /// <summary>
    /// Копирует одну плоскость с учётом stride в плотный массив.
    /// </summary>
    private static void CopyPlane(byte* src, int srcStride,
        byte[] dst, int dstOffset, int planeWidth, int planeHeight)
    {
        if (srcStride == planeWidth)
        {
            // Stride совпадает — копируем одним блоком
            Marshal.Copy((IntPtr)src, dst, dstOffset, planeWidth * planeHeight);
        }
        else
        {
            // Stride больше ширины — копируем построчно
            for (int row = 0; row < planeHeight; row++)
            {
                Marshal.Copy(
                    (IntPtr)(src + row * srcStride),
                    dst,
                    dstOffset + row * planeWidth,
                    planeWidth);
            }
        }
    }

    public void Dispose()
    {
        if (_frame != null)
        {
            var frame = _frame;                   // копия на стек
            ffmpeg.av_frame_free(&frame);         // &локальной — работает
            _frame = null;
        }

        if (_packet != null)
        {
            var packet = _packet;
            ffmpeg.av_packet_free(&packet);
            _packet = null;
        }

        if (_codecContext != null)
        {
            var ctx = _codecContext;
            ffmpeg.avcodec_free_context(&ctx);
            _codecContext = null;
        }

        _initialized = false;
    }

}
