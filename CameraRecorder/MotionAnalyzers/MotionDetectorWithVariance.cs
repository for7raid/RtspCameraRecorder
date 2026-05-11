namespace CameraRecorder.MotionAnalyzers;

public class MotionDetectorWithVariance
{
    private readonly Queue<int> _recentSizes = new Queue<int>();
    private readonly int _windowSize;
    private readonly double _sigmaThreshold;

    public MotionDetectorWithVariance(int windowSize = 10, double sigmaThreshold = 2.0)
    {
        _windowSize = windowSize;
        _sigmaThreshold = sigmaThreshold;
    }

    public bool IsMotionByVariance(int currentFrameSize)
    {
        // Добавляем текущий размер в окно
        _recentSizes.Enqueue(currentFrameSize);
        if (_recentSizes.Count > _windowSize)
            _recentSizes.Dequeue();

        // Нужно минимум 3 кадра для анализа
        if (_recentSizes.Count < 3)
            return false;

        // Вычисляем среднее
        double mean = _recentSizes.Average();

        // Вычисляем стандартное отклонение
        double variance = _recentSizes.Select(x => Math.Pow(x - mean, 2)).Average();
        double stdDev = Math.Sqrt(variance);

        // Если stdDev очень маленький — шум, движения нет
        if (stdDev < 15) // порог для шума (подбирается экспериментально)
            return false;

        // Текущий кадр — аномалия?
        double deviation = Math.Abs(currentFrameSize - mean);
        bool isAnomaly = deviation > _sigmaThreshold * stdDev;

        // Дополнительно: проверяем, что кадр больше среднего (движение увеличивает размер)
        bool isLargerThanMean = currentFrameSize > mean;

        return isAnomaly && isLargerThanMean;
    }
}