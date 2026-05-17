
using Microsoft.Extensions.Logging;
namespace CameraRecorder.MotionAnalyzers;

public enum PixelFormat
{
    RGB,     // 3 байта: R,G,B
    RGBA,    // 4 байта: R,G,B,A
    BGR,     // 3 байта: B,G,R (как в OpenCV)
    BGRA,    // 4 байта: B,G,R,A
    Y,       // 1 байт: яркость (оптимально!)
    YUV420,  // Планарный YUV (требует специальной обработки)
}

/// <summary>
/// Настройки детектора движения с автоадаптацией
/// </summary>
public class MotionDetectorSettings
{
    /// <summary>
    /// Размер блока в пикселях (NxN). Рекомендуется: 8, 16, 32
    /// </summary>
    public int BlockSize { get; set; } = 16;

    /// <summary>
    /// Количество хранимых последних кадров (буфер)
    /// </summary>
    public int FrameBufferSize { get; set; } = 30;

    /// <summary>
    /// Множитель сигмы шума для порога (обычно 2.0-4.0)
    /// Чем выше — тем меньше ложных срабатываний, но ниже чувствительность
    /// </summary>
    public double SigmaThreshold { get; set; } = 2.5;

    /// <summary>
    /// Минимальный процент изменившихся блоков для детекции движения (0.0-1.0)
    /// </summary>
    public double ChangedBlocksRatioThreshold { get; set; } = 0.05;

    /// <summary>
    /// Ширина изображения в пикселях
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Высота изображения в пикселях
    /// </summary>
    public int Height { get; set; }


    /// <summary>
    /// Минимальное количество кадров перед началом детекции
    /// </summary>
    public int MinFramesBeforeDetection { get; set; } = 10;

    /// <summary>
    /// Период пересчёта статистики освещения (количество кадров)
    /// </summary>
    public int StatsRecalculationPeriod { get; set; } = 30;

    /// <summary>
    /// Включить фильтрацию всплесков (подавление кратковременных шумов)
    /// </summary>
    public bool EnableSpikeFilter { get; set; } = true;

    /// <summary>
    /// Минимальная длительность движения в кадрах для подтверждения
    /// </summary>
    public int MinMotionDuration { get; set; } = 2;

    /// <summary>
    /// Максимальное изменение общей яркости кадра для адаптации (0-255)
    /// </summary>
    public byte MaxGlobalBrightnessChange { get; set; } = 50;

    public PixelFormat PixelFormat { get; set; } = PixelFormat.RGB; // для обратной совместимости

    public void Validate()
    {
        if (BlockSize < 1) throw new ArgumentException("BlockSize должен быть >= 1");
        if (FrameBufferSize < 1) throw new ArgumentException("FrameBufferSize должен быть >= 1");
        if (Width <= 0) throw new ArgumentException("Width должен быть > 0");
        if (Height <= 0) throw new ArgumentException("Height должен быть > 0");
        if (ChangedBlocksRatioThreshold < 0 || ChangedBlocksRatioThreshold > 1)
            throw new ArgumentException("ChangedBlocksRatioThreshold должен быть в диапазоне 0-1");
    }
}

/// <summary>
/// Статистика освещения кадра
/// </summary>
public class LightingStats
{
    public double GlobalBrightness { get; set; }      // Средняя яркость всего кадра
    public double BrightnessStdDev { get; set; }      // Стандартное отклонение яркости
    public double NoiseLevel { get; set; }            // Оценка уровня шума
    public double AdaptiveThreshold { get; set; }       // Адаптивный порог для текущих условий

    public override string ToString()
    {
        return $"Global={GlobalBrightness:F0}, Noise={NoiseLevel:F1}, BrightnessStdDev={BrightnessStdDev:F0}, Threshold={AdaptiveThreshold:F0}";
    }
}

/// <summary>
/// Результат анализа одного кадра
/// </summary>
public class MotionDetectionResult
{
    public bool HasMotion { get; set; }
    public double ChangedBlocksPercent { get; set; }
    public int ChangedBlocksCount { get; set; }
    public int TotalBlocksCount { get; set; }
    public double AverageChangeIntensity { get; set; }
    public bool[] ChangedBlocksMap { get; set; }
    public double ProcessingTimeMs { get; set; }
    public LightingStats LightingStats { get; set; }
    public double CurrentThreshold { get; set; }
    public bool IsAdapting { get; set; }
    public ulong RtpTimestamp { get; internal set; }

    public override string ToString()
    {
        return $"[{RtpTimestamp}] [{ProcessingTimeMs:0.00} ms] Motion: {(HasMotion ? "YES" : " NO")}, " +
               $"Changed: {ChangedBlocksCount:00}/{TotalBlocksCount} ({ChangedBlocksPercent:P2}), " +
               $"AverageChangeIntensity: {AverageChangeIntensity:00}, " +
               //$"Threshold: {CurrentThreshold}, " +
               $"Lighting: {LightingStats}";
    }
}

/// <summary>
/// Детектор движения с адаптацией к освещению
/// </summary>
public class AdaptiveMotionDetector
{
    private readonly MotionDetectorSettings _settings;
    private readonly ILogger<AdaptiveMotionDetector> _logger;
    private readonly CircularBuffer<byte[]> _frameBuffer;
    private readonly int _blocksPerRow;
    private readonly int _blocksPerCol;
    private readonly int _totalBlocks;

    private int _framesProcessed;
    private int _framesSinceLastStats;
    private LightingStats _currentLightingStats;
    private readonly CircularBuffer<double> _globalBrightnessHistory = new(10);
    private readonly CircularBuffer<double> _brightnessStdDevHistory = new(10);
    private readonly CircularBuffer<double> _noiseLevelHistory = new(10);
    private readonly CircularBuffer<double> _adaptiveThresholdHistory = new(10);

    // ── Стратегия извлечения яркости (выбирается один раз) ──
    private readonly Func<byte[], int, byte> _getBrightness;
    private readonly int _totalBytes;
    private readonly int _bytesPerPixel;

    // Для фильтрации всплесков
    private int _consecutiveMotionFrames;
    private int _consecutiveNoMotionFrames;

    // Recycled arrays for hot path
    private byte[] _medianBg;
    private bool[] _changedMap;

    // ── Helper: index = row * _blocksPerRow + col ──
    private int Idx(int row, int col) => row * _blocksPerRow + col;

    ushort[] _pixelToBlockMap;
    int[][] _colrowToPixelMap;

    public AdaptiveMotionDetector(MotionDetectorSettings settings, ILogger<AdaptiveMotionDetector> logger)
    {
        settings.Validate();
        _settings = settings;
        _logger = logger;
        _blocksPerCol = (int)Math.Round(settings.Height / (double)settings.BlockSize, MidpointRounding.ToPositiveInfinity);
        _blocksPerRow = (int)Math.Round(settings.Width / (double)settings.BlockSize, MidpointRounding.ToPositiveInfinity);
        _totalBlocks = _blocksPerCol * _blocksPerRow;

        _frameBuffer = new(_settings.FrameBufferSize);
        _bytesPerPixel = GetBytesPerPixel(_settings.PixelFormat);
        _getBrightness = settings.PixelFormat switch
        {
            PixelFormat.Y => static (data, i) => data[i],
            PixelFormat.RGB => RgbBrightness,
            PixelFormat.RGBA => RgbBrightness,
            PixelFormat.BGR => BgrBrightness,
            PixelFormat.BGRA => BgrBrightness,
            _ => throw new NotSupportedException($"Формат {settings.PixelFormat} не поддерживается")
        };

        _totalBytes = _settings.Width * _settings.Height * _bytesPerPixel;
        _pixelToBlockMap = new ushort[_totalBytes];
        for (int i = 0; i < _totalBytes; i += _bytesPerPixel)
        {
            int p = i / _bytesPerPixel;
            int y = p / _settings.Width;
            int x = p - y * _settings.Width;
            int idx = (y / _settings.BlockSize) * _blocksPerRow + (x / _settings.BlockSize);
            _pixelToBlockMap[i] = (ushort)idx;
        }

        _colrowToPixelMap = new int[_blocksPerCol][];
        for (int row = 0; row < _blocksPerCol; row++)
        {
            _colrowToPixelMap[row] = new int[_blocksPerRow];
            for (int col = 0; col < _blocksPerRow; col++)
            {
                _colrowToPixelMap[row][col] = Idx(row, col);
            }
        }

        _logger.LogWarning($"[AdaptiveMotionDetector] Инициализирован:");
        _logger.LogWarning($"  Размер: {settings.Width}x{settings.Height}");
        _logger.LogWarning($"  Блок: {settings.BlockSize}x{settings.BlockSize} -> {_blocksPerRow}x{_blocksPerCol} блоков");
        _logger.LogWarning($"  Буфер кадров: {settings.FrameBufferSize}");
    }

    // ── Статические хелперы яркости (JIT встроит) ──
    private static byte RgbBrightness(byte[] d, int i) =>
        (byte)((299 * d[i] + 587 * d[i + 1] + 114 * d[i + 2]) / 1000);

    private static byte BgrBrightness(byte[] d, int i) =>
        (byte)((299 * d[i + 2] + 587 * d[i + 1] + 114 * d[i]) / 1000);

    private static int GetBytesPerPixel(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Y => 1,
            PixelFormat.RGB or PixelFormat.BGR => 3,
            PixelFormat.RGBA or PixelFormat.BGRA => 4,
            _ => throw new NotSupportedException()
        };
    }

    int[] _brightnessblockSums;
    byte[] _brightnessMap;
    /// <summary>
    /// Получение одномерной карты яркости блоков (row-major)
    /// </summary>
    private byte[] GetBlockBrightnessMap(byte[] imageData)
    {
        if (_brightnessblockSums == null)
        {
            _brightnessblockSums = new int[_totalBlocks];
        }
        else
        {
            Array.Clear(_brightnessblockSums, 0, _totalBlocks);
        }

        if (_brightnessMap == null)
        {
            _brightnessMap = new byte[_totalBlocks];
        }

        int w = _settings.Width, h = _settings.Height, bs = _settings.BlockSize;
        int blockSquare = bs * bs;

        for (int i = 0; i < _totalBytes; i += _bytesPerPixel)
        {
            var block = _pixelToBlockMap[i];
            _brightnessblockSums[block] += imageData[i];//_getBrightness(imageData, i);
        }

        
        int count = bs * bs;
        for (int i = 0; i < _totalBlocks; i++)
            _brightnessMap[i] = (byte)(_brightnessblockSums[i] / count);
        return _brightnessMap;
    }


    /// <summary>
    /// Расчёт статистики освещения
    /// </summary>
    internal LightingStats CalculateLightingStats(byte[] brightnessMap)
    {
        var stats = new LightingStats();

        // Средняя яркость кадра
        double totalBrightness = 0;
        for (int i = 0; i < _totalBlocks; i++)
            totalBrightness += brightnessMap[i];
        stats.GlobalBrightness = totalBrightness / _totalBlocks;

        // Стандартное отклонение (контрастность)
        double variance = 0;
        for (int i = 0; i < _totalBlocks; i++)
        {
            double d = brightnessMap[i] - stats.GlobalBrightness;
            variance += d * d;
        }
        variance /= _totalBlocks;
        stats.BrightnessStdDev = Math.Sqrt(variance);

        // Оценка уровня шума (среднее абсолютное отклонение между соседними блоками)
        double noiseSum = 0;
        int noiseCount = 0;
        for (int row = 0; row < _blocksPerCol - 1; row++)
        {
            for (int col = 0; col < _blocksPerRow - 1; col++)
            {
                int diffHorizontal = Math.Abs(brightnessMap[_colrowToPixelMap[row][col]] - brightnessMap[_colrowToPixelMap[row][col + 1]]);
                int diffVertical = Math.Abs(brightnessMap[_colrowToPixelMap[row][col]] - brightnessMap[_colrowToPixelMap[row + 1][col]]);
                noiseSum += diffHorizontal + diffVertical;
                noiseCount += 2;
            }
        }
        stats.NoiseLevel = noiseCount > 0 ? noiseSum / noiseCount : 0;

        // Сигма-модель: порог = k * уровень_шума
        double newThreshold = _settings.SigmaThreshold * stats.NoiseLevel;

        // При низкой контрастности чуть повышаем порог (тяжело отличить сигнал от шума)
        if (stats.BrightnessStdDev < 15)
            newThreshold *= 1.3;

        // Не даём порогу уйти в 0 или в бесконечность
        newThreshold = Math.Clamp(newThreshold, 3.0, 250.0);

        stats.AdaptiveThreshold = newThreshold;

        return stats;
    }

    /// <summary>
    /// Получение фоновой карты (медианный фильтр или адаптивный фон)
    /// </summary>
    internal byte[] GetBackgroundMap()
    {

        if (_frameBuffer.Length < 3)
            return null;

        if (_medianBg == null || _medianBg.Length != _totalBlocks)
            _medianBg = new byte[_totalBlocks];
        var medianBackground = _medianBg;
        var temp = new byte[_frameBuffer.Length];
        int count = _frameBuffer.Length;
        int mid = count / 2;
        bool even = count % 2 == 0;

        var frames = new byte[count][];
        for (int j = 0; j < count; j++)
            frames[j] = _frameBuffer.GetAt(j);

        for (int i = 0; i < _totalBlocks; i++)
        {
            for (int j = 0; j < count; j++)
                temp[j] = frames[j][i];

            Array.Sort(temp);
            medianBackground[i] = even
                ? (byte)((temp[mid - 1] + temp[mid]) / 2)
                : temp[mid];
        }

        return medianBackground;
    }

    // ── EMA-вариант фона (быстрее, без буфера кадров) ──
    private double[] _emaBackground;
    private byte[] _emaBgOut;

    /// <summary>
    /// Экспоненциальное скользящее среднее фона.
    /// Не требует буфера кадров — обновляется каждый вызов.
    /// Вызывать ДО добавления текущего кадра в буфер.
    /// </summary>
    internal byte[] GetBackgroundMapEMA(byte[] currentMap, double alpha = 0.05)
    {
        if (_emaBackground == null)
        {
            _emaBackground = new double[_totalBlocks];
            for (int i = 0; i < _totalBlocks; i++)
                _emaBackground[i] = currentMap[i];
            _emaBgOut = new byte[_totalBlocks];
        }
        else
        {
            double invAlpha = 1.0 - alpha;
            for (int i = 0; i < _totalBlocks; i++)
                _emaBackground[i] = _emaBackground[i] * invAlpha + currentMap[i] * alpha;
        }

        for (int i = 0; i < _totalBlocks; i++)
            _emaBgOut[i] = (byte)Math.Round(_emaBackground[i]);

        return _emaBgOut;
    }


    /// <summary>
    /// Сравнение двух карт яркости
    /// </summary>
    private (bool[] map, int changedCount, double changedRatio, double avgChangeIntensity) CompareBrightnessMaps(byte[] current, byte[] reference, double threshold)
    {
        if (_changedMap == null || _changedMap.Length != _totalBlocks)
            _changedMap = new bool[_totalBlocks];
        Array.Clear(_changedMap, 0, _totalBlocks);
        var changed = _changedMap;
        long totalDiff = 0;
        int changedCount = 0;

        for (int i = 0; i < _totalBlocks; i++)
        {
            int diff = Math.Abs(current[i] - reference[i]);
            if (diff >= threshold)
            {
                changed[i] = true;
                totalDiff += diff;
                changedCount++;
            }
        }

        var avgChangeIntensity = changedCount > 0 ? (double)totalDiff / changedCount : 0;
        double changedRatio = (double)changedCount / _totalBlocks;

        return (changed, changedCount, changedRatio, avgChangeIntensity);
    }

    /// <summary>
    /// Фильтрация всплесков (кратковременных движений)
    /// </summary>
    private bool FilterSpikes(bool currentMotion)
    {
        if (!_settings.EnableSpikeFilter)
            return currentMotion;

        if (currentMotion)
        {
            _consecutiveMotionFrames++;
            _consecutiveNoMotionFrames = 0;
        }
        else
        {
            _consecutiveNoMotionFrames++;
            _consecutiveMotionFrames = 0;
        }

        // Движение подтверждается только после N последовательных кадров
        if (_consecutiveMotionFrames >= _settings.MinMotionDuration)
            return true;

        // Если было движение, но быстро прекратилось - это шум
        if (_consecutiveMotionFrames > 0 && _consecutiveMotionFrames < _settings.MinMotionDuration)
            return false;

        return currentMotion;
    }

    /// <summary>
    /// Обновление статистики освещения (периодически)
    /// </summary>



    private void UpdateLightingStatsPeriodic_(byte[] brightnessMap)
    {
        _framesSinceLastStats++;

        if (_framesSinceLastStats >= _settings.StatsRecalculationPeriod)
        {
            var stats = CalculateLightingStats(brightnessMap);

            _globalBrightnessHistory.Add(stats.GlobalBrightness);
            _brightnessStdDevHistory.Add(stats.BrightnessStdDev);
            _noiseLevelHistory.Add(stats.NoiseLevel);
            _adaptiveThresholdHistory.Add(stats.AdaptiveThreshold);


            _currentLightingStats = new LightingStats
            {
                GlobalBrightness = MedianForSmallArray(_globalBrightnessHistory.Buffer, _globalBrightnessHistory.Length),
                BrightnessStdDev = MedianForSmallArray(_brightnessStdDevHistory.Buffer, _globalBrightnessHistory.Length),
                NoiseLevel = MedianForSmallArray(_noiseLevelHistory.Buffer, _noiseLevelHistory.Length),
                AdaptiveThreshold = MedianForSmallArray(_adaptiveThresholdHistory.Buffer, _adaptiveThresholdHistory.Length),
            };
            _framesSinceLastStats = 0;
        }
    }
    private void UpdateLightingStatsPeriodic(byte[] brightnessMap)
    {

        _framesSinceLastStats++;

        if (_framesSinceLastStats >= _settings.StatsRecalculationPeriod)
        {
            var stats = CalculateLightingStats(brightnessMap);
            _currentLightingStats = _currentLightingStats == null
                ? stats
                : new LightingStats
                {
                    GlobalBrightness = _currentLightingStats.GlobalBrightness * 0.8 + stats.GlobalBrightness * 0.2,
                    BrightnessStdDev = _currentLightingStats.BrightnessStdDev * 0.8 + stats.BrightnessStdDev * 0.2,
                    NoiseLevel = _currentLightingStats.NoiseLevel * 0.8 + stats.NoiseLevel * 0.2,
                    AdaptiveThreshold = _currentLightingStats.AdaptiveThreshold * 0.8 + stats.AdaptiveThreshold * 0.2,
                };
            _framesSinceLastStats = 0;
        }
    }

    public static double MedianForSmallArray(double[] arr, int length)
    {
        // Предоплаченный буфер на стеке (избегаем аллокации)
        Span<double> span = stackalloc double[length];
        arr.AsSpan().Slice(0, length).CopyTo(span);
        span.Sort();
        int n = length;
        return n % 2 == 1 ? span[n / 2] : (span[n / 2 - 1] + span[n / 2]) / 2.0;
    }

    /// <summary>
    /// Основной метод детекции движения
    /// </summary>
    public MotionDetectionResult DetectMotion(byte[] rgbaData, ulong RtpTimestamp)
    {
        var startTime = DateTime.Now;
        var result = new MotionDetectionResult() { RtpTimestamp = RtpTimestamp };


        if (rgbaData.Length != _settings.Width * _settings.Height * _bytesPerPixel)
        {
            throw new ArgumentException(
                $"Размер данных не соответствует изображению. " +
                $"Ожидается: {_settings.Width * _settings.Height * _bytesPerPixel}, получено: {rgbaData.Length}");
        }


        // Получаем карту яркости
        var currentMap = GetBlockBrightnessMap(rgbaData);

        // Получаем эталонный фон
        byte[] background = GetBackgroundMapEMA(currentMap);
        // Добавляем в буфер
        //_frameBuffer.Add(currentMap);

        _framesProcessed++;

        // Обновляем статистику освещения
        UpdateLightingStatsPeriodic(currentMap);

        // Проверяем, достаточно ли кадров
        if (_framesProcessed < _settings.MinFramesBeforeDetection)
        {
            result.HasMotion = false;
            result.IsAdapting = true;
            result.ProcessingTimeMs = (DateTime.Now - startTime).TotalMilliseconds;
            result.CurrentThreshold = _currentLightingStats?.AdaptiveThreshold ?? 25.0;
            return result;
        }

        if (background == null)
        {
            result.HasMotion = false;
            result.ProcessingTimeMs = (DateTime.Now - startTime).TotalMilliseconds;
            return result;
        }

        // Получаем актуальный порог
        double threshold = _currentLightingStats?.AdaptiveThreshold ?? 25.0;

        // Сравниваем с фоном
        var changedMap = CompareBrightnessMaps(currentMap, background, threshold);

        // Определяем наличие движения
        bool rawMotion = changedMap.changedRatio >= _settings.ChangedBlocksRatioThreshold;
        bool filteredMotion = FilterSpikes(rawMotion);

        // Заполняем результат
        result.HasMotion = filteredMotion;
        result.ChangedBlocksCount = changedMap.changedCount;
        result.ChangedBlocksPercent = changedMap.changedRatio;
        result.TotalBlocksCount = _totalBlocks;
        result.ChangedBlocksMap = changedMap.map;
        result.AverageChangeIntensity = changedMap.avgChangeIntensity;
        result.LightingStats = _currentLightingStats;
        result.CurrentThreshold = threshold;
        result.IsAdapting = _framesProcessed < _settings.MinFramesBeforeDetection;
        result.ProcessingTimeMs = (DateTime.Now - startTime).TotalMilliseconds;

        _logger.LogInformation(result.ToString());

        return result;
    }

    /// <summary>
    /// Принудительный сброс адаптации
    /// </summary>
    public void Reset()
    {
        _frameBuffer.Clear();
        _medianBg = null;
        _changedMap = null;
        _adaptiveThresholdHistory.Clear();
        _globalBrightnessHistory.Clear();
        _noiseLevelHistory.Clear();
        _brightnessStdDevHistory.Clear();
        _currentLightingStats = null;
        _emaBackground = null;
        _framesProcessed = 0;
        _framesSinceLastStats = 0;
        _consecutiveMotionFrames = 0;
        _consecutiveNoMotionFrames = 0;
        _logger.LogWarning("[AdaptiveMotionDetector] Сброшен");
    }

    /// <summary>
    /// Получение текущей статистики адаптации
    /// </summary>
    public string GetAdaptationStats()
    {
        var stats = _currentLightingStats;
        return $"Кадров: {_framesProcessed}, " +
               $"Сигма: {_settings.SigmaThreshold:F1}, " +
               $"Шум: {stats?.NoiseLevel ?? 0:F2}, " +
               $"Порог: {stats?.AdaptiveThreshold ?? 0:F1}";
    }
}

public class CircularBuffer<T>
{
    public T[] Buffer { get; init; }
    private int tail = 0;
    private int length = 0;  // Текущее количество элементов

    public CircularBuffer(int capacity)
    {
        Buffer = new T[capacity];
    }

    public void Add(T item)
    {
        Buffer[tail] = item;
        tail = (tail + 1) % Buffer.Length;

        if (length < Buffer.Length)
            length++;
    }
    public void Clear()
    {
        // Очищаем ссылки на элементы (важно для сборщика мусора, если T - ссылочный тип)
        Array.Clear(Buffer, 0, Buffer.Length);

        // Сбрасываем указатели и счетчик
        tail = 0;
        length = 0;
    }

    public T GetAt(int index)
    {
        if (index < 0 || index >= length)
            throw new IndexOutOfRangeException($"Index {index} is out of range. Valid range: 0 to {length - 1}");

        // Вычисляем реальную позицию в буфере с учетом циклического хвоста
        int position = (tail - length + index) % Buffer.Length;

        // Обработка отрицательного остатка в C#
        if (position < 0)
            position += Buffer.Length;

        return Buffer[position];
    }

    public int Length => length;
    public int Capacity => Buffer.Length;  // Вместимость буфера
}
