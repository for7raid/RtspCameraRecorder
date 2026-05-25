using Android.Graphics;
using Android.Media;
using Android.Views;
using CameraRecorder;
using CameraRecorder.VideoDecoders;
using Java.Nio;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace CameraRecorderAndroidApp;

public class H265Decoder : IH26xDecoder, IDisposable
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
    private readonly ConcurrentQueue<H26xFrameData> _inputQueue = new ConcurrentQueue<H26xFrameData>();

    // Событие для уведомления о декодированном кадре
    private readonly AutoResetEvent _inputSignal = new AutoResetEvent(false);

    // Параметры видео
    private int _width;
    private int _height;
    private readonly ILogger<H265Decoder> _logger;
    Surface? _surface;

    /// <summary>
    /// Событие возникает при декодировании нового кадра
    /// </summary>
    public event EventHandler<DecodedFrameEventArgs> FrameDecoded;

    public H265Decoder(ILogger<H265Decoder> logger)
    {

        _logger = logger;
    }

    public void SetOutputSurface(Surface? surface)
    {
        _surface = surface;
        _codec?.SetOutputSurface(_surface);
    }
    /// <summary>
    /// Инициализация и запуск декодера
    /// </summary>
    private bool Initialize()
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
                    _logger.LogWarning("H.265 не поддерживается аппаратно на этом устройстве.");
                    return false;
                }


                // Создаём декодер
                _codec = MediaCodec.CreateDecoderByType(MediaFormat.MimetypeVideoHevc);

                // Настраиваем формат
                MediaFormat format = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoHevc, _width, _height);

                // Важно: передаём null вместо Surface - декодируем в буферы
                _codec.Configure(format, _surface, null, MediaCodecConfigFlags.None);

                // Запускаем декодер
                _codec.Start();

                _isRunning = true;

                // Запускаем потоки для обработки
                _inputThread = new Thread(InputLoop) { Name = "H265InputThread", IsBackground = true };
                _outputThread = new Thread(OutputLoop) { Name = "H265OutputThread", IsBackground = true };
                _inputThread.Start();
                _outputThread.Start();

                _logger.LogInformation($"Декодер H.265 инициализирован: {_width}x{_height}.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка инициализации декодера.");
                return false;
            }
        }
    }

    /// <summary>
    /// Проверка поддержки H.265
    /// </summary>
    private bool IsHevcSupported()
    {
        //CheckH265ConcurrencyLimit();
        var codecList = new MediaCodecList(MediaCodecListKind.AllCodecs);
        var format = MediaFormat.CreateVideoFormat(MediaFormat.MimetypeVideoHevc, _width, _height);
        string codecName = codecList.FindDecoderForFormat(format);
        return codecName != null;
    }


    public static void CheckH265ConcurrencyLimit()
    {
        // 1. Получаем список всех кодеков на устройстве
        var codecList = new MediaCodecList(MediaCodecListKind.AllCodecs);
        var hevcCodecs = codecList.GetCodecInfos()
        .Where(codecInfo => codecInfo.IsEncoder == false) // Нам нужен ДЕкодер
        .Where(codecInfo => codecInfo.GetSupportedTypes().Contains("video/hevc"))
        .ToList();

        if (!hevcCodecs.Any())
        {
            Console.WriteLine("H.265 декодер не найден на этом устройстве");
            return;
        }

        Console.WriteLine($"Найдено {hevcCodecs.Count} кодек(ов) для H.265");

        foreach (var codecInfo in hevcCodecs)
        {
            // 2. Получаем возможности (Capabilities) для этого кодека
            var capabilities = codecInfo.GetCapabilitiesForType("video/hevc");

            // 3. Пытаемся прочитать максимальное количество поддерживаемых инстансов
            // Некоторые старые версии Android могут возвращать 0, если лимит не задан явно
            int maxInstances = capabilities.MaxSupportedInstances;

            if (maxInstances > 0)
            {
                Console.WriteLine($"Кодек '{codecInfo.Name}' поддерживает максимум {maxInstances} одновременных экземпляров");
            }
            else
            {
                // Если метод вернул 0, это обычно означает "без ограничений" или "очень много"
                Console.WriteLine($"Кодек '{codecInfo.Name}' не имеет жесткого лимита (или версия Android старая)");
            }
        }
    }



    /// <summary>
    /// Подать H.265 данные на декодирование
    /// </summary>
    public void DecodeFrame(byte[] h265Data, long timestampUs = 0, NalUnitType nalUnitType = NalUnitType.Unknown)
    {
        try
        {
            if (!_isRunning && nalUnitType == NalUnitType.H265_SPS)
            {
                var resolution = H265Parser.ParseResolution(h265Data);
                if (resolution.HasValue)
                {
                    _width = resolution.Value.width;
                    _height = resolution.Value.height;
                    Initialize();
                }
            }

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
                _inputQueue.Enqueue(new H26xFrameData
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
            else if (!_csdSent)
            {
                return;
            }
            else
            {
                _inputQueue.Enqueue(new H26xFrameData
                {
                    Data = h265Data,
                    TimestampUs = timestampUs,
                    NalUnitType = nalUnitType,
                    CodeFlag = MediaCodecBufferFlags.None

                });
            }
            _inputSignal.Set();

        }
        catch (Exception ex)
        {

            _logger.LogError(ex, "Ошибка декодирования видео фрейма.");
        }

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
                    _logger.LogError(ex, $"Ошибка в InputLoop.");
                }
            }
        }
    }

    /// <summary>
    /// Цикл получения декодированных данных
    /// </summary>
    private async void OutputLoop()
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

                    if (outputBuffer != null && bufferInfo.Size > 8)
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
                        var decodedFrame = new DecodedVideoFrame
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

                    _codec.ReleaseOutputBuffer(outputIndex, _surface != null);


                }
                else if (outputIndex == (int)MediaCodecInfoState.TryAgainLater)
                {
                    await Task.Delay(1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка в OutputLoop.");

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
                _logger.LogError(ex, $"Ошибка при остановке.");
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
