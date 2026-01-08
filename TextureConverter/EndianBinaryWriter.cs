using System.Text;

namespace TextureConverter;

public class EndianBinaryWriter : BinaryWriter
{
    public bool IsBigEndian;

    public EndianBinaryWriter(Stream input, bool isBigEndian = false)
        : base(input)
    {
        IsBigEndian = isBigEndian;
    }

    public EndianBinaryWriter(Stream input, Encoding encoding, bool isBigEndian = false)
        : base(input, encoding)
    {
        IsBigEndian = isBigEndian;
    }

    public EndianBinaryWriter(Stream input, Encoding encoding, bool leaveOpen, bool isBigEndian = false)
        : base(input, encoding, leaveOpen)
    {
        IsBigEndian = isBigEndian;
    }

    public override void Write(short value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(int value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(long value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(ushort value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(uint value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(ulong value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(float value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }

    public override void Write(double value)
    {
        byte[] data = BitConverter.GetBytes(value);
        if (IsBigEndian)
        {
            Array.Reverse(data);
        }

        base.Write(data);
    }
}