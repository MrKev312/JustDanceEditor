using System;
using System.IO;
using System.Text;

namespace TextureConverter;

public class EndianBinaryReader : BinaryReader
{
    public bool IsBigEndian;

    public EndianBinaryReader(Stream input, bool isBigEndian = false)
        : base(input)
    {
        IsBigEndian = isBigEndian;
    }

    public EndianBinaryReader(Stream input, Encoding encoding, bool isBigEndian = false)
        : base(input, encoding)
    {
        IsBigEndian = isBigEndian;
    }

    public EndianBinaryReader(Stream input, Encoding encoding, bool leaveOpen, bool isBigEndian = false)
        : base(input, encoding, leaveOpen)
    {
        IsBigEndian = isBigEndian;
    }

    public override short ReadInt16()
    {
        byte[] data = base.ReadBytes(2);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToInt16(data, 0);
    }

    public override int ReadInt32()
    {
        byte[] data = base.ReadBytes(4);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToInt32(data, 0);
    }

    public override long ReadInt64()
    {
        byte[] data = base.ReadBytes(8);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToInt64(data, 0);
    }

    public override ushort ReadUInt16()
    {
        byte[] data = base.ReadBytes(2);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToUInt16(data, 0);
    }

    public override uint ReadUInt32()
    {
        byte[] data = base.ReadBytes(4);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToUInt32(data, 0);
    }

    public override ulong ReadUInt64()
    {
        byte[] data = base.ReadBytes(8);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToUInt64(data, 0);
    }

    public override float ReadSingle()
    {
        byte[] data = base.ReadBytes(4);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToSingle(data, 0);
    }

    public override double ReadDouble()
    {
        byte[] data = base.ReadBytes(8);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        return BitConverter.ToDouble(data, 0);
    }
}
