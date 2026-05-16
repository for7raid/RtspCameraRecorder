using CameraRecorder.MotionAnalyzers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraRecorder.Testing;

public class CalculateLightingStatsTests
{
    private const int BlockSize = 6;
    private const double SigmaThreshold = 2.5;

    private static AdaptiveMotionDetector CreateDetector(int width = 12, int height = 12)
    {
        var settings = new MotionDetectorSettings
        {
            Width = width,
            Height = height,
            BlockSize = BlockSize,
            PixelFormat = PixelFormat.Y,
            SigmaThreshold = SigmaThreshold,
            FrameBufferSize = 30,
        };
        return new AdaptiveMotionDetector(settings, NullLogger<AdaptiveMotionDetector>.Instance);
    }

    [Fact]
    public void Uniform_AllBlocks100_NoiseZero_ThresholdClampedTo3()
    {
        var detector = CreateDetector();
        byte[] brightnessMap = [100, 100, 100, 100]; // 2×2 blocks

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.Equal(100.0, stats.GlobalBrightness, precision: 3);
        Assert.Equal(0.0, stats.BrightnessStdDev, precision: 3);
        Assert.Equal(0.0, stats.NoiseLevel, precision: 3);
        Assert.Equal(3.0, stats.AdaptiveThreshold, precision: 3); // 0 clamped → 3
    }

    [Fact]
    public void LowContrast_StdDevBelow15_ThresholdMultipliedBy1_3()
    {
        var detector = CreateDetector();
        byte[] brightnessMap = [48, 52, 50, 49];

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.Equal(49.75, stats.GlobalBrightness, precision: 3);
        Assert.Equal(1.479, stats.BrightnessStdDev, precision: 1);
        Assert.True(stats.BrightnessStdDev < 15);
        // Noise: |48-52|=4, |48-50|=2 → (4+2)/2 = 3
        Assert.Equal(3.0, stats.NoiseLevel, precision: 3);
        // 2.5*3=7.5, StdDev<15 → *1.3 = 9.75
        Assert.Equal(9.75, stats.AdaptiveThreshold, precision: 3);
    }

    [Fact]
    public void NormalContrast_StdDevBelow15_Noise25()
    {
        var detector = CreateDetector();
        byte[] brightnessMap = [80, 100, 110, 90];

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.Equal(95.0, stats.GlobalBrightness, precision: 3);
        Assert.Equal(11.18, stats.BrightnessStdDev, precision: 1);
        // Noise: |80-100|=20, |80-110|=30 → (20+30)/2 = 25
        Assert.Equal(25.0, stats.NoiseLevel, precision: 3);
        // 2.5*25=62.5, StdDev<15 → *1.3 = 81.25
        Assert.Equal(81.25, stats.AdaptiveThreshold, precision: 3);
    }

    [Fact]
    public void Checkerboard_HighNoise_ThresholdClampedTo250()
    {
        var detector = CreateDetector();
        byte[] brightnessMap = [50, 150, 150, 50];

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.Equal(100.0, stats.GlobalBrightness, precision: 3);
        Assert.Equal(50.0, stats.BrightnessStdDev, precision: 3);
        Assert.True(stats.BrightnessStdDev >= 15);
        // Noise: |50-150|=100, |50-150|=100 → (100+100)/2 = 100
        Assert.Equal(100.0, stats.NoiseLevel, precision: 3);
        // 2.5*100=250, StdDev≥15 → no *1.3, clamp(250,3,250)=250
        Assert.Equal(250.0, stats.AdaptiveThreshold, precision: 3);
    }

    [Fact]
    public void EdgeCase_AllZeros_GlobalBrightnessZero()
    {
        var detector = CreateDetector();
        byte[] brightnessMap = [0, 0, 0, 0];

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.Equal(0.0, stats.GlobalBrightness);
        Assert.Equal(0.0, stats.BrightnessStdDev);
        Assert.Equal(0.0, stats.NoiseLevel);
        Assert.Equal(3.0, stats.AdaptiveThreshold); // 0 clamped
    }

    [Fact]
    public void EdgeCase_All255_BrightnessSaturated()
    {
        var detector = CreateDetector();
        byte[] brightnessMap = [255, 255, 255, 255];

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.Equal(255.0, stats.GlobalBrightness);
        Assert.Equal(0.0, stats.BrightnessStdDev);
        Assert.Equal(0.0, stats.NoiseLevel);
        Assert.Equal(3.0, stats.AdaptiveThreshold);
    }

    [Fact]
    public void LargerGrid_10x10Blocks_RandomValues()
    {
        var detector = CreateDetector(60, 60); // 10×10 blocks
        var rng = new Random(42);
        byte[] brightnessMap = new byte[100];
        for (int i = 0; i < 100; i++)
            brightnessMap[i] = (byte)rng.Next(80, 140);

        var stats = detector.CalculateLightingStats(brightnessMap);

        Assert.InRange(stats.GlobalBrightness, 80, 140);
        Assert.True(stats.BrightnessStdDev > 0);
        Assert.True(stats.NoiseLevel > 0);
        Assert.InRange(stats.AdaptiveThreshold, 3.0, 250.0);
    }

    [Fact]
    public void Grid6x5_HorizontalGradient_NoisePreciselyCalculated()
    {
        var detector = CreateDetector(36, 30); // 6×5 blocks
        // Яркость растёт слева направо: row=[20,40,60,80,100,120], все строки одинаковые
        byte[] brightnessMap = new byte[30];
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 6; col++)
                brightnessMap[row * 6 + col] = (byte)((col + 1) * 20);

        var stats = detector.CalculateLightingStats(brightnessMap);

        // Среднее: (20+40+60+80+100+120)/6 = 70
        Assert.Equal(70.0, stats.GlobalBrightness, precision: 3);

        // Шум: только по горизонтали (diffH=20), по вертикали строки одинаковые (diffV=0)
        // 5 rows × 5 horizontal gaps = 25 gaps, each |20-40|=20, |40-60|=20, ... → все по 20
        // 4 vertical gaps × 6 columns = 24 gaps, diffV=0
        // NoiseLevel = (25×20 + 24×0) / (25+24) = 500/49 ≈ 10.204
        Assert.InRange(stats.NoiseLevel, 10.0, 10.5);

        // Порог: 2.5 × NoiseLevel × (StdDev<15 ? 1.3 : 1), clamp(3,250)
        // BrightnessStdDev для [20,40,60,80,100,120]: mean=70
        // variance = (50²+30²+10²+10²+30²+50²)/6 = (2500+900+100+100+900+2500)/6 = 7000/6 = 1166.67
        // StdDev = sqrt(1166.67) ≈ 34.16 ≥ 15 → no *1.3
        Assert.True(stats.BrightnessStdDev >= 15);
        double expected = Math.Clamp(SigmaThreshold * stats.NoiseLevel, 3.0, 250.0);
        Assert.Equal(expected, stats.AdaptiveThreshold, precision: 3);
    }
}
