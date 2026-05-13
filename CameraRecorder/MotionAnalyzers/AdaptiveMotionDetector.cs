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
    /// Скорость адаптации к изменению освещения (0-1, 0=медленно, 1=быстро)
    /// </summary>
    public double AdaptationSpeed { get; set; } = 0.1;

    /// <summary>
    /// Минимальное количество кадров перед началом детекции
    /// </summary>
    public int MinFramesBeforeDetection { get; set; } = 10;

    /// <summary>
    /// Период пересчёта статистики освещения (количество кадров)
    /// </summary>
    public int StatsRecalculationPeriod { get; set; } = 30;

    /// <summary>
    /// Использовать адаптивный фон (медианный фильтр)
    /// </summary>
    public bool UseAdaptiveBackground { get; set; } = true;

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
        if (AdaptationSpeed < 0 || AdaptationSpeed > 1)
            throw new ArgumentException("AdaptationSpeed должен быть в диапазоне 0-1");
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
        return $"Global={GlobalBrightness:F1}, Noise={NoiseLevel:F1}, BrightnessStdDev={BrightnessStdDev:F1}, Threshold={AdaptiveThreshold:F1}";
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

    public override string ToString()
    {
        return $"Motion: {(HasMotion ? "YES" : " NO")}, " +
               $"Changed: {ChangedBlocksCount}/{TotalBlocksCount} ({ChangedBlocksPercent:F2}%), " +
               $"AverageChangeIntensity: {AverageChangeIntensity:F1}, " +
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
    private readonly Queue<byte[]> _frameBuffer;
    private readonly Queue<double> _globalBrightnessHistory;
    private readonly int _blocksPerRow;
    private readonly int _blocksPerCol;
    private readonly int _totalBlocks;

    private int _framesProcessed;
    private int _framesSinceLastStats;
    private LightingStats _currentLightingStats;
    private byte[] _adaptiveBackground;
    private double _referenceGlobalBrightness;
    private double _brightnessSum;

    // Для фильтрации всплесков
    private int _consecutiveMotionFrames;
    private int _consecutiveNoMotionFrames;

    // ── Helper: index = row * _blocksPerRow + col ──
    private int Idx(int row, int col) => row * _blocksPerRow + col;


    public AdaptiveMotionDetector(MotionDetectorSettings settings, ILogger<AdaptiveMotionDetector> logger)
    {
        settings.Validate();
        _settings = settings;
        _logger = logger;
        _blocksPerCol = settings.Height / settings.BlockSize;
        _blocksPerRow = settings.Width / settings.BlockSize;
        _totalBlocks = _blocksPerCol * _blocksPerRow;

        _frameBuffer = new Queue<byte[]>();
        _globalBrightnessHistory = new Queue<double>();

        _logger.LogWarning($"[AdaptiveMotionDetector] Инициализирован:");
        _logger.LogWarning($"  Размер: {settings.Width}x{settings.Height}");
        _logger.LogWarning($"  Блок: {settings.BlockSize}x{settings.BlockSize} -> {_blocksPerRow}x{_blocksPerCol} блоков");
        _logger.LogWarning($"  Буфер кадров: {settings.FrameBufferSize}");
    }

    /// <summary>
    /// Извлечение средней яркости блока из RGBA данных
    /// </summary>
    private byte GetBlockAverageBrightness(byte[] imageData, int blockRow, int blockCol)
    {
        int startX = blockCol * _settings.BlockSize;
        int startY = blockRow * _settings.BlockSize;

        int bytesPerPixel = GetBytesPerPixel(_settings.PixelFormat);
        int sum = 0;
        int pixelCount = 0;

        for (int y = 0; y < _settings.BlockSize && startY + y < _settings.Height; y++)
        {
            for (int x = 0; x < _settings.BlockSize && startX + x < _settings.Width; x++)
            {
                int pixelIndex = ((startY + y) * _settings.Width + (startX + x)) * bytesPerPixel;

                byte brightness;

                switch (_settings.PixelFormat)
                {
                    case PixelFormat.Y:
                        brightness = imageData[pixelIndex];
                        break;

                    case PixelFormat.RGB:
                        byte r = imageData[pixelIndex];
                        byte g = imageData[pixelIndex + 1];
                        byte b = imageData[pixelIndex + 2];
                        brightness = (byte)((299 * r + 587 * g + 114 * b) / 1000);
                        break;

                    case PixelFormat.BGR:
                        byte bBgr = imageData[pixelIndex];
                        byte gBgr = imageData[pixelIndex + 1];
                        byte rBgr = imageData[pixelIndex + 2];
                        brightness = (byte)((299 * rBgr + 587 * gBgr + 114 * bBgr) / 1000);
                        break;

                    case PixelFormat.RGBA:
                        r = imageData[pixelIndex];
                        g = imageData[pixelIndex + 1];
                        b = imageData[pixelIndex + 2];
                        // альфа игнорируется
                        brightness = (byte)((299 * r + 587 * g + 114 * b) / 1000);
                        break;

                    case PixelFormat.BGRA:
                        b = imageData[pixelIndex];
                        g = imageData[pixelIndex + 1];
                        r = imageData[pixelIndex + 2];
                        brightness = (byte)((299 * r + 587 * g + 114 * b) / 1000);
                        break;

                    default:
                        throw new NotSupportedException($"Формат {_settings.PixelFormat} не поддерживается");
                }

                sum += brightness;
                pixelCount++;
            }
        }

        return (byte)(pixelCount > 0 ? sum / pixelCount : 0);
    }

    private int GetBytesPerPixel(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Y => 1,
            PixelFormat.RGB or PixelFormat.BGR => 3,
            PixelFormat.RGBA or PixelFormat.BGRA => 4,
            _ => throw new NotSupportedException()
        };
    }

    /// <summary>
    /// Получение одномерной карты яркости блоков (row-major)
    /// </summary>
    private byte[] GetBlockBrightnessMap(byte[] rgbaData)
    {
        var brightnessMap = new byte[_totalBlocks];

        for (int row = 0; row < _blocksPerCol; row++)
        {
            for (int col = 0; col < _blocksPerRow; col++)
            {
                brightnessMap[Idx(row, col)] = GetBlockAverageBrightness(rgbaData, row, col);
            }
        }

        return brightnessMap;
    }

    /// <summary>
    /// Расчёт статистики освещения
    /// </summary>
    private LightingStats CalculateLightingStats(byte[] brightnessMap)
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
                int diffHorizontal = Math.Abs(brightnessMap[Idx(row, col)] - brightnessMap[Idx(row, col + 1)]);
                int diffVertical = Math.Abs(brightnessMap[Idx(row, col)] - brightnessMap[Idx(row + 1, col)]);
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
        newThreshold = Math.Clamp(newThreshold, 3.0, 200.0);

        stats.AdaptiveThreshold = newThreshold;

        return stats;
    }

    /// <summary>
    /// Обновление адаптивного фона
    /// </summary>
    private void UpdateAdaptiveBackground(byte[] currentMap)
    {
        if (_adaptiveBackground == null)
        {
            _adaptiveBackground = new byte[_totalBlocks];
            Array.Copy(currentMap, _adaptiveBackground, _totalBlocks);
            return;
        }

        // Экспоненциальное скользящее среднее для фона
        double alpha = _settings.AdaptationSpeed;

        for (int i = 0; i < _totalBlocks; i++)
        {
            _adaptiveBackground[i] = (byte)(
                _adaptiveBackground[i] * (1 - alpha) +
                currentMap[i] * alpha
            );
        }
    }

    /// <summary>
    /// Получение фоновой карты (медианный фильтр или адаптивный фон)
    /// </summary>
    private byte[] GetBackgroundMap()
    {
        if (_settings.UseAdaptiveBackground && _adaptiveBackground != null)
        {
            return _adaptiveBackground;
        }

        if (_frameBuffer.Count < 3)
            return null;

        var frameList = _frameBuffer.ToList();
        var medianBackground = new byte[_totalBlocks];
        var temp = new byte[frameList.Count];
        int count = frameList.Count;
        int mid = count / 2;
        bool even = count % 2 == 0;

        for (int i = 0; i < _totalBlocks; i++)
        {
            for (int j = 0; j < count; j++)
                temp[j] = frameList[j][i];

            Array.Sort(temp);
            medianBackground[i] = even
                ? (byte)((temp[mid - 1] + temp[mid]) / 2)
                : temp[mid];
        }

        return medianBackground;
    }

    /// <summary>
    /// Сравнение двух карт яркости
    /// </summary>
    private bool[] CompareBrightnessMaps(byte[] current, byte[] reference, double threshold)
    {
        var changed = new bool[_totalBlocks];

        for (int i = 0; i < _totalBlocks; i++)
        {
            int diff = Math.Abs(current[i] - reference[i]);
            changed[i] = diff >= threshold;
        }

        return changed;
    }

    /// <summary>
    /// Проверка глобального изменения освещения
    /// </summary>
    private bool IsGlobalLightingChange(byte[] currentMap)
    {
        if (_globalBrightnessHistory.Count < 10)
            return false;

        if (_referenceGlobalBrightness == 0)
        {
            _referenceGlobalBrightness = _brightnessSum / _globalBrightnessHistory.Count;
            return false;
        }

        double currentGlobal = _globalBrightnessHistory.Last();
        double change = Math.Abs(currentGlobal - _referenceGlobalBrightness);

        _referenceGlobalBrightness = _referenceGlobalBrightness * 0.995 + currentGlobal * 0.005;

        return change > _settings.MaxGlobalBrightnessChange;
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
    private void UpdateLightingStatsPeriodic(byte[] brightnessMap)
    {
        _framesSinceLastStats++;

        if (_framesSinceLastStats >= _settings.StatsRecalculationPeriod)
        {
            _currentLightingStats = CalculateLightingStats(brightnessMap);
            _framesSinceLastStats = 0;


        }
    }

    /// <summary>
    /// Основной метод детекции движения
    /// </summary>
    public MotionDetectionResult DetectMotion(byte[] rgbaData)
    {
        var startTime = DateTime.Now;
        var result = new MotionDetectionResult();

        int bytesPerPixel = GetBytesPerPixel(_settings.PixelFormat);


        if (rgbaData.Length != _settings.Width * _settings.Height * bytesPerPixel)
        {
            throw new ArgumentException(
                $"Размер данных не соответствует изображению. " +
                $"Ожидается: {_settings.Width * _settings.Height * bytesPerPixel}, получено: {rgbaData.Length}");
        }

        // Получаем карту яркости
        var currentMap = GetBlockBrightnessMap(rgbaData);

        // Обновляем историю глобальной яркости
        long sum = 0;
        for (int i = 0; i < _totalBlocks; i++)
            sum += currentMap[i];
        double globalBrightness = (double)sum / _totalBlocks;

        _brightnessSum += globalBrightness;
        _globalBrightnessHistory.Enqueue(globalBrightness);

        while (_globalBrightnessHistory.Count > 100)
        {
            _brightnessSum -= _globalBrightnessHistory.Dequeue();
        }



        // Добавляем в буфер
        _frameBuffer.Enqueue(currentMap);
        while (_frameBuffer.Count > _settings.FrameBufferSize)
            _frameBuffer.Dequeue();

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

        // Получаем эталонный фон
        byte[] background = GetBackgroundMap();

        if (background == null)
        {
            result.HasMotion = false;
            result.ProcessingTimeMs = (DateTime.Now - startTime).TotalMilliseconds;
            return result;
        }

        // Проверяем глобальное изменение освещения
        bool isLightingChange = IsGlobalLightingChange(currentMap);

        if (isLightingChange)
        {
            // При изменении освещения ускоряем адаптацию фона
            UpdateAdaptiveBackground(currentMap);
            _currentLightingStats = CalculateLightingStats(currentMap);
            _framesSinceLastStats = 0;
            result.IsAdapting = true;
            result.HasMotion = false;
            result.ProcessingTimeMs = (DateTime.Now - startTime).TotalMilliseconds;
            result.CurrentThreshold = _currentLightingStats.AdaptiveThreshold;
            return result;
        }

        // Получаем актуальный порог
        double threshold = _currentLightingStats?.AdaptiveThreshold ?? 25.0;

        // Сравниваем с фоном
        var changedMap = CompareBrightnessMaps(currentMap, background, threshold);

        // Подсчитываем изменения
        int changedCount = 0;
        for (int i = 0; i < _totalBlocks; i++)
            if (changedMap[i]) changedCount++;
        double changedPercent = (double)changedCount / _totalBlocks * 100;

        // Вычисляем интенсивность изменений
        double avgChangeIntensity = 0;
        long totalDiff = 0;
        int diffCount = 0;
        for (int i = 0; i < _totalBlocks; i++)
        {
            int diff = Math.Abs(currentMap[i] - background[i]);
            if (diff >= threshold)
            {
                totalDiff += diff;
                diffCount++;
            }
        }
        avgChangeIntensity = diffCount > 0 ? (double)totalDiff / diffCount : 0;

        // Определяем наличие движения
        bool rawMotion = (changedPercent / 100.0) >= _settings.ChangedBlocksRatioThreshold;
        bool filteredMotion = FilterSpikes(rawMotion);

        // Заполняем результат
        result.HasMotion = filteredMotion;
        result.ChangedBlocksCount = changedCount;
        result.ChangedBlocksPercent = changedPercent;
        result.TotalBlocksCount = _totalBlocks;
        result.ChangedBlocksMap = changedMap;
        result.AverageChangeIntensity = avgChangeIntensity;
        result.LightingStats = _currentLightingStats;
        result.CurrentThreshold = threshold;
        result.IsAdapting = _framesProcessed < _settings.MinFramesBeforeDetection;
        result.ProcessingTimeMs = (DateTime.Now - startTime).TotalMilliseconds;

        // Обновляем адаптивный фон (даже при движении, но медленнее)
        if (_settings.UseAdaptiveBackground && !result.HasMotion)
        {
            UpdateAdaptiveBackground(currentMap);
        }

        _logger.LogInformation(result.ToString());

        return result;
    }

    /// <summary>
    /// Принудительный сброс адаптации
    /// </summary>
    public void Reset()
    {
        _frameBuffer.Clear();
        _globalBrightnessHistory.Clear();
        _adaptiveBackground = null;
        _currentLightingStats = null;
        _framesProcessed = 0;
        _framesSinceLastStats = 0;
        _consecutiveMotionFrames = 0;
        _consecutiveNoMotionFrames = 0;
        _referenceGlobalBrightness = 0;
        _brightnessSum = 0;
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
