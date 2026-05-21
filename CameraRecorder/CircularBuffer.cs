namespace CameraRecorder;

public class CircularBuffer<T>
{
    public T[] Buffer { get; init; }
    private int tail = 0;
    private int length = 0;  // Текущее количество элементов

    public CircularBuffer(int capacity)
    {
        Buffer = new T[capacity];
    }

    public void Add(T item)
    {
        Buffer[tail] = item;
        tail = (tail + 1) % Buffer.Length;

        if (length < Buffer.Length)
            length++;
    }
    public void Clear()
    {
        // Очищаем ссылки на элементы (важно для сборщика мусора, если T - ссылочный тип)
        Array.Clear(Buffer, 0, Buffer.Length);

        // Сбрасываем указатели и счетчик
        tail = 0;
        length = 0;
    }

    public T GetAt(int index)
    {
        if (index < 0 || index >= length)
            throw new IndexOutOfRangeException($"Index {index} is out of range. Valid range: 0 to {length - 1}");

        // Вычисляем реальную позицию в буфере с учетом циклического хвоста
        int position = (tail - length + index) % Buffer.Length;

        // Обработка отрицательного остатка в C#
        if (position < 0)
            position += Buffer.Length;

        return Buffer[position];
    }

    public int Length => length;

    /// <summary>
    /// Возвращает элементы в порядке добавления (FIFO).
    /// </summary>
    public IEnumerable<T> Ordered
    {
        get
        {
            for (int i = 0; i < length; i++)
                yield return Buffer[(tail - length + i + Buffer.Length) % Buffer.Length];
        }
    }

    public int Capacity => Buffer.Length;  // Вместимость буфера

    public T this[int index] => GetAt(index);
}
