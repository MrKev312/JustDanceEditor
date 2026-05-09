using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Buffers.Binary;
using System.Text;

using TextureConverter.TextureConverterHelpers;

using static TextureConverter.TextureType.DDS;

namespace TextureConverter.TextureType;

public sealed class Xbox360
{
    public const int CkdWrapperOffset = 0x2C;
    public const int HeaderSize = 0x34;
    private const uint TextureFetchDimension2D = 0x00000003;
    private const uint TextureFetchConstant = 0x00000001;

    public enum Xbox360TextureFormat : uint
    {
        L8 = 0x02,
        R5G6B5 = 0x44,
        A8L8 = 0x4A,
        X4R4G4B4 = 0x4F,
        DXT1 = 0x52,
        DXT3 = 0x53,
        DXT5 = 0x54,
        DXN = 0x71,
        A8R8G8B8 = 0x86,
    }

    public Xbox360TextureHeader Header { get; private set; } = new();
    public byte[] TextureData { get; private set; } = [];

    public static bool IsHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize)
            return false;

        uint word0 = BinaryPrimitives.ReadUInt32BigEndian(header);
        uint word1 = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        uint format = BinaryPrimitives.ReadUInt32BigEndian(header[0x20..]);
        uint packedSize = BinaryPrimitives.ReadUInt32BigEndian(header[0x24..]);

        if (word0 != TextureFetchDimension2D || word1 != TextureFetchConstant)
            return false;

        if (!Enum.IsDefined(typeof(Xbox360TextureFormat), format))
            return false;

        uint width = ((packedSize >> 13) & 0x1FFF) + 1;
        uint height = (packedSize & 0x1FFF) + 1;

        return width > 0 && height > 0 && width <= 8192 && height <= 8192;
    }

    public void LoadFile(Stream data)
    {
        long baseOffset = LocatePayload(data);
        data.Seek(baseOffset, SeekOrigin.Begin);

        using EndianBinaryReader reader = new(data, Encoding.Default, leaveOpen: true, isBigEndian: true);
        uint[] words = new uint[13];
        for (int i = 0; i < words.Length; i++)
            words[i] = reader.ReadUInt32();

        byte[] rawHeader = new byte[HeaderSize];
        for (int i = 0; i < words.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(rawHeader.AsSpan(i * 4), words[i]);

        if (!IsHeader(rawHeader))
            throw new InvalidDataException("Invalid Xbox 360 texture header.");

        Header = Xbox360TextureHeader.FromWords(words, rawHeader);

        int expectedDataSize = GetTextureDataSize((int)Header.Width, (int)Header.Height, Header.Format);
        if (data.Length - data.Position < expectedDataSize)
            throw new InvalidDataException($"Xbox 360 texture payload is too small. Expected {expectedDataSize} bytes.");

        TextureData = reader.ReadBytes(expectedDataSize);
    }

    public Image<Bgra32> ConvertToImage()
    {
        byte[] linearData = DecodeTextureDataToLinear(TextureData, (int)Header.Width, (int)Header.Height, Header.Format);
        DDSFormat ddsFormat = ConvertXbox360ToDDSFormat(Header.Format);
        byte[] ddsHeader = GenerateHeader(1, Header.Width, Header.Height, ddsFormat, GetCompSel(Header.Format), (uint)linearData.Length);
        if (ddsHeader.Length > 128)
            ddsHeader = ddsHeader[..128];

        using MemoryStream stream = new([.. ddsHeader, .. linearData]);
        return DDS.GetImage(stream);
    }

    public static Image<Bgra32> GetImage(Stream data)
    {
        Xbox360 texture = new();
        texture.LoadFile(data);
        return texture.ConvertToImage();
    }

    public static void ConvertToFile(Image<Bgra32> image, Xbox360TextureFormat format, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);

        uint width = (uint)image.Width;
        uint height = (uint)image.Height;
        byte[] linearData = ConvertImageDataToFormat(image, format);
        byte[] textureData = EncodeLinearTextureData(linearData, image.Width, image.Height, format);
        Xbox360TextureHeader header = Xbox360TextureHeader.Create(width, height, format);

        WritePayload(output, header, textureData);
    }

    public static void ConvertToCkdFile(Image<Bgra32> image, Xbox360TextureFormat format, Stream output)
    {
        ArgumentNullException.ThrowIfNull(image);

        using MemoryStream payload = new();
        ConvertToFile(image, format, payload);
        byte[] payloadBytes = payload.ToArray();
        WriteCkdWrapper(output, image.Width, image.Height, format, payloadBytes);
    }

    public static int GetTextureDataSize(int width, int height, Xbox360TextureFormat format)
    {
        return format switch
        {
            Xbox360TextureFormat.L8 => width * height,
            Xbox360TextureFormat.A8L8 => width * height * 2,
            Xbox360TextureFormat.R5G6B5 or Xbox360TextureFormat.X4R4G4B4 => width * height * 2,
            Xbox360TextureFormat.A8R8G8B8 => width * height * 4,
            Xbox360TextureFormat.DXT1 => ((width + 3) / 4) * ((height + 3) / 4) * 8,
            Xbox360TextureFormat.DXT3 or Xbox360TextureFormat.DXT5 or Xbox360TextureFormat.DXN => ((width + 3) / 4) * ((height + 3) / 4) * 16,
            _ => throw new NotSupportedException($"Xbox 360 texture format {format} is not supported."),
        };
    }

    public static DDSFormat ConvertXbox360ToDDSFormat(Xbox360TextureFormat format)
    {
        return format switch
        {
            Xbox360TextureFormat.L8 => DDSFormat.L8,
            Xbox360TextureFormat.A8L8 => DDSFormat.LA8,
            Xbox360TextureFormat.R5G6B5 => DDSFormat.RGB565,
            Xbox360TextureFormat.X4R4G4B4 => DDSFormat.RGBA4,
            Xbox360TextureFormat.A8R8G8B8 => DDSFormat.RGBA8,
            Xbox360TextureFormat.DXT1 => DDSFormat.BC1,
            Xbox360TextureFormat.DXT3 => DDSFormat.BC2,
            Xbox360TextureFormat.DXT5 => DDSFormat.BC3,
            Xbox360TextureFormat.DXN => DDSFormat.BC5U,
            _ => throw new NotSupportedException($"Xbox 360 texture format {format} is not supported."),
        };
    }

    private static long LocatePayload(Stream data)
    {
        long start = data.CanSeek ? data.Position : 0;
        if (!data.CanSeek)
            return start;

        Span<byte> probe = stackalloc byte[Math.Min(CkdWrapperOffset + HeaderSize, (int)Math.Min(data.Length - start, CkdWrapperOffset + HeaderSize))];
        int read = data.Read(probe);
        data.Seek(start, SeekOrigin.Begin);

        if (read >= CkdWrapperOffset + HeaderSize &&
            probe[0] == 0x00 && probe[1] == 0x00 && probe[2] == 0x00 && probe[3] == 0x09 &&
            probe[4] == (byte)'T' && probe[5] == (byte)'E' && probe[6] == (byte)'X' && probe[7] == 0x00 &&
            BinaryPrimitives.ReadUInt32BigEndian(probe[8..]) == CkdWrapperOffset &&
            IsHeader(probe.Slice(CkdWrapperOffset, HeaderSize)))
        {
            return start + CkdWrapperOffset;
        }

        return start;
    }

    private static byte[] DecodeTextureDataToLinear(byte[] textureData, int width, int height, Xbox360TextureFormat format)
    {
        (int blockPixelSize, int texelPitch) = GetBlockLayout(format);
        byte[] source = RequiresEndianSwap16(format) ? SwapEndian16(textureData) : [.. textureData];
        return Xbox360TextureSwizzler.ToLinear(source, width, height, blockPixelSize, texelPitch);
    }

    private static byte[] EncodeLinearTextureData(byte[] linearData, int width, int height, Xbox360TextureFormat format)
    {
        (int blockPixelSize, int texelPitch) = GetBlockLayout(format);
        byte[] tiled = Xbox360TextureSwizzler.ToTiled(linearData, width, height, blockPixelSize, texelPitch);
        return RequiresEndianSwap16(format) ? SwapEndian16(tiled) : tiled;
    }

    private static (int BlockPixelSize, int TexelPitch) GetBlockLayout(Xbox360TextureFormat format)
    {
        return format switch
        {
            Xbox360TextureFormat.L8 => (1, 1),
            Xbox360TextureFormat.A8L8 => (1, 2),
            Xbox360TextureFormat.R5G6B5 or Xbox360TextureFormat.X4R4G4B4 => (1, 2),
            Xbox360TextureFormat.A8R8G8B8 => (1, 4),
            Xbox360TextureFormat.DXT1 => (4, 8),
            Xbox360TextureFormat.DXT3 or Xbox360TextureFormat.DXT5 or Xbox360TextureFormat.DXN => (4, 16),
            _ => throw new NotSupportedException($"Xbox 360 texture format {format} is not supported."),
        };
    }

    private static bool RequiresEndianSwap16(Xbox360TextureFormat format)
    {
        return format is Xbox360TextureFormat.A8L8
            or Xbox360TextureFormat.R5G6B5
            or Xbox360TextureFormat.X4R4G4B4
            or Xbox360TextureFormat.DXT1
            or Xbox360TextureFormat.DXT3
            or Xbox360TextureFormat.DXT5
            or Xbox360TextureFormat.DXN;
    }

    private static (uint, uint, uint, uint) GetCompSel(Xbox360TextureFormat format)
    {
        return format switch
        {
            Xbox360TextureFormat.L8 => (0, 0, 0, 5),
            Xbox360TextureFormat.A8L8 => (0, 1, 0, 5),
            Xbox360TextureFormat.R5G6B5 => (0, 1, 2, 5),
            _ => (0, 1, 2, 3),
        };
    }

    private static byte[] ConvertImageDataToFormat(Image<Bgra32> image, Xbox360TextureFormat format)
    {
        return format switch
        {
            Xbox360TextureFormat.L8 => PixelFormatConverter.ConvertToL8(image),
            Xbox360TextureFormat.A8L8 => PixelFormatConverter.ConvertToLA8(image),
            Xbox360TextureFormat.R5G6B5 => PixelFormatConverter.ConvertToRGB565(image),
            Xbox360TextureFormat.X4R4G4B4 => PixelFormatConverter.ConvertToRGBA4(image),
            Xbox360TextureFormat.A8R8G8B8 => PixelFormatConverter.ConvertToBGRA8(image),
            Xbox360TextureFormat.DXT1 => CompressBCn(image, CompressionFormat.Bc1),
            Xbox360TextureFormat.DXT3 => CompressBCn(image, CompressionFormat.Bc2),
            Xbox360TextureFormat.DXT5 => CompressBCn(image, CompressionFormat.Bc3),
            Xbox360TextureFormat.DXN => CompressBCn(image, CompressionFormat.Bc5),
            _ => throw new NotSupportedException($"Xbox 360 texture format {format} is not supported."),
        };
    }

    private static byte[] CompressBCn(Image<Bgra32> image, CompressionFormat format)
    {
        using Image<Rgba32> rgba = image.CloneAs<Rgba32>();
        BcEncoder encoder = new()
        {
            OutputOptions = { GenerateMipMaps = false, Quality = CompressionQuality.BestQuality, Format = format }
        };
        return encoder.EncodeToRawBytes(rgba)[0];
    }

    private static byte[] SwapEndian16(byte[] data)
    {
        byte[] result = [.. data];
        for (int i = 0; i + 1 < result.Length; i += 2)
            (result[i], result[i + 1]) = (result[i + 1], result[i]);

        return result;
    }

    private static void WritePayload(Stream output, Xbox360TextureHeader header, byte[] textureData)
    {
        using EndianBinaryWriter writer = new(output, Encoding.Default, leaveOpen: true, isBigEndian: true);
        foreach (uint word in header.ToWords())
            writer.Write(word);
        writer.Write(textureData);
    }

    private static void WriteCkdWrapper(Stream output, int width, int height, Xbox360TextureFormat format, byte[] payload)
    {
        int textureDataSize = payload.Length - HeaderSize;
        using EndianBinaryWriter writer = new(output, Encoding.Default, leaveOpen: true, isBigEndian: true);

        writer.Write(9U);
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        writer.Write((uint)CkdWrapperOffset);

        uint payloadSizeWithHeader = (uint)(textureDataSize + 0x80);
        writer.Write(payloadSizeWithHeader);
        writer.Write(ComputeWrapperDimensionWord(width, height, format));
        writer.Write(format is Xbox360TextureFormat.DXT1 ? 0x00011800U : 0x00012000U);
        writer.Write(payloadSizeWithHeader);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(format is Xbox360TextureFormat.DXT5 ? 0x0202CCCCU : 0x0000CCCCU);

        writer.Write(payload);
    }

    private static uint ComputeWrapperDimensionWord(int width, int height, Xbox360TextureFormat format)
    {
        int scale = format is Xbox360TextureFormat.DXT1 ? 4 : 2;
        uint scaledWidth = (uint)Math.Min(0xFFFF, width * scale);
        uint scaledHeight = (uint)Math.Min(0xFFFF, height * scale);
        return (scaledWidth << 16) | scaledHeight;
    }

    public sealed class Xbox360TextureHeader
    {
        public uint Width { get; private init; }
        public uint Height { get; private init; }
        public Xbox360TextureFormat Format { get; private init; }
        public byte[] RawHeader { get; private init; } = new byte[HeaderSize];

        public static Xbox360TextureHeader FromWords(uint[] words, byte[] rawHeader)
        {
            uint packedSize = words[9];
            return new Xbox360TextureHeader
            {
                Width = ((packedSize >> 13) & 0x1FFF) + 1,
                Height = (packedSize & 0x1FFF) + 1,
                Format = (Xbox360TextureFormat)words[8],
                RawHeader = [.. rawHeader],
            };
        }

        public static Xbox360TextureHeader Create(uint width, uint height, Xbox360TextureFormat format)
        {
            if (width == 0 || height == 0 || width > 8192 || height > 8192)
                throw new ArgumentOutOfRangeException(nameof(width), "Xbox 360 texture dimensions must be in the range 1..8192.");

            uint[] words =
            [
                TextureFetchDimension2D,
                TextureFetchConstant,
                0U,
                0U,
                0U,
                0xFFFF0000U,
                0xFFFF0000U,
                ComputeFetchWord0(width),
                (uint)format,
                ((width - 1) << 13) | (height - 1),
                0x00000D10U,
                0U,
                0x00000A00U,
            ];

            byte[] rawHeader = new byte[HeaderSize];
            for (int i = 0; i < words.Length; i++)
                BinaryPrimitives.WriteUInt32BigEndian(rawHeader.AsSpan(i * 4), words[i]);

            return FromWords(words, rawHeader);
        }

        public uint[] ToWords()
        {
            uint[] words = new uint[13];
            for (int i = 0; i < words.Length; i++)
                words[i] = BinaryPrimitives.ReadUInt32BigEndian(RawHeader.AsSpan(i * 4));

            return words;
        }

        private static uint ComputeFetchWord0(uint width)
        {
            uint widthBucket = Math.Max(1U, width >> 7);
            return 0x80000002U | ((widthBucket & 0x7FU) << 24);
        }
    }

    private static class Xbox360TextureSwizzler
    {
        public static byte[] ToLinear(byte[] data, int pixelWidth, int pixelHeight, int blockPixelSize, int texelPitch)
        {
            return Convert(data, pixelWidth, pixelHeight, blockPixelSize, texelPitch, toLinear: true);
        }

        public static byte[] ToTiled(byte[] data, int pixelWidth, int pixelHeight, int blockPixelSize, int texelPitch)
        {
            return Convert(data, pixelWidth, pixelHeight, blockPixelSize, texelPitch, toLinear: false);
        }

        private static byte[] Convert(byte[] data, int pixelWidth, int pixelHeight, int blockPixelSize, int texelPitch, bool toLinear)
        {
            int blockWidth = Math.Max(1, (pixelWidth + blockPixelSize - 1) / blockPixelSize);
            int blockHeight = Math.Max(1, (pixelHeight + blockPixelSize - 1) / blockPixelSize);
            byte[] result = new byte[data.Length];

            for (int y = 0; y < blockHeight; y++)
            {
                for (int x = 0; x < blockWidth; x++)
                {
                    int blockOffset = (y * blockWidth) + x;
                    int tiledX = XGAddress2DTiledX(blockOffset, blockWidth, texelPitch);
                    int tiledY = XGAddress2DTiledY(blockOffset, blockWidth, texelPitch);

                    int physicalOffset = ((y * blockWidth) + x) * texelPitch;
                    int linearOffset = ((tiledY * blockWidth) + tiledX) * texelPitch;

                    if (physicalOffset + texelPitch > data.Length || linearOffset + texelPitch > result.Length)
                        continue;

                    if (toLinear)
                        Array.Copy(data, physicalOffset, result, linearOffset, texelPitch);
                    else
                        Array.Copy(data, linearOffset, result, physicalOffset, texelPitch);
                }
            }

            return result;
        }

        private static int XGAddress2DTiledX(int offset, int width, int texelPitch)
        {
            int alignedWidth = (width + 31) & ~31;
            int logBpp = (texelPitch >> 2) + ((texelPitch >> 1) >> (texelPitch >> 2));
            int offsetB = offset << logBpp;
            int offsetT = ((offsetB & ~4095) >> 3) + ((offsetB & 1792) >> 2) + (offsetB & 63);
            int offsetM = offsetT >> (7 + logBpp);

            int macroX = (offsetM % (alignedWidth >> 5)) << 2;
            int tile = ((((offsetT >> (5 + logBpp)) & 2) + (offsetB >> 6)) & 3);
            int macro = (macroX + tile) << 3;
            int micro = ((((offsetT >> 1) & ~15) + (offsetT & 15)) & ((texelPitch << 3) - 1)) >> logBpp;

            return macro + micro;
        }

        private static int XGAddress2DTiledY(int offset, int width, int texelPitch)
        {
            int alignedWidth = (width + 31) & ~31;
            int logBpp = (texelPitch >> 2) + ((texelPitch >> 1) >> (texelPitch >> 2));
            int offsetB = offset << logBpp;
            int offsetT = ((offsetB & ~4095) >> 3) + ((offsetB & 1792) >> 2) + (offsetB & 63);
            int offsetM = offsetT >> (7 + logBpp);

            int macroY = (offsetM / (alignedWidth >> 5)) << 2;
            int tile = ((offsetT >> (6 + logBpp)) & 1) + ((offsetB & 2048) >> 10);
            int macro = (macroY + tile) << 3;
            int micro = ((((offsetT & (((texelPitch << 6) - 1) & ~31)) + ((offsetT & 15) << 1)) >> (3 + logBpp)) & ~1);

            return macro + micro + ((offsetT & 16) >> 4);
        }
    }
}
