using CameraRecorder.MotionAnalyzers;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraRecorder.Testing;

public class GetBackgroundMapTests
{
    private const int BlockSize = 6;

    private static AdaptiveMotionDetector CreateDetector(int width = 12, int height = 12)
    {
        var settings = new MotionDetectorSettings
        {
            Width = width,
            Height = height,
            BlockSize = BlockSize,
            PixelFormat = PixelFormat.Y,
            FrameBufferSize = 30,
        };
        return new AdaptiveMotionDetector(settings, NullLogger<AdaptiveMotionDetector>.Instance);
    }

    /// <summary>
    /// Подаёт кадр с заданной яркостью на блок (2×2 блока = 4 значения).
    /// Изображение 12×12, Y-формат, BlockSize=6 → каждый блок 6×6=36 пикселей.
    /// </summary>
    private static void FeedFrames(AdaptiveMotionDetector detector, params byte[][] blockValuesPerFrame)
    {
        foreach (var blockValues in blockValuesPerFrame)
        {
            // Строим изображение: повторяем blockValues[i] для всех пикселей блока i
            var image = new byte[12 * 12];
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    byte val = blockValues[row * 2 + col];
                    int startY = row * BlockSize;
                    int startX = col * BlockSize;
                    for (int y = 0; y < BlockSize; y++)
                        for (int x = 0; x < BlockSize; x++)
                            image[(startY + y) * 12 + (startX + x)] = val;
                }
            }
            detector.DetectMotion(image, 0);
        }
    }

    [Fact]
    public void EmptyBuffer_ReturnsNull()
    {
        var detector = CreateDetector();
        Assert.Null(detector.GetBackgroundMap());
    }

    [Fact]
    public void ThreeIdenticalFrames_MedianEqualsValue()
    {
        var detector = CreateDetector();
        // 3 одинаковых кадра: каждый блок = 100
        FeedFrames(detector,
            [100, 100, 100, 100],
            [100, 100, 100, 100],
            [100, 100, 100, 100]);

        var bg = detector.GetBackgroundMap();
        Assert.NotNull(bg);
        Assert.Equal(4, bg!.Length);
        Assert.All(bg, v => Assert.Equal(100, v));
    }

    [Fact]
    public void FiveFrames_OneOutlier_MedianIgnoresOutlier()
    {
        var detector = CreateDetector();
        // 4 кадра = 50, 1 кадр = 200 → медиана = 50 (сортировка: 50,50,50,50,200, mid=50)
        FeedFrames(detector,
            [50, 50, 50, 50],
            [50, 50, 50, 50],
            [200, 200, 200, 200],  // выброс
            [50, 50, 50, 50],
            [50, 50, 50, 50]);

        var bg = detector.GetBackgroundMap();
        Assert.NotNull(bg);
        Assert.All(bg!, v => Assert.Equal(50, v));
    }

    [Fact]
    public void FourFrames_EvenCount_MedianIsAverageOfTwoMiddle()
    {
        var detector = CreateDetector();
        // 4 кадра: 10, 30, 50, 70 → sorted: 10,30,50,70 → median=(30+50)/2=40
        FeedFrames(detector,
            [10, 30, 50, 70],
            [30, 10, 70, 50],
            [50, 70, 10, 30],
            [70, 50, 30, 10]);

        var bg = detector.GetBackgroundMap();
        Assert.NotNull(bg);
        Assert.All(bg!, v => Assert.Equal(40, v));
    }

    [Fact]
    public void DifferentBlocks_HaveDifferentMedians()
    {
        var detector = CreateDetector();
        // Блок 0: всегда 100 → медиана 100
        // Блок 1: 60, 60, 60, 100, 100 → sorted: 60,60,60,100,100 → медиана 60
        // Блок 2: 100, 100, 100, 60, 60 → sorted: 60,60,100,100,100 → медиана 100
        // Блок 3: всегда 80 → медиана 80
        FeedFrames(detector,
            [100, 60, 100, 80],
            [100, 60, 100, 80],
            [100, 60, 100, 80],
            [100, 100, 60, 80],
            [100, 100, 60, 80]);

        var bg = detector.GetBackgroundMap();
        Assert.NotNull(bg);
        Assert.Equal(100, bg![0]);
        Assert.Equal(60,  bg![1]);
        Assert.Equal(100, bg![2]);
        Assert.Equal(80,  bg![3]);
    }

    [Fact]
    public void FullBuffer_30Frames_MedianCalculatedCorrectly()
    {
        var detector = CreateDetector();
        // 15 кадров = 0, 15 кадров = 200 → медиана = 0 (сортировка: 0×15, 200×15, mid=0)
        var frames = new byte[30][];
        for (int f = 0; f < 15; f++)
            frames[f] = [0, 0, 0, 0];
        for (int f = 15; f < 30; f++)
            frames[f] = [200, 200, 200, 200];

        FeedFrames(detector, frames);

        var bg = detector.GetBackgroundMap();
        Assert.NotNull(bg);
        // При 30 кадрах медиана — среднее temp[14] и temp[15]
        // temp[0..14] = 0, temp[15..29] = 200 → median = (0+200)/2 = 100
        Assert.All(bg!, v => Assert.Equal(100, v));
    }
}
