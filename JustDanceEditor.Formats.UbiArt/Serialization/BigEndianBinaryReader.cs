using System.Buffers.Binary;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

public class BigEndianBinaryReader(Stream input) : BinaryReader(input, Encoding.UTF8, true)
{
    public override int ReadInt32() => BinaryPrimitives.ReverseEndianness(base.ReadInt32());
    public override uint ReadUInt32() => BinaryPrimitives.ReverseEndianness(base.ReadUInt32());
    public override short ReadInt16() => BinaryPrimitives.ReverseEndianness(base.ReadInt16());
    public override ushort ReadUInt16() => BinaryPrimitives.ReverseEndianness(base.ReadUInt16());
    public override long ReadInt64() => BinaryPrimitives.ReverseEndianness(base.ReadInt64());
    public override ulong ReadUInt64() => BinaryPrimitives.ReverseEndianness(base.ReadUInt64());

    public override float ReadSingle()
    {
        byte[] bytes = base.ReadBytes(4);
        Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    public override double ReadDouble()
    {
        byte[] bytes = base.ReadBytes(8);
        Array.Reverse(bytes);
        return BitConverter.ToDouble(bytes, 0);
    }

    public string ReadUbiArtString()
    {
        int length = ReadInt32();
        if (length <= 0)
            return string.Empty;
        byte[] bytes = base.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }
}