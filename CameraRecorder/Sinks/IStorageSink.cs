namespace CameraRecorder.Sinks;

public interface IStorageSink
{
    /// <summary>
    /// Имя sink для логирования
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Сохранить файл. Позиция stream будет установлена в 0 перед вызовом.
    /// </summary>
    /// <param name="fileName">Имя файла (без пути)</param>
    /// <param name="stream">Поток с данными</param>
    /// <param name="ct">Токен отмены</param>
    Task SaveAsync(string fileName, Stream stream, CancellationToken ct);
}
