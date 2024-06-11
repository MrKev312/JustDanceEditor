using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
}
