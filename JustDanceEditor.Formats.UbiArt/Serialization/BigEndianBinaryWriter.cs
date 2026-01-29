using System.Buffers.Binary;
using System.Text;
using System.IO.Hashing;

namespace JustDanceEditor.Formats.UbiArt.Serialization;

public class BigEndianBinaryWriter(Stream output) : BinaryWriter(output, Encoding.UTF8, true)
{
    public override void Write(int value) => base.Write(BinaryPrimitives.ReverseEndianness(value));
    public override void Write(uint value) => base.Write(BinaryPrimitives.ReverseEndianness(value));
    public override void Write(short value) => base.Write(BinaryPrimitives.ReverseEndianness(value));
    public override void Write(ushort value) => base.Write(BinaryPrimitives.ReverseEndianness(value));
    public override void Write(long value) => base.Write(BinaryPrimitives.ReverseEndianness(value));
    public override void Write(ulong value) => base.Write(BinaryPrimitives.ReverseEndianness(value));

    public override void Write(float value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }

    public override void Write(double value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Array.Reverse(bytes);
        base.Write(bytes);
    }

    /// <summary>
    /// Writes a length-prefixed string (Int32 Big-Endian length + UTF8 bytes).
    /// </summary>
    public void WriteUbiArtString(string? value)
    {
        if (value == null)
        {
            Write(0);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Write(bytes.Length);
        base.Write(bytes);
    }

    /// <summary>
    /// Writes a UbiArt Path structure: [Len][File][Len][Folder][CRC32(File, LittleEndian)]
    /// </summary>
    public void WriteUbiArtPath(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            Write(0); // File Len
            Write(0); // Folder Len
            Write(0xFFFFFFFF); // CRC (Null)
            return;
        }

        string normalized = fullPath.Replace('\\', '/');
        string filename = Path.GetFileName(normalized);
        string folder = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";
        if (!string.IsNullOrEmpty(folder) && !folder.EndsWith('/'))
            folder += "/";

        // Write Filename
        WriteUbiArtString(filename);

        // Write Folder
        WriteUbiArtString(folder);

        // Write CRC32 of Filename (Little Endian)
        byte[] fileBytes = Encoding.UTF8.GetBytes(filename);
        uint crc = Crc32.HashToUInt32(fileBytes);
        base.Write(crc); // base.Write writes Little Endian for primitives on most systems
    }
}