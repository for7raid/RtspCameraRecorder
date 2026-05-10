namespace CameraRecorder;

public class MotionSensitivity
{
    /// <summary>
    /// Минимальный размер P/B кадра для проверки на движение (байт)
    /// Меньшие кадры считаются статичными
    /// </summary>
    public int MinFrameSizeForMotion { get; init; } = 80;

    /// <summary>
    /// Процент ненулевых байт в payload, необходимый для детекции движения
    /// 0.01 = 1%, 0.1 = 10%, 1.0 = 100%
    /// </summary>
    public double NonZeroBytesThreshold { get; init; } = 0.05; // 5%

    /// <summary>
    /// Количество байт для выборки при проверке ненулевых байт
    /// </summary>
    public int SampleSize { get; init; } = 100;

    /// <summary>
    /// Порог для остатка относительно среднего I-кадра
    /// 0.1 = 10%, 0.2 = 20% и т.д.
    /// </summary>
    public double ResidualRatioThreshold { get; init; } = 0.2;

    /// <summary>
    /// Включить детекцию по остатку
    /// </summary>
    public bool EnableResidualDetection { get; init; } = true;

    /// <summary>
    /// Включить детекцию по векторам
    /// </summary>
    public bool EnableVectorDetection { get; init; } = true;

    /// <summary>
    /// Минимальная длина payload для анализа
    /// </summary>
    public int MinPayloadLength { get; init; } = 10;

    /// <summary>
    /// Уровень детализации вывода (0-минимум, 1-нормальный, 2-подробный)
    /// </summary>
    public int VerboseLevel { get; init; } = 1;

    /// <summary>
    /// Порог движеня по количеству кадров с движением от общего количества кадров для анализа
    /// </summary>
    public double MovieFramesRatioThreshold { get; init; }
    /// <summary>
    /// Предустановки чувствительности
    /// </summary>


    public static MotionSensitivity SlowHand { get; } = new MotionSensitivity
    {
        // Базовые пороги для 2560x1440 @ 15 fps
        MinFrameSizeForMotion = 220,        // Минимальный размер P/B кадра (байт)
                                            // Медленный жест даёт ~250-400 байт
                                            // Статика даёт ~180-220 байт

        NonZeroBytesThreshold = 0.72,       // 72% ненулевых байт в payload
                                            // Медленный жест: 0.72-0.80
                                            // Статика: 0.65-0.71

        ResidualRatioThreshold = 0.025,     // 2.5% от среднего I-кадра (170КБ = ~4250 байт)
                                            // Медленный жест даёт 3-8КБ остатка

        SampleSize = 200,                   // Размер выборки (уже хорошо)

        EnableVectorDetection = true,
        EnableResidualDetection = true,     // Для плавных движений остаток важен

        MinPayloadLength = 20,
        VerboseLevel = 1,

        MovieFramesRatioThreshold = 0.2
    };
}
