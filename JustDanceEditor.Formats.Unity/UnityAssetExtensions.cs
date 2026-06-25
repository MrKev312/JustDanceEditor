using AssetsTools.NET;

using System.Security.Cryptography;
using System.Text;

namespace JustDanceEditor.Formats.Unity;

public static class UnityAssetExtensions
{
    private const string BlankCabName = "CAB-00000000000000000000000000000000";

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

        byte[] compressedData = assetBundleFile.ToCompressedBundleData();
        string hash = ComputeMd5Hash(compressedData);
        byte[] finalData = [.. compressedData];
        if (!TryUpdateCabHashInMemory(finalData, hash))
        {
            if (assetBundleFile.BlockAndDirInfo.DirectoryInfos.Count == 0)
                throw new InvalidOperationException("Marker 'CAB-' not found in the bundle.");

            AssetBundleDirectoryInfo directoryInfo = assetBundleFile.BlockAndDirInfo.DirectoryInfos[0];
            if (!directoryInfo.Name.StartsWith("CAB-", StringComparison.Ordinal))
                throw new InvalidOperationException("Marker 'CAB-' not found in the bundle.");

            directoryInfo.Name = $"CAB-{hash}";
            finalData = assetBundleFile.ToCompressedBundleData();
        }

        WriteCompressedBundleData(finalData, outputPath, keepExtension, hash);
    }

    public static byte[] ToCompressedBundleData(this AssetBundleFile assetBundleFile)
    {
        ArgumentNullException.ThrowIfNull(assetBundleFile);

        EnsureUniqueCabNames(assetBundleFile);

        using MemoryStream uncompressedMs = new();
        using (AssetsFileWriter writer = new(WrapNonClosing(uncompressedMs)))
        {
            assetBundleFile.Write(writer);
        }

        uncompressedMs.Position = 0;

        AssetBundleFile newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(uncompressedMs));

        try
        {
            using MemoryStream compressedMs = new();
            using (AssetsFileWriter compressedWriter = new(compressedMs))
            {
                newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);
            }

            return compressedMs.ToArray();
        }
        finally
        {
            newUncompressedBundle.Close();
        }
    }

    internal static void EnsureUniqueCabNames(AssetBundleFile assetBundleFile)
    {
        ArgumentNullException.ThrowIfNull(assetBundleFile);

        foreach (AssetBundleDirectoryInfo directoryInfo in assetBundleFile.BlockAndDirInfo.DirectoryInfos)
        {
            if (string.Equals(directoryInfo.Name, BlankCabName, StringComparison.Ordinal))
                directoryInfo.Name = $"CAB-{Guid.NewGuid():N}";
        }
    }

    public static string WriteCompressedBundle(byte[] compressedData, string outputPath, bool keepExtension)
    {
        ArgumentNullException.ThrowIfNull(compressedData);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        byte[] finalData = [.. compressedData];
        string hash = ComputeMd5Hash(finalData);
        TryUpdateCabHashInMemory(finalData, hash);

        return WriteCompressedBundleData(finalData, outputPath, keepExtension, hash);
    }

    private static string WriteCompressedBundleData(byte[] finalData, string outputPath, bool keepExtension, string hash)
    {
        Directory.CreateDirectory(outputPath);

        string fileName = hash;
        if (keepExtension)
            fileName += ".bundle";

        string finalPath = Path.Combine(outputPath, fileName);

        if (File.Exists(finalPath))
            File.Delete(finalPath);

        File.WriteAllBytes(finalPath, finalData);
        return finalPath;
    }

    internal static Stream WrapNonClosing(Stream stream) => new NonClosingStreamWrapper(stream);

    private static string ComputeMd5Hash(byte[] data)
    {
        byte[] hashBytes = MD5.HashData(data);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool TryUpdateCabHashInMemory(byte[] data, string hash)
    {
        // "CAB-" in UTF8 bytes
        ReadOnlySpan<byte> marker = "CAB-"u8;
        byte[] hashBytes = Encoding.UTF8.GetBytes(hash);

        Span<byte> searchArea = data.AsSpan();
        int index = searchArea.LastIndexOf(marker);

        if (index != -1)
        {
            // Absolute position of the start of "CAB-"
            int writePos = index + marker.Length;

            // Safety check
            if (writePos + hashBytes.Length <= data.Length)
            {
                hashBytes.CopyTo(data.AsSpan(writePos));
                return true;
            }
        }

        return false;
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