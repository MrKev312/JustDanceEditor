using System.IO.Compression;

namespace JustDanceEditor.IPK;

public static class Decompressor
{
    public static byte[] Decompress(byte[] data)
    {
        using MemoryStream compressedStream = new(data);

        // If we start with 0x78, it's a zlib compressed file and thus skip the first two bytes
        if (compressedStream.ReadByte() == 0x78)
            compressedStream.ReadByte();
        else
            compressedStream.Seek(0, SeekOrigin.Begin);

        using DeflateStream deflateStream = new(compressedStream, CompressionMode.Decompress);
        using MemoryStream decompressedStream = new();
        deflateStream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }
}
