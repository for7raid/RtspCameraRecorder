namespace CameraRecorder;

/// <summary>
/// Декодированный кадр
/// </summary>
public class DecodedVideoFrame : IDisposable
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
    }
}
