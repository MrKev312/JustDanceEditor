using AssetsTools.NET;
using AssetsTools.NET.Extra;

using KevInc.Texture;
using KevInc.Texture.ImageSharp;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.Unity;

internal static class UnityTextureExtractor
{
    public static Image<Rgba32> ExtractTexture(AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo textureInfo, AssetTypeValueField? cachedBaseField = null)
    {
        AssetTypeValueField baseField = cachedBaseField ?? manager.GetBaseField(fileInst, textureInfo);
        UnityTextureData texture = UnityTextureData.FromBaseField(baseField);

        byte[] encodedData = texture.ImageData;
        if (!TryPopulateStreamData(ref encodedData, fileInst, texture.StreamInfo))
            throw new InvalidOperationException($"Failed to load external stream data for texture '{textureInfo.PathId}'.");

        if (encodedData.Length == 0)
            throw new InvalidOperationException($"Texture '{textureInfo.PathId}' does not contain image data.");

        Image<Rgba32> image = TextureImageSharpCodec.Decode(encodedData, texture.Format, texture.Width, texture.Height);
        image.Mutate(x => x.Flip(FlipMode.Vertical));
        return image;
    }

    private static bool TryPopulateStreamData(ref byte[] imageData, AssetsFileInstance fileInst, StreamingInfoData streamInfo)
    {
        if (streamInfo.Size == 0 || string.IsNullOrEmpty(streamInfo.Path))
            return imageData.Length > 0;

        if (fileInst.parentBundle != null)
            return TryLoadFromBundle(ref imageData, fileInst, streamInfo);

        return TryLoadFromExternalFile(ref imageData, fileInst, streamInfo);
    }

    private static bool TryLoadFromBundle(ref byte[] imageData, AssetsFileInstance fileInst, StreamingInfoData streamInfo)
    {
        string searchPath = streamInfo.Path ?? string.Empty;
        if (searchPath.StartsWith("archive:/", StringComparison.OrdinalIgnoreCase))
            searchPath = searchPath[9..];
        searchPath = Path.GetFileName(searchPath) ?? string.Empty;

        AssetBundleFile bundle = fileInst.parentBundle.file;
        AssetsFileReader reader = bundle.DataReader;

        foreach (AssetBundleDirectoryInfo entry in bundle.BlockAndDirInfo.DirectoryInfos)
        {
            if (!string.Equals(entry.Name, searchPath, StringComparison.OrdinalIgnoreCase))
                continue;

            reader.Position = entry.Offset + streamInfo.Offset;
            int length = checked((int)streamInfo.Size);
            imageData = reader.ReadBytes(length);
            return true;
        }

        return false;
    }

    private static bool TryLoadFromExternalFile(ref byte[] imageData, AssetsFileInstance fileInst, StreamingInfoData streamInfo)
    {
        string resolvedPath = streamInfo.Path ?? string.Empty;
        if (resolvedPath.StartsWith("archive:/", StringComparison.OrdinalIgnoreCase))
            resolvedPath = Path.GetFileName(resolvedPath) ?? string.Empty;

        string? rootPath = Path.GetDirectoryName(fileInst.path);
        if (!Path.IsPathRooted(resolvedPath) && !string.IsNullOrEmpty(rootPath))
            resolvedPath = Path.Combine(rootPath, resolvedPath);

        if (!File.Exists(resolvedPath))
            return false;

        using FileStream stream = File.OpenRead(resolvedPath);
        stream.Position = streamInfo.Offset;
        int length = checked((int)streamInfo.Size);
        imageData = new byte[length];
        _ = stream.Read(imageData, 0, length);
        return true;
    }

    private readonly record struct UnityTextureData(int Width, int Height, TextureFormat Format, byte[] ImageData, StreamingInfoData StreamInfo)
    {
        public static UnityTextureData FromBaseField(AssetTypeValueField baseField)
        {
            int width = baseField["m_Width"].AsInt;
            int height = baseField["m_Height"].AsInt;
            TextureFormat format = (TextureFormat)baseField["m_TextureFormat"].AsInt;
            byte[] imageData = baseField["image data"].AsByteArray ?? [];

            AssetTypeValueField streamField = baseField["m_StreamData"];
            StreamingInfoData streamInfo = new(
                streamField["offset"].AsLong,
                streamField["size"].AsUInt,
                streamField["path"].AsString ?? string.Empty);

            return new UnityTextureData(width, height, format, imageData, streamInfo);
        }
    }

    private readonly record struct StreamingInfoData(long Offset, uint Size, string Path);
}
