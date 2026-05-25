namespace CameraRecorder.Utils;

public class BitReader
{
    private readonly byte[] _data;
    private int _bitIndex = 0;
    private int _byteIndex = 0;

    public BitReader(byte[] data) => _data = data;

    public bool HasMoreData => _byteIndex < _data.Length;

    public int ReadBit()
    {
        if (_byteIndex >= _data.Length) return 0;
        int bit = (_data[_byteIndex] >> (7 - _bitIndex)) & 1;
        if (++_bitIndex == 8) { _bitIndex = 0; _byteIndex++; }
        return bit;
    }

    public uint ReadBits(int count)
    {
        uint value = 0;
        for (int i = 0; i < count; i++)
            value = (value << 1) | (uint)ReadBit();
        return value;
    }

    public uint ReadExpGolomb()
    {
        int leadingZeros = 0;

        // Считаем leading zeros с защитой от EOF
        while (HasMoreData && ReadBit() == 0)
        {
            leadingZeros++;
            if (leadingZeros > 31)      // guard: в H.265 значения > 2³¹ не встречаются
                return 0;
        }

        // Если вышли по EOF — невалидные данные
        if (!HasMoreData && leadingZeros == 0)
            return 0;

        return (1u << leadingZeros) - 1 + ReadBits(leadingZeros);
    }

    public int PeekBits(int count)
    {
        // Сохраняем позицию, читаем, восстанавливаем
        int savedBitIndex = _bitIndex;
        int savedByteIndex = _byteIndex;
        int result = (int)ReadBits(count);
        _bitIndex = savedBitIndex;
        _byteIndex = savedByteIndex;
        return result;
    }
}
