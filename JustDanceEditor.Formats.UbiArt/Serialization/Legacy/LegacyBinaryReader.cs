using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal interface ILegacyBinaryReader
{
    byte[] Bytes { get; }
    int Offset { get; set; }
    int Remaining { get; }

    byte ReadByte();
    short ReadInt16();
    ushort ReadUInt16();
    int ReadInt32();
    uint ReadUInt32();
    long ReadInt64();
    ulong ReadUInt64();
    float ReadSingle();
    double ReadDouble();
    string ReadString();
    byte[] ReadBytes(int length);
    void Skip(int count);
}

internal sealed class LegacyBinaryBufferReader(byte[] bytes, int offset = 0) : ILegacyBinaryReader
{
    public byte[] Bytes { get; } = bytes;
    public int Offset { get; set; } = offset;
    public int Remaining => Bytes.Length - Offset;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return Bytes[Offset++];
    }

    public short ReadInt16()
    {
        EnsureAvailable(2);
        short value = BinaryPrimitives.ReadInt16BigEndian(Bytes.AsSpan(Offset, 2));
        Offset += 2;
        return value;
    }

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(Bytes.AsSpan(Offset, 2));
        Offset += 2;
        return value;
    }

    public int ReadInt32()
    {
        EnsureAvailable(4);
        int value = BinaryPrimitives.ReadInt32BigEndian(Bytes.AsSpan(Offset, 4));
        Offset += 4;
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(Bytes.AsSpan(Offset, 4));
        Offset += 4;
        return value;
    }

    public long ReadInt64()
    {
        EnsureAvailable(8);
        long value = BinaryPrimitives.ReadInt64BigEndian(Bytes.AsSpan(Offset, 8));
        Offset += 8;
        return value;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(8);
        ulong value = BinaryPrimitives.ReadUInt64BigEndian(Bytes.AsSpan(Offset, 8));
        Offset += 8;
        return value;
    }

    public float ReadSingle()
    {
        uint raw = ReadUInt32();
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    public double ReadDouble()
    {
        ulong raw = ReadUInt64();
        return BitConverter.Int64BitsToDouble(unchecked((long)raw));
    }

    public string ReadString()
    {
        int length = ReadInt32();
        if (length == -1)
            return string.Empty;

        if (length < 0 || length > 4096 || Offset + length > Bytes.Length)
            throw new InvalidDataException($"Invalid legacy string length {length} at 0x{Offset - 4:X}.");

        string value = Encoding.UTF8.GetString(Bytes, Offset, length).TrimEnd('\0');
        Offset += length;
        return value;
    }

    public byte[] ReadBytes(int length)
    {
        if (length < 0 || Offset + length > Bytes.Length)
            throw new InvalidDataException($"Cannot read {length} byte(s) at 0x{Offset:X}.");

        byte[] value = Bytes.AsSpan(Offset, length).ToArray();
        Offset += length;
        return value;
    }

    public void Skip(int count)
    {
        if (count < 0 || Offset + count > Bytes.Length)
            throw new InvalidDataException($"Cannot skip {count} byte(s) at 0x{Offset:X}.");

        Offset += count;
    }

    private void EnsureAvailable(int count)
    {
        if (Offset < 0 || Offset + count > Bytes.Length)
            throw new InvalidDataException($"Cannot read {count} byte(s) at 0x{Offset:X}.");
    }
}