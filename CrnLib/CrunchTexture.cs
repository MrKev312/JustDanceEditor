using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CrnLib;

public static class CrunchTexture
{
    public static bool Validate(ReadOnlySpan<byte> crnData)
    {
        return CrnFile.Validate(crnData);
    }

    public static CrnFileInfo GetFileInfo(ReadOnlySpan<byte> crnData)
    {
        return CrnFile.GetFileInfo(crnData);
    }

    public static CrnTextureInfo GetTextureInfo(ReadOnlySpan<byte> crnData)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        return new CrnTextureInfo(
            header.Width,
            header.Height,
            header.Levels,
            header.Faces,
            header.Format,
            header.BytesPerBlock,
            header.UserData0,
            header.UserData1);
    }

    public static CrnLevelInfo GetLevelInfo(ReadOnlySpan<byte> crnData, int level = 0)
    {
        return CrnFile.GetLevelInfo(crnData, level);
    }

    public static CrnLevelDataRange GetLevelDataRange(ReadOnlySpan<byte> crnData, int level = 0)
    {
        return CrnFile.GetLevelDataRange(crnData, level);
    }

    public static byte[] GetLevelData(ReadOnlySpan<byte> crnData, int level = 0)
    {
        return CrnFile.GetLevelData(crnData, level);
    }

    public static int GetSegmentedFileSize(ReadOnlySpan<byte> crnData)
    {
        return CrnFile.GetSegmentedFileSize(crnData);
    }

    public static byte[] CreateSegmentedFile(ReadOnlySpan<byte> crnData)
    {
        return CrnFile.CreateSegmentedFile(crnData);
    }

    public static byte[] DecodeUnityCrunch(ReadOnlySpan<byte> crnData, int level = 0)
    {
        return CrnDecoder.DecodeLevel(crnData, level);
    }

    public static byte[] DecodeUnityCrunchSegmented(ReadOnlySpan<byte> baseCrnData, ReadOnlySpan<byte> levelData, int level = 0)
    {
        return CrnDecoder.DecodeLevelSegmented(baseCrnData, levelData, level);
    }

    public static byte[][] DecodeUnityCrunchSegmentedFaces(ReadOnlySpan<byte> baseCrnData, ReadOnlySpan<byte> levelData, int level = 0)
    {
        return CrnDecoder.DecodeLevelFacesSegmented(baseCrnData, levelData, level);
    }

    public static byte[][] DecodeUnityCrunchFaces(ReadOnlySpan<byte> crnData, int level = 0)
    {
        return CrnDecoder.DecodeLevelFaces(crnData, level);
    }

    public static Image<Rgba32> Decode(ReadOnlySpan<byte> crnData, int level = 0)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        byte[] blocks = CrnDecoder.DecodeLevel(crnData, level);
        int width = Math.Max(1, header.Width >> level);
        int height = Math.Max(1, header.Height >> level);
        return BcBlockDecoder.Decode(blocks, header.Format, width, height);
    }

    public static Image<Rgba32>[] DecodeFaces(ReadOnlySpan<byte> crnData, int level = 0)
    {
        CrnHeader header = CrnHeader.Read(crnData);
        byte[][] faceBlocks = CrnDecoder.DecodeLevelFaces(crnData, level);
        int width = Math.Max(1, header.Width >> level);
        int height = Math.Max(1, header.Height >> level);
        Image<Rgba32>[] faces = new Image<Rgba32>[faceBlocks.Length];
        for (int i = 0; i < faces.Length; i++)
            faces[i] = BcBlockDecoder.Decode(faceBlocks[i], header.Format, width, height);

        return faces;
    }

    public static Image<TPixel> Decode<TPixel>(ReadOnlySpan<byte> crnData, int level = 0)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<Rgba32> rgba = Decode(crnData, level);
        return rgba.CloneAs<TPixel>();
    }

    public static Image<Rgba32> DecodeUnityCrunchImage(ReadOnlySpan<byte> crnData, int level = 0)
    {
        return Decode(crnData, level);
    }

    public static byte[] Encode(Image<Rgba32> image, CrnFormat format, CrnEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return CrnEncoder.Encode(image, format, options ?? new CrnEncodeOptions());
    }

    public static byte[] EncodeFaces(IReadOnlyList<Image<Rgba32>> faces, CrnFormat format, CrnEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(faces);
        return CrnEncoder.EncodeFaces(faces, format, options ?? new CrnEncodeOptions());
    }

    public static byte[] Encode<TPixel>(Image<TPixel> image, CrnFormat format, CrnEncodeOptions? options = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ArgumentNullException.ThrowIfNull(image);
        using Image<Rgba32> rgba = image.CloneAs<Rgba32>();
        return Encode(rgba, format, options);
    }

    public static byte[] EncodeFaces<TPixel>(IReadOnlyList<Image<TPixel>> faces, CrnFormat format, CrnEncodeOptions? options = null)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        ArgumentNullException.ThrowIfNull(faces);
        Image<Rgba32>[] rgbaFaces = new Image<Rgba32>[faces.Count];
        try
        {
            for (int i = 0; i < faces.Count; i++)
            {
                ArgumentNullException.ThrowIfNull(faces[i]);
                rgbaFaces[i] = faces[i].CloneAs<Rgba32>();
            }

            return EncodeFaces(rgbaFaces, format, options);
        }
        finally
        {
            foreach (Image<Rgba32>? face in rgbaFaces)
                face?.Dispose();
        }
    }

    public static byte[] EncodeUnityCrunch(Image<Rgba32> image, CrnFormat format, CrnEncodeOptions? options = null)
    {
        return Encode(image, format, options);
    }

    public static byte[] EncodeUnityCrunch(
        ReadOnlySpan<byte> rgba32,
        int width,
        int height,
        CrnFormat format,
        int quality = 128,
        int mipCount = 1,
        uint userData0 = 1,
        uint userData1 = 0)
    {
        return CrnEncoder.Encode(rgba32, width, height, format, quality, mipCount, userData0, userData1);
    }
}
