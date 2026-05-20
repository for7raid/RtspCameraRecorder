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
    void SaveAsync(string fileName, byte[] data);

    /// <summary>
    /// Сохранить файл.
    /// </summary>
    /// <param name="fileName">Имя файла (без пути)</param>
    /// <param name="tmpDataFilePath">Временный файл с содержимым</param>
    void SaveAsync(string fileName, string tmpDataFilePath);
}
