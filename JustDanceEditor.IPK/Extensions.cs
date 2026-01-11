using System.Text;

namespace JustDanceEditor.IPK;

public static class Extensions
{
    // Extend the BinaryReader to read stuff in Big Endian
    public static long ReadInt64BigEndian(this BinaryReader reader)
    {
        byte[] data = reader.ReadBytes(8);
        Array.Reverse(data);
        return BitConverter.ToInt64(data, 0);
    }

    public static int ReadInt32BigEndian(this BinaryReader reader)
    {
        byte[] data = reader.ReadBytes(4);
        Array.Reverse(data);
        return BitConverter.ToInt32(data, 0);
    }

    public static string ReadNTString(this BinaryReader reader)
    {
        int size = reader.ReadInt32BigEndian();
        return new string(reader.ReadChars(size));
    }

    // Extend the BinaryWriter to write stuff in Big Endian
    public static void WriteInt64BigEndian(this BinaryWriter writer, long value)
    {
        byte[] data = BitConverter.GetBytes(value);
        Array.Reverse(data);
        writer.Write(data);
    }

    public static void WriteInt32BigEndian(this BinaryWriter writer, int value)
    {
        byte[] data = BitConverter.GetBytes(value);
        Array.Reverse(data);
        writer.Write(data);
    }

    public static void WriteNTString(this BinaryWriter writer, string value)
    {
        byte[] chars = Encoding.UTF8.GetBytes(value); // Use UTF8 bytes
        writer.WriteInt32BigEndian(chars.Length);
        writer.Write(chars);
    }
}