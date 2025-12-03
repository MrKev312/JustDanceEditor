using AssetsTools.NET;
using AssetsTools.NET.Extra;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Buffers.Binary;

using TextureConverter.TextureConverterHelpers;

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

        Image<Rgba32> image = DecodeToImage(encodedData, texture.Format, texture.Width, texture.Height);
        image.Mutate(x => x.Flip(FlipMode.Vertical));
        return image;
    }

    private static Image<Rgba32> DecodeToImage(byte[] encodedData, UnityTextureFormat format, int width, int height)
    {
        return format switch
        {
            UnityTextureFormat.RGBA32 => LoadRawRgba(encodedData, width, height, ColorChannelOrder.Rgba),
            UnityTextureFormat.ARGB32 => LoadRawRgba(encodedData, width, height, ColorChannelOrder.Argb),
            UnityTextureFormat.BGRA32 => LoadRawRgba(encodedData, width, height, ColorChannelOrder.Bgra),
            UnityTextureFormat.RGB24 => LoadRgb24(encodedData, width, height),
            UnityTextureFormat.DXT1 => LoadDxtTexture(encodedData, width, height, useAlpha: false),
            UnityTextureFormat.DXT5 => LoadDxtTexture(encodedData, width, height, useAlpha: true),
            UnityTextureFormat.DXT1Crunched => LoadDxtTexture(TextureEncoderDecoder.DecodeCrunch(encodedData), width, height, useAlpha: false),
            UnityTextureFormat.DXT5Crunched => LoadDxtTexture(TextureEncoderDecoder.DecodeCrunch(encodedData), width, height, useAlpha: true),
            _ => throw new NotSupportedException($"Unsupported Unity texture format '{format}'.")
        };
    }

    private static Image<Rgba32> LoadRawRgba(byte[] data, int width, int height, ColorChannelOrder order)
    {
        if (data.Length != width * height * 4)
            throw new InvalidOperationException("Unexpected raw texture payload length.");

        if (order == ColorChannelOrder.Rgba)
            return Image.LoadPixelData<Rgba32>(data, width, height);

        byte[] converted = new byte[data.Length];
        for (int i = 0; i < data.Length; i += 4)
        {
            (byte r, byte g, byte b, byte a) = order switch
            {
                ColorChannelOrder.Argb => (data[i + 1], data[i + 2], data[i + 3], data[i]),
                ColorChannelOrder.Bgra => (data[i + 2], data[i + 1], data[i], data[i + 3]),
                _ => throw new InvalidOperationException("Unsupported channel order."),
            };

            converted[i] = r;
            converted[i + 1] = g;
            converted[i + 2] = b;
            converted[i + 3] = a;
        }

        return Image.LoadPixelData<Rgba32>(converted, width, height);
    }

    private static Image<Rgba32> LoadRgb24(byte[] data, int width, int height)
    {
        if (data.Length != width * height * 3)
            throw new InvalidOperationException("Unexpected RGB24 payload length.");

        byte[] converted = new byte[width * height * 4];
        int source = 0;
        for (int i = 0; i < converted.Length; i += 4)
        {
            converted[i] = data[source++];
            converted[i + 1] = data[source++];
            converted[i + 2] = data[source++];
            converted[i + 3] = 255;
        }

        return Image.LoadPixelData<Rgba32>(converted, width, height);
    }

    private static Image<Rgba32> LoadDxtTexture(byte[] data, int width, int height, bool useAlpha)
    {
        byte[] rgba = new byte[width * height * 4];
        if (useAlpha)
            DecodeDxt5(data, width, height, rgba);
        else
            DecodeDxt1(data, width, height, rgba);

        return Image.LoadPixelData<Rgba32>(rgba, width, height);
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
            searchPath = searchPath.Substring(9);
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

    private static void DecodeDxt1(byte[] data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) / 4;
        int blockCountY = (height + 3) / 4;
        int dataIndex = 0;

        Span<Rgba32> colors = stackalloc Rgba32[4];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(dataIndex));
                ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(dataIndex + 2));
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dataIndex + 4));
                dataIndex += 8;

                colors[0] = FromRgb565(color0);
                colors[1] = FromRgb565(color1);

                if (color0 > color1)
                {
                    colors[2] = Interpolate(colors[0], colors[1], 2, 1);
                    colors[3] = Interpolate(colors[0], colors[1], 1, 2);
                }
                else
                {
                    colors[2] = Interpolate(colors[0], colors[1], 1, 1);
                    colors[3] = new Rgba32(0, 0, 0, 0);
                }

                WriteColorBlock(destination, colors, code, bx, by, width, height, ReadOnlySpan<byte>.Empty, false, 0);
            }
        }
    }

    private static void DecodeDxt5(byte[] data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) / 4;
        int blockCountY = (height + 3) / 4;
        int dataIndex = 0;

        Span<Rgba32> colors = stackalloc Rgba32[4];
        Span<byte> alphas = stackalloc byte[8];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                byte alpha0 = data[dataIndex];
                byte alpha1 = data[dataIndex + 1];
                ulong alphaBits = 0;
                for (int i = 0; i < 6; i++)
                    alphaBits |= (ulong)data[dataIndex + 2 + i] << (8 * i);
                dataIndex += 8;

                BuildAlphaTable(alpha0, alpha1, alphas);

                ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(dataIndex));
                ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(dataIndex + 2));
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(dataIndex + 4));
                dataIndex += 8;

                colors[0] = FromRgb565(color0);
                colors[1] = FromRgb565(color1);
                colors[2] = Interpolate(colors[0], colors[1], 2, 1);
                colors[3] = Interpolate(colors[0], colors[1], 1, 2);

                WriteColorBlock(destination, colors, code, bx, by, width, height, alphas, true, alphaBits);
            }
        }
    }

    private static void WriteColorBlock(byte[] destination, Span<Rgba32> colors, uint code, int blockX, int blockY, int width, int height, ReadOnlySpan<byte> alphaLut, bool hasAlpha, ulong alphaBits)
    {
        ulong alphaData = alphaBits;
        for (int py = 0; py < 4; py++)
        {
            for (int px = 0; px < 4; px++)
            {
                int pixelIndex = (int)(code & 0x3);
                code >>= 2;

                int x = (blockX * 4) + px;
                int y = (blockY * 4) + py;
                if (x >= width || y >= height)
                    continue;

                int destIndex = ((y * width) + x) * 4;
                Rgba32 color = colors[pixelIndex];

                destination[destIndex] = color.R;
                destination[destIndex + 1] = color.G;
                destination[destIndex + 2] = color.B;
                destination[destIndex + 3] = hasAlpha ? alphaLut[(int)(alphaData & 0x7)] : color.A;

                if (hasAlpha)
                    alphaData >>= 3;
            }
        }
    }

    private static void BuildAlphaTable(byte alpha0, byte alpha1, Span<byte> table)
    {
        table[0] = alpha0;
        table[1] = alpha1;
        if (alpha0 > alpha1)
        {
            table[2] = (byte)(((6 * alpha0) + (1 * alpha1)) / 7);
            table[3] = (byte)(((5 * alpha0) + (2 * alpha1)) / 7);
            table[4] = (byte)(((4 * alpha0) + (3 * alpha1)) / 7);
            table[5] = (byte)(((3 * alpha0) + (4 * alpha1)) / 7);
            table[6] = (byte)(((2 * alpha0) + (5 * alpha1)) / 7);
            table[7] = (byte)(((1 * alpha0) + (6 * alpha1)) / 7);
        }
        else
        {
            table[2] = (byte)(((4 * alpha0) + (1 * alpha1)) / 5);
            table[3] = (byte)(((3 * alpha0) + (2 * alpha1)) / 5);
            table[4] = (byte)(((2 * alpha0) + (3 * alpha1)) / 5);
            table[5] = (byte)(((1 * alpha0) + (4 * alpha1)) / 5);
            table[6] = 0;
            table[7] = 255;
        }
    }

    private static Rgba32 FromRgb565(ushort value)
    {
        byte r = (byte)(((((value >> 11) & 0x1F) * 527) + 23) >> 6);
        byte g = (byte)(((((value >> 5) & 0x3F) * 259) + 33) >> 6);
        byte b = (byte)((((value & 0x1F) * 527) + 23) >> 6);
        return new Rgba32(r, g, b, 255);
    }

    private static Rgba32 Interpolate(Rgba32 c0, Rgba32 c1, int weight0, int weight1)
    {
        int total = weight0 + weight1;
        byte r = (byte)(((c0.R * weight0) + (c1.R * weight1)) / total);
        byte g = (byte)(((c0.G * weight0) + (c1.G * weight1)) / total);
        byte b = (byte)(((c0.B * weight0) + (c1.B * weight1)) / total);
        byte a = (byte)(((c0.A * weight0) + (c1.A * weight1)) / total);
        return new Rgba32(r, g, b, a);
    }

    private readonly record struct UnityTextureData(int Width, int Height, UnityTextureFormat Format, byte[] ImageData, StreamingInfoData StreamInfo)
    {
        public static UnityTextureData FromBaseField(AssetTypeValueField baseField)
        {
            int width = baseField["m_Width"].AsInt;
            int height = baseField["m_Height"].AsInt;
            UnityTextureFormat format = (UnityTextureFormat)baseField["m_TextureFormat"].AsInt;
            byte[] imageData = baseField["image data"].AsByteArray ?? Array.Empty<byte>();

            AssetTypeValueField streamField = baseField["m_StreamData"];
            StreamingInfoData streamInfo = new(
                streamField["offset"].AsLong,
                streamField["size"].AsUInt,
                streamField["path"].AsString ?? string.Empty);

            return new UnityTextureData(width, height, format, imageData, streamInfo);
        }
    }

    private readonly record struct StreamingInfoData(long Offset, uint Size, string Path);

    private enum ColorChannelOrder
    {
        Rgba,
        Argb,
        Bgra,
    }

    private enum UnityTextureFormat
    {
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        BGRA32 = 14,
        DXT1 = 10,
        DXT5 = 12,
        DXT1Crunched = 28,
        DXT5Crunched = 29,
    }
}
