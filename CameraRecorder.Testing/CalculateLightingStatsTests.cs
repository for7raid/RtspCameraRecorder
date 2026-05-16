using CameraRecorder.MotionAnalyzers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraRecorder.Testing;

public class CalculateLightingStatsTests
{
    private const int Width = 12;
    private const int Height = 12;
    private const int BlockSize = 6;
    private const double SigmaThreshold = 2.5;

    /// <summary>
    /// Creates a Y-format (1 byte/pixel) image of the given size,
    /// filling each block-sized region with the specified per-block brightness value.
    /// Width and Height must be exact multiples of blockSize.
    /// </summary>
    private static byte[] CreateYImage(int width, int height, int blockSize, byte[] blockValues)
    {
        int blocksPerRow = width / blockSize;
        var image = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int blockRow = y / blockSize;
            for (int x = 0; x < width; x++)
            {
                int blockCol = x / blockSize;
                int blockIdx = blockRow * blocksPerRow + blockCol;
                image[y * width + x] = blockValues[blockIdx];
            }
        }
        return image;
    }

    /// <summary>
    /// Creates the detector and warms it up with 4 identical frames so that
    /// DetectMotion returns a result populated with LightingStats.
    /// Returns the result of the 4th call.
    /// </summary>
    private static MotionDetectionResult GetLightingResult(byte[] imageData, byte[] blockValues)
    {
        var settings = new MotionDetectorSettings
        {
            Width = Width,
            Height = Height,
            BlockSize = BlockSize,
            PixelFormat = PixelFormat.Y,
            SigmaThreshold = SigmaThreshold,
            MinFramesBeforeDetection = 0,
            StatsRecalculationPeriod = 1,
            FrameBufferSize = 30,
        };

        var detector = new AdaptiveMotionDetector(settings, NullLogger<AdaptiveMotionDetector>.Instance);

        // Frames 1–3: build up frame buffer (GetBackgroundMap is called before
        // the current frame is added, so we need 4 calls for buffer to have ≥ 3 items)
        detector.DetectMotion(imageData, 1);
        detector.DetectMotion(imageData, 2);
        detector.DetectMotion(imageData, 3);

        // Frame 4: LightingStats will be populated
        return detector.DetectMotion(imageData, 4);
    }

    /// <summary>
    /// Uniform brightness: all blocks = 100.
    /// Expected: Noise≈0, StdDev=0, Threshold=3 (clamped from 0*2.5 → 0, then 0*1.3=0, clamp to 3).
    /// </summary>
    [Fact]
    public void UniformBrightness_AllBlocks100_NoiseNearZero_ThresholdClampedTo3()
    {
        byte[] blockValues = [100, 100, 100, 100];
        byte[] imageData = CreateYImage(Width, Height, BlockSize, blockValues);

        var result = GetLightingResult(imageData, blockValues);
        var stats = result.LightingStats;

        Assert.NotNull(stats);
        Assert.Equal(100.0, stats.GlobalBrightness, precision: 3);
        Assert.Equal(0.0, stats.BrightnessStdDev, precision: 3);
        Assert.Equal(0.0, stats.NoiseLevel, precision: 3);
        Assert.Equal(3.0, stats.AdaptiveThreshold, precision: 3);
    }

    /// <summary>
    /// Low contrast: block values 48, 52, 50, 49 (all within ±2 of mean ~49.75).
    /// BrightnessStdDev ≈ 1.48 (< 15) → threshold gets multiplied by 1.3.
    /// </summary>
    [Fact]
    public void LowContrast_BrightnessStdDevBelow15_ThresholdMultipliedBy1_3()
    {
        byte[] blockValues = [48, 52, 50, 49];
        byte[] imageData = CreateYImage(Width, Height, BlockSize, blockValues);

        var result = GetLightingResult(imageData, blockValues);
        var stats = result.LightingStats;

        Assert.NotNull(stats);
        // GlobalBrightness = (48+52+50+49)/4 = 49.75
        Assert.Equal(49.75, stats.GlobalBrightness, precision: 3);

        // Variance = ((48-49.75)^2 + (52-49.75)^2 + (50-49.75)^2 + (49-49.75)^2)/4
        // = (3.0625 + 5.0625 + 0.0625 + 0.5625)/4 = 8.75/4 = 2.1875
        // StdDev = sqrt(2.1875) ≈ 1.479
        Assert.True(stats.BrightnessStdDev < 15, "BrightnessStdDev should be below 15");
        Assert.Equal(1.479, stats.BrightnessStdDev, precision: 1);

        // Noise: diffH=|48-52|=4, diffV=|48-50|=2
        // NoiseLevel = (4+2)/2 = 3
        Assert.Equal(3.0, stats.NoiseLevel, precision: 3);

        // Threshold = 2.5*3=7.5, StdDev<15 → 7.5*1.3=9.75, clamp(9.75,3,250)=9.75
        Assert.Equal(9.75, stats.AdaptiveThreshold, precision: 3);
    }

    /// <summary>
    /// Normal contrast: blocks 80, 100, 110, 90 — StdDev ≈ 11.18 (< 15), NoiseLevel=25.
    /// </summary>
    [Fact]
    public void NormalContrast_RandomBlocks_ReasonableNoiseAndThreshold()
    {
        byte[] blockValues = [80, 100, 110, 90];
        byte[] imageData = CreateYImage(Width, Height, BlockSize, blockValues);

        var result = GetLightingResult(imageData, blockValues);
        var stats = result.LightingStats;

        Assert.NotNull(stats);
        // GlobalBrightness = 95
        Assert.Equal(95.0, stats.GlobalBrightness, precision: 3);

        // Variance = ((80-95)^2 + (100-95)^2 + (110-95)^2 + (90-95)^2)/4
        // = (225+25+225+25)/4 = 125
        // StdDev = sqrt(125) ≈ 11.18
        Assert.Equal(11.18, stats.BrightnessStdDev, precision: 1);

        // Noise: diffH=|80-100|=20, diffV=|80-110|=30
        // NoiseLevel = (20+30)/2 = 25
        Assert.Equal(25.0, stats.NoiseLevel, precision: 3);

        // Threshold = 2.5*25=62.5, StdDev<15 → 62.5*1.3=81.25, clamp→81.25
        Assert.Equal(81.25, stats.AdaptiveThreshold, precision: 3);
    }

    /// <summary>
    /// Checkerboard pattern: alternating 50 and 150 — high contrast, StdDev=50 (≥ 15).
    /// NoiseLevel=100, Threshold=250 (clamped at upper bound).
    /// </summary>
    [Fact]
    public void Checkerboard_Alternating50And150_HighNoise_ThresholdAtMax()
    {
        byte[] blockValues = [50, 150, 150, 50];
        byte[] imageData = CreateYImage(Width, Height, BlockSize, blockValues);

        var result = GetLightingResult(imageData, blockValues);
        var stats = result.LightingStats;

        Assert.NotNull(stats);
        // GlobalBrightness = 100
        Assert.Equal(100.0, stats.GlobalBrightness, precision: 3);

        // Variance = ((50-100)^2 + (150-100)^2 + (150-100)^2 + (50-100)^2)/4
        // = (2500+2500+2500+2500)/4 = 2500
        // StdDev = 50
        Assert.True(stats.BrightnessStdDev >= 15, "BrightnessStdDev should be ≥ 15, so no 1.3 multiplier");
        Assert.Equal(50.0, stats.BrightnessStdDev, precision: 3);

        // Noise: diffH=|50-150|=100, diffV=|50-150|=100
        // NoiseLevel = (100+100)/2 = 100
        Assert.Equal(100.0, stats.NoiseLevel, precision: 3);

        // Threshold = 2.5*100=250, StdDev≥15 → no multiplier, clamp(250,3,250)=250
        Assert.Equal(250.0, stats.AdaptiveThreshold, precision: 3);
    }
}
