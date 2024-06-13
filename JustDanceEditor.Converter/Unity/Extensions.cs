using AssetsTools.NET;

using JustDanceEditor.Converter.Helpers;

using NAudio.Codecs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JustDanceEditor.Converter.Unity;

internal static class Extensions
{
    /// <summary>
    /// Convert a <see cref="Guid"/> to a Unity <see cref="uint"/>[] GUID
    /// </summary>
    /// <param name="guid">The System.Guid to convert</param>
    /// <returns>The Unity <see cref="uint"/>[] GUID</returns>
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

    /// <summary>
    /// Save and compress an <see cref="AssetBundleFile"/> to a file
    /// </summary>
    /// <param name="assetBundleFile">The <see cref="AssetBundleFile"/> to save</param>
    /// <param name="outputPath">The path to save the file to</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="outputPath"/> is null or whitespace</exception>
    /// <exception cref="IOException">Thrown if the file already exists</exception>
    public static void SaveAndCompress(this AssetBundleFile assetBundleFile, string outputPath)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(outputPath);

        // Throw if the file already exists
        if (File.Exists(outputPath))
            throw new IOException($"The file '{outputPath}' already exists.");

        Directory.CreateDirectory(outputPath);
        string uncompressedPath = Path.Combine(outputPath, "temp.mod.uncompressed");
        string compressedPath = Path.Combine(outputPath, "temp.mod");

        using (AssetsFileWriter writer = new(uncompressedPath))
            assetBundleFile.Write(writer);

        AssetBundleFile newUncompressedBundle = new();
        newUncompressedBundle.Read(new AssetsFileReader(File.OpenRead(uncompressedPath)));

        using (AssetsFileWriter compressedWriter = new(compressedPath))
            newUncompressedBundle.Pack(compressedWriter, AssetBundleCompressionType.LZ4);

        newUncompressedBundle.Close();

        // Delete the uncompressed file
        File.Delete(uncompressedPath);

        // Rename the compressed file to it's md5 hash
        string hash = Download.GetFileMD5(compressedPath);
        string newPath = Path.Combine(outputPath, $"{hash}");

        // If the file already exists, delete it
        if (File.Exists(newPath))
            File.Delete(newPath);

        File.Move(compressedPath, newPath);
    }

    /// <summary>
    /// Get a random ID that is not already used in the <see cref="AssetsFile"/>
    /// </summary>
    /// <param name="afile">The <see cref="AssetsFile"/> to get the ID from</param>
    /// <returns>A random ID that is not already used in the <see cref="AssetsFile"/></returns>S
    public static long GetRandomId(this AssetsFile afile)
    {
        Random Rand = Random.Shared;

        long id = Rand.NextInt64(long.MinValue, long.MaxValue);

        while (afile.Metadata.GetAssetInfo(id) is not null)
            id = Rand.NextInt64(long.MinValue, long.MaxValue);

        return id;
    }
}
