using Android.Graphics;
using Android.Media;
using CameraRecorder;
using Java.Nio;
using System.Collections.Concurrent;

namespace CameraRecorderAndroidApp;

public class H265Decoder : IDisposable
{
    private MediaCodec _codec;
    private readonly object _lockObject = new object();
    private bool _isRunning;
    private Thread _inputThread;
    private Thread _outputThread;

    private byte[] _sps;
    private byte[] _vps;
    private byte[] _pps;
    private bool _csdSent;

    // Очереди для входных данных
    private readonly ConcurrentQueue<FrameData> _inputQueue = new ConcurrentQueue<FrameData>();

    // Событие для уведомления о декодированном кадре
    private readonly AutoResetEvent _inputSignal = new AutoResetEvent(false);

    // Параметры видео
    private readonly int _width;
    private readonly int _height;

    /// <summary>
    /// Событие возникает при декодировании нового кадра
    /// </summary>
    public event EventHandler<DecodedFrameEventArgs> FrameDecoded;

    public H265Decoder(int width, int height)
    {
        _width = width;
        _height = height;
    }

    /// <summary>
    /// Инициализация и запуск декодера
    /// </summary>
    public bool Initialize()
    {
        lock (_lockObject)
        {
            if (_isRunning)
            {
                return true;
            }

            try
            {
                // Проверяем поддержку H.265
                if (!IsHevcSupported())
                {
                    Console.WriteLine("H.265 не поддерживается аппаратно на этом устройстве");
                    return false;
                }

                // Создаём декодер
                _codec = MediaCodec.CreateDecoderByType(MediaFormat.MimetypeVideoHevc);

                // Настраиваем формат
                MediaFormat format = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoHevc, _width, _height);

                // Важно: передаём null вместо Surface - декодируем в буферы
                _codec.Configure(format, null, null, MediaCodecConfigFlags.None);

                // Запускаем декодер
                _codec.Start();

                _isRunning = true;

                // Запускаем потоки для обработки
                _inputThread = new Thread(InputLoop) { Name = "H265InputThread", IsBackground = true };
                _outputThread = new Thread(OutputLoop) { Name = "H265OutputThread", IsBackground = true };
                _inputThread.Start();
                _outputThread.Start();

                Console.WriteLine($"Декодер H.265 инициализирован: {_width}x{_height}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка инициализации декодера: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Проверка поддержки H.265
    /// </summary>
    private bool IsHevcSupported()
    {
        var codecList = new MediaCodecList(MediaCodecListKind.AllCodecs);
        var format = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoHevc, _width, _height);
        string codecName = codecList.FindDecoderForFormat(format);
        return codecName != null;
    }

    /// <summary>
    /// Подать H.265 данные на декодирование
    /// </summary>
    public void DecodeFrame(byte[] h265Data, long timestampUs = 0, NalUnitType nalUnitType = NalUnitType.Unknown)
    {
        if (!_isRunning || h265Data == null || h265Data.Length == 0)
            return;

        switch (nalUnitType)
        {
            case NalUnitType.H265_SPS:
                _sps = h265Data;
                break;
            case NalUnitType.H265_VPS:
                _vps = h265Data;
                break;
            case NalUnitType.H265_PPS:
                _pps = h265Data;
                break;
        }


        if (_sps is not null && _vps is not null && _pps is not null
            && nalUnitType is NalUnitType.H265_SPS or NalUnitType.H265_VPS or NalUnitType.H265_PPS)
        {
            _inputQueue.Enqueue(new FrameData
            {
                Data = ConcatenateVpsSpsPps(_vps, _sps, _pps),
                TimestampUs = 0,
                NalUnitType = nalUnitType,
                CodeFlag = MediaCodecBufferFlags.CodecConfig,
            });
            _csdSent = true;

        }
        else if (nalUnitType is NalUnitType.H265_SPS or NalUnitType.H265_VPS or NalUnitType.H265_PPS)
        {
            return;
        }
        else
        {
            if (!_csdSent)
            {
                return;
            }

            _inputQueue.Enqueue(new FrameData
            {
                Data = h265Data,
                TimestampUs = timestampUs,
                NalUnitType = nalUnitType,
                CodeFlag = MediaCodecBufferFlags.None

            });
        }
        _inputSignal.Set();
    }

    /// <summary>
    /// Цикл подачи данных на вход декодера
    /// </summary>
    private void InputLoop()
    {
        while (_isRunning)
        {
            _inputSignal.WaitOne(10);

            while (_inputQueue.TryDequeue(out var frameData))
            {
                try
                {
                    // Запрашиваем входной буфер
                    int inputIndex = _codec.DequeueInputBuffer(10000);

                    if (inputIndex >= 0)
                    {
                        ByteBuffer inputBuffer = _codec.GetInputBuffer(inputIndex);
                        if (inputBuffer != null)
                        {
                            inputBuffer.Clear();
                            inputBuffer.Put(frameData.Data);

                            _codec.QueueInputBuffer(inputIndex, 0, frameData.Data.Length,
                                frameData.TimestampUs, frameData.CodeFlag);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в InputLoop: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Цикл получения декодированных данных
    /// </summary>
    private void OutputLoop()
    {
        var bufferInfo = new MediaCodec.BufferInfo();

        while (_isRunning)
        {
            try
            {
                int outputIndex = _codec.DequeueOutputBuffer(bufferInfo, 10000);

                if (outputIndex >= 0)
                {
                    // Получаем выходной буфер с декодированными данными
                    ByteBuffer outputBuffer = _codec.GetOutputBuffer(outputIndex);

                    if (outputBuffer != null && bufferInfo.Size > 0)
                    {
                        // Копируем данные из буфера
                        byte[] decodedData = new byte[bufferInfo.Size];
                        outputBuffer.Get(decodedData);
                       // Image outputImage = _codec.GetOutputImage(outputIndex);

                        // Получаем информацию о формате пикселей
                        var outputFormat = _codec.OutputFormat;
                        string pixelFormat = GetPixelFormat(outputFormat);
                        int stride = outputFormat.GetInteger(MediaFormat.KeyStride);
                        int sliceHeight = outputFormat.GetInteger(MediaFormat.KeySliceHeight);

                        // Создаём объект с декодированным кадром
                        var decodedFrame = new DecodedFrame
                        {
                            Data = decodedData,
                            Width = _width,
                            Height = _height,
                            Stride = stride > 0 ? stride : _width,
                            SliceHeight = sliceHeight > 0 ? sliceHeight : _height,
                            TimestampUs = bufferInfo.PresentationTimeUs,
                            Format = pixelFormat,
                            //Image = outputImage,
                        };

                        // Вызываем событие в UI потоке (или в потоке декодера)
                        OnFrameDecoded(decodedFrame);
                    }

                    // Освобождаем буфер (false = не выводить на Surface)
                    _codec.ReleaseOutputBuffer(outputIndex, false);
                }
                else if (outputIndex == (int)MediaCodecInfoState.TryAgainLater)
                {
                    // Нет данных, ждём
                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в OutputLoop: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Получение формата пикселей
    /// </summary>
    private string GetPixelFormat(MediaFormat format)
    {
        // Определяем цветовой формат
        if (format.ContainsKey(MediaFormat.KeyColorFormat))
        {
            int colorFormat = format.GetInteger(MediaFormat.KeyColorFormat);
            switch (colorFormat)
            {
                case 19:
                    return "I420";
                case 21: //FormatYUV420SemiPlanar
                    return "NV12";
                case (int)ImageFormatType.Nv21:
                    return "NV21";
                default:
                    return $"UNKNOWN_{colorFormat}";
            }
        }
        return "UNKNOWN";
    }

    /// <summary>
    /// Вызов события о декодировании кадра
    /// </summary>
    private void OnFrameDecoded(DecodedFrame frame)
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

    /// <summary>
    /// Остановка и освобождение ресурсов
    /// </summary>
    public void Stop()
    {
        lock (_lockObject)
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            _inputSignal.Set();

            _inputThread?.Join(1000);
            _outputThread?.Join(1000);

            try
            {
                _codec?.Stop();
                _codec?.Release();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке: {ex.Message}");
            }

            _codec = null;

            // Очищаем очередь
            _inputQueue.Clear();
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

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Входной кадр с H.265 данными
/// </summary>
public class FrameData
{
    public byte[] Data { get; set; }
    public long TimestampUs { get; set; }
    public NalUnitType NalUnitType { get; internal set; }
    public MediaCodecBufferFlags CodeFlag { get; internal set; }
}

/// <summary>
/// Декодированный кадр
/// </summary>
public class DecodedFrame : IDisposable
{
    public byte[] Data { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }      // Шаг по ширине (может быть больше Width)
    public int SliceHeight { get; set; }  // Высота слайса (может быть больше Height)
    public long TimestampUs { get; set; }
    public string Format { get; set; }

    /// <summary>
    /// Получить размер данных в байтах
    /// </summary>
    public int DataSize => Data?.Length ?? 0;

    public Image? Image { get; internal set; }

    /// <summary>
    /// Получить только массив яркости (Y-компонент)
    /// </summary>
    /// <returns>Массив яркости размером Width * Height</returns>
    public byte[] ToY()
    {
        if (Data == null || Width == 0 || Height == 0)
            return null;

        int ySize = Width * Height;
        byte[] yData = new byte[ySize];

        if (Format == "NV12" || Format == "NV21" || Format == "YUV420_SEMI_PLANAR")
        {
            // Форматы NV12/NV21: Y компонент идет первым блоком
            // Размер Y блока = Width * Height
            // Важно: учитываем stride если он больше Width
            if (Stride > Width)
            {
                // Копируем построчно с учетом stride
                for (int row = 0; row < Height; row++)
                {
                    int srcOffset = row * Stride;
                    int dstOffset = row * Width;
                    Array.Copy(Data, srcOffset, yData, dstOffset, Width);
                }
            }
            else
            {
                // Stride равен Width - просто копируем первые ySize байт
                Array.Copy(Data, 0, yData, 0, Math.Min(ySize, Data.Length));
            }
        }
        else if (Format == "YUV420_PLANAR" || Format == "I420")
        {
            // Формат I420: Y компонент идет первым блоком
            if (Stride > Width)
            {
                // Копируем построчно с учетом stride
                for (int row = 0; row < Height; row++)
                {
                    int srcOffset = row * Stride;
                    int dstOffset = row * Width;
                    Array.Copy(Data, srcOffset, yData, dstOffset, Width);
                }
            }
            else
            {
                Array.Copy(Data, 0, yData, 0, Math.Min(ySize, Data.Length));
            }
        }
        else if (Format == "YV12")
        {
            // YV12: Y компонент тоже первый блок
            Array.Copy(Data, 0, yData, 0, Math.Min(ySize, Data.Length));
        }
        else
        {
            // Неизвестный формат - пытаемся скопировать первые ySize байт
            Console.WriteLine($"Предупреждение: неизвестный формат {Format}, " +
                              $"попытка извлечь Y как первые {ySize} байт");
            if (Data.Length >= ySize)
            {
                Array.Copy(Data, 0, yData, 0, ySize);
            }
            else
            {
                return null;
            }
        }

        return yData;
    }

    /// <summary>
    /// Получить Y-компонент в виде массива байт с альтернативным выравниванием
    /// </summary>
    /// <param name="outputStride">Желаемый шаг выходной строки (если 0, то равен Width)</param>
    /// <returns>Массив яркости размером Height * outputStride</returns>
    public byte[] ToY(int outputStride)
    {
        if (Data == null || Width == 0 || Height == 0)
            return null;

        if (outputStride <= 0)
            outputStride = Width;

        byte[] yData = new byte[Height * outputStride];

        if (Stride > Width)
        {
            // Копируем с конвертацией stride
            for (int row = 0; row < Height; row++)
            {
                int srcOffset = row * Stride;
                int dstOffset = row * outputStride;

                // Копируем Width пикселей
                Array.Copy(Data, srcOffset, yData, dstOffset, Width);

                // Остальное (если outputStride > Width) останется нулями
            }
        }
        else
        {
            // Данные уже плотные, просто копируем построчно
            for (int row = 0; row < Height; row++)
            {
                int srcOffset = row * Width;
                int dstOffset = row * outputStride;
                Array.Copy(Data, srcOffset, yData, dstOffset, Width);
            }
        }

        return yData;
    }

    /// <summary>
    /// Быстрое получение Y-компонента без выделения новой памяти (опасно!)
    /// </summary>
    /// <returns>Массив яркости как сегмент исходных данных</returns>
    public ArraySegment<byte> GetYSegment()
    {
        if (Data == null || Width == 0 || Height == 0)
            return new ArraySegment<byte>();

        int ySize = Width * Height;

        if (Data.Length >= ySize)
        {
            return new ArraySegment<byte>(Data, 0, ySize);
        }

        return new ArraySegment<byte>();
    }

    /// <summary>
    /// Конвертировать в RGB
    /// </summary>
    public byte[] ToRgb()
    {
        if (Data == null) return null;

        byte[] yData = ToY();
        if (yData == null) return null;

        if (Format == "NV12" || Format == "NV21")
        {
            return ConvertNV12ToRGB(Data, Width, Height);
        }
        else if (Format == "YUV420_PLANAR" || Format == "I420")
        {
            return ConvertYUV420ToRGB(Data, Width, Height);
        }

        return null;
    }

    private byte[] ConvertNV12ToRGB(byte[] nv12, int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        int ySize = width * height;

        for (int i = 0; i < ySize; i++)
        {
            int y = nv12[i] & 0xFF;
            int uv = nv12[ySize + (i / 2)] & 0xFF;

            int r = (int)(y + 1.402f * (uv - 128));
            int g = (int)(y - 0.344f * (uv - 128) - 0.714f * (uv - 128));
            int b = (int)(y + 1.772f * (uv - 128));

            rgb[i * 3] = (byte)Math.Clamp(r, 0, 255);
            rgb[i * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
            rgb[i * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
        }

        return rgb;
    }

    private byte[] ConvertYUV420ToRGB(byte[] yuv, int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        int ySize = width * height;
        int uSize = ySize / 4;

        for (int i = 0; i < ySize; i++)
        {
            int y = yuv[i] & 0xFF;
            int u = yuv[ySize + (i / 4)] & 0xFF;
            int v = yuv[ySize + uSize + (i / 4)] & 0xFF;

            int r = (int)(y + 1.402f * (v - 128));
            int g = (int)(y - 0.344f * (u - 128) - 0.714f * (v - 128));
            int b = (int)(y + 1.772f * (u - 128));

            rgb[i * 3] = (byte)Math.Clamp(r, 0, 255);
            rgb[i * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
            rgb[i * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
        }

        return rgb;
    }

    public void Dispose()
    {
        Data = null;
        Image?.Dispose();
    }
}

/// <summary>
/// Аргументы события декодирования кадра
/// </summary>
public class DecodedFrameEventArgs : EventArgs
{
    public DecodedFrame Frame { get; set; }
    public H265Decoder Decoder { get; set; }
}