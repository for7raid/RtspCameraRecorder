namespace CameraRecorder.Sinks;

public interface IStorageSink
{
    /// <summary>
    /// Имя sink для логирования
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Сохранить файл.
    /// </summary>
    /// <param name="fileName">Имя файла (без пути)</param>
    /// <param name="data">Данные файла</param>
    /// <param name="ct">Токен отмены</param>
    void SaveAsync(string fileName, byte[] data, CancellationToken ct);
}
