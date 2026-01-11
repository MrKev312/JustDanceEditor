using System.IO.Compression;

namespace JustDanceEditor.IPK;

public static class Compressor
{
    public static byte[] Compress(byte[] data)
    {
        using MemoryStream outputStream = new();

        // Write Zlib Header (RFC 1950)
        // CMF = 0x78 (Deflate, 32k window)
        // FLG = 0x9C (Default compression level) or 0xDA (Best)
        outputStream.WriteByte(0x78);
        outputStream.WriteByte(0x9C);

        // Write Deflate Data
        using (DeflateStream deflateStream = new(outputStream, CompressionMode.Compress, true))
        {
            deflateStream.Write(data, 0, data.Length);
        }

        // Write Adler32 Checksum (Big Endian)
        uint adler = Adler32(data);
        byte[] adlerBytes = BitConverter.GetBytes(adler);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(adlerBytes);

        outputStream.Write(adlerBytes, 0, adlerBytes.Length);

        return outputStream.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1;
        uint b = 0;
        const uint modulus = 65521;

        foreach (byte t in data)
        {
            a = (a + t) % modulus;
            b = (b + a) % modulus;
        }

        return (b << 16) | a;
    }
}