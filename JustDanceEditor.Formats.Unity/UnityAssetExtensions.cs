using AssetsTools.NET;

using System.Security.Cryptography;
using System.Text;

namespace JustDanceEditor.Formats.Unity;

public static class UnityAssetExtensions
{
    public static uint[] ToUnity(this Guid guid)
    {
        byte[] guidBytes = guid.ToByteArray();
        uint[] uintArray = new uint[4];

        for (int j = 0; j < guidBytes.Length; j++)
            guidBytes[j] = (byte)(((guidBytes[j] & 0xF0) >> 4) | ((guidBytes[j] & 0x0F) << 4));

        for (int j = 0; j < 4; j++)
            uintArray[j] = BitConverter.ToUInt32(guidBytes, j * 4);

        return uintArray;
    }

    public static void SaveAndCompress(this AssetBundleFile assetBundleFile, string outputPath, bool keepExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // 1. Write Uncompressed to Memory
        // We use the wrapper here so AssetsFileWriter doesn't kill our stream
        using MemoryStream uncompressedMs = new();
        using (AssetsFileWriter writer = new(new NonClosingStreamWrapper(uncompressedMs)))
        {
            assetBundleFile.Write(writer);
        }

        // Reset position to read it back
        uncompressedMs.Position = 0;

        // 2. Load into new instance
        AssetBundleFile newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(uncompressedMs));

        // 3. Pack (Compress) to Memory
        // Here we don't need the wrapper because MemoryStream.ToArray() works even if closed
        using MemoryStream compressedMs = new();
        using (AssetsFileWriter compressedWriter = new(compressedMs))
        {
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);
        }

        // 4. Get raw bytes (works on closed MemoryStream)
        byte[] compressedData = compressedMs.ToArray();

        // 5. Calculate Hash & Patch CAB (In-Memory)
        string hash = ComputeMd5Hash(compressedData);
        UpdateCabHashInMemory(compressedData, hash);

        // 6. Write Final File
        Directory.CreateDirectory(outputPath);

        string fileName = hash;
        if (keepExtension)
            fileName += ".bundle";

        string finalPath = Path.Combine(outputPath, fileName);

        if (File.Exists(finalPath))
            File.Delete(finalPath);

        File.WriteAllBytes(finalPath, compressedData);

        // Explicit cleanup of the reader's stream if needed, though GC handles it.
        newUncompressedBundle.Close();
    }

    private static string ComputeMd5Hash(byte[] data)
    {
        byte[] hashBytes = MD5.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void UpdateCabHashInMemory(byte[] data, string hash)
    {
        // "CAB-" in UTF8 bytes
        ReadOnlySpan<byte> marker = "CAB-"u8;
        byte[] hashBytes = Encoding.UTF8.GetBytes(hash);

        // We search the last 1KB (or less)
        // Original logic: reader.BaseStream.Length - 0x40
        int startSearch = Math.Max(0, data.Length - 0x400);

        Span<byte> searchArea = data.AsSpan(startSearch);
        int index = searchArea.IndexOf(marker);

        if (index != -1)
        {
            // Absolute position of the start of "CAB-"
            int absoluteIndex = startSearch + index;

            // We want to write immediately after "CAB-"
            int writePos = absoluteIndex + marker.Length;

            // Safety check
            if (writePos + hashBytes.Length <= data.Length)
            {
                hashBytes.CopyTo(data.AsSpan(writePos));
                return;
            }
        }

        throw new InvalidOperationException("Marker 'CAB-' not found in the bundle.");
    }

    private class NonClosingStreamWrapper(Stream baseStream) : Stream
    {

        // Ignore disposal
        protected override void Dispose(bool disposing) { }
        public override void Close() { }

        // Forwarding logic
        public override bool CanRead => baseStream.CanRead;
        public override bool CanSeek => baseStream.CanSeek;
        public override bool CanWrite => baseStream.CanWrite;
        public override long Length => baseStream.Length;
        public override long Position { get => baseStream.Position; set => baseStream.Position = value; }
        public override void Flush() => baseStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => baseStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => baseStream.Seek(offset, origin);
        public override void SetLength(long value) => baseStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => baseStream.Write(buffer, offset, count);
    }

    public static long GetRandomId(this AssetsFile afile)
    {
        Random rand = Random.Shared;
        long id = rand.NextInt64(long.MinValue, long.MaxValue);

        while (afile.Metadata.GetAssetInfo(id) is not null)
            id = rand.NextInt64(long.MinValue, long.MaxValue);

        return id;
    }
}