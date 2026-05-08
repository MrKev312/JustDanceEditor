namespace CrnLib;

internal sealed class BitWriter
{
    private readonly List<byte> _bytes = [];
    private byte _currentByte;
    private int _bitsInCurrentByte;

    public void WriteBits(uint bits, int bitCount)
    {
        if (bitCount is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        for (int bit = bitCount - 1; bit >= 0; bit--)
        {
            _currentByte = (byte)(((uint)_currentByte << 1) | ((bits >> bit) & 1U));
            _bitsInCurrentByte++;

            if (_bitsInCurrentByte == 8)
            {
                _bytes.Add(_currentByte);
                _currentByte = 0;
                _bitsInCurrentByte = 0;
            }
        }
    }

    public byte[] Finish()
    {
        WriteBits(0, 7);
        return [.. _bytes];
    }
}

internal sealed class BitReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private int _bitOffset;

    public BitReader(ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
            throw new InvalidDataException("Compressed CRN bitstream is empty.");

        _data = data;
    }

    public uint ReadBits(int bitCount)
    {
        if (bitCount is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        uint value = 0;
        for (int i = 0; i < bitCount; i++)
            value = (value << 1) | ReadBit();

        return value;
    }

    private uint ReadBit()
    {
        int byteOffset = _bitOffset >> 3;
        int bitInByte = 7 - (_bitOffset & 7);
        _bitOffset++;

        if (byteOffset >= _data.Length)
            return 0;

        return (uint)((_data.Span[byteOffset] >> bitInByte) & 1);
    }
}
