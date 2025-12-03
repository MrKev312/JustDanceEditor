using AssetsTools.NET;

using JustDanceEditor.Formats.JDI.Utilities;

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

        if (File.Exists(outputPath))
            throw new IOException($"The file '{outputPath}' already exists.");

        Directory.CreateDirectory(outputPath);
        string uncompressedPath = Path.Combine(outputPath, "temp.mod.uncompressed");
        string compressedPath = Path.Combine(outputPath, "temp.mod");

        using (AssetsFileWriter assetWriter = new(uncompressedPath))
            assetBundleFile.Write(assetWriter);

        AssetBundleFile newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);

        newUncompressedBundle.Close();
        File.Delete(uncompressedPath);

        string hash = FileHashing.GetFileMD5(compressedPath);
        string newPath = Path.Combine(outputPath, hash);
        if (keepExtension)
            newPath += ".bundle";

        if (File.Exists(newPath))
            File.Delete(newPath);

        File.Move(compressedPath, newPath);
        UpdateCabHash(newPath, hash);
    }

    private static void UpdateCabHash(string bundlePath, string hash)
    {
        using BinaryReader reader = new(File.OpenRead(bundlePath));
        const string marker = "CAB-";
        byte[] markerBytes = Encoding.UTF8.GetBytes(marker);
        long startPosition = Math.Max(0, reader.BaseStream.Length - 0x40);
        reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            byte[] buffer = reader.ReadBytes(markerBytes.Length);
            if (buffer.SequenceEqual(markerBytes))
            {
                long offset = reader.BaseStream.Position - markerBytes.Length + 4;
                reader.Close();

                using BinaryWriter writer = new(File.OpenWrite(bundlePath));
                writer.BaseStream.Seek(offset, SeekOrigin.Begin);
                writer.Write(Encoding.UTF8.GetBytes(hash));
                return;
            }

            reader.BaseStream.Seek(-markerBytes.Length + 1, SeekOrigin.Current);
        }

        throw new InvalidOperationException("Marker 'CAB-' not found in the bundle.");
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
