using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using BCnEncoder.Shared.ImageFiles;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

namespace TextureConverter.TextureType;

public class DDS
{
    // DDS Header Constants
    public const uint FOURCC_DXT1 = 0x31545844;
    public const uint FOURCC_DXT2 = 0x32545844;
    public const uint FOURCC_DXT3 = 0x33545844;
    public const uint FOURCC_DXT4 = 0x34545844;
    public const uint FOURCC_DXT5 = 0x35545844;
    public const uint FOURCC_ATI1 = 0x31495441;
    public const uint FOURCC_ATI2 = 0x32495441;
    public const uint FOURCC_BC4U = 0x55344342;
    public const uint FOURCC_BC4S = 0x53344342;
    public const uint FOURCC_BC5U = 0x55354342;
    public const uint FOURCC_BC5S = 0x53354342;
    public const uint FOURCC_DX10 = 0x30315844;

    [Flags]
    public enum DDSHeaderFlags : uint
    {
        CAPS = 0x00000001,
        HEIGHT = 0x00000002,
        WIDTH = 0x00000004,
        PITCH = 0x00000008,
        PIXELFORMAT = 0x00001000,
        MIPMAPCOUNT = 0x00020000,
        LINEARSIZE = 0x00080000,
        DEPTH = 0x00800000
    }

    [Flags]
    public enum DDSPixelFormatFlags : uint
    {
        ALPHAPIXELS = 0x00000001,
        ALPHA = 0x00000002,
        FOURCC = 0x00000004,
        RGB = 0x00000040,
        YUV = 0x00000200,
        LUMINANCE = 0x00020000,
    }

    [Flags]
    public enum DDSCaps : uint
    {
        COMPLEX = 0x00000008,
        TEXTURE = 0x00001000,
        MIPMAP = 0x00400000,
    }

    [Flags]
    public enum DDSCaps2 : uint
    {
        CUBEMAP = 0x00000200,
        VOLUME = 0x00200000
    }

    public enum DDSFormat
    {
        // Common formats
        ETC1,
        DXT1,
        DXT3,
        DXT5,
        BC1,
        BC2,
        BC3,
        BC4U,
        BC4S,
        BC5U,
        BC5S,
        // NVN formats
        RGBA8,
        RGBA_SRGB,
        RGB10A2,
        RGB565,
        RGB5A1,
        RGBA4,
        R8,
        RG8,
        L8,
        LA8,
        LA4
    }

    public static readonly DDSFormat[] BCnFormats =
    [
        DDSFormat.DXT1,
        DDSFormat.DXT3,
        DDSFormat.DXT5,
        DDSFormat.BC1,
        DDSFormat.BC2,
        DDSFormat.BC3,
        DDSFormat.BC4U,
        DDSFormat.BC4S,
        DDSFormat.BC5U,
        DDSFormat.BC5S
    ];

    // Standard color masks for common formats
    private static readonly uint[] A1R5G5B5_MASKS = [0x7C00, 0x03E0, 0x001F, 0x8000];
    private static readonly uint[] X1R5G5B5_MASKS = [0x7C00, 0x03E0, 0x001F, 0x0000];
    private static readonly uint[] A4R4G4B4_MASKS = [0x0F00, 0x00F0, 0x000F, 0xF000];
    private static readonly uint[] X4R4G4B4_MASKS = [0x0F00, 0x00F0, 0x000F, 0x0000];
    private static readonly uint[] R5G6B5_MASKS = [0xF800, 0x07E0, 0x001F, 0x0000];
    private static readonly uint[] R8G8B8_MASKS = [0xFF0000, 0x00FF00, 0x0000FF, 0x000000];
    private static readonly uint[] A8B8G8R8_MASKS = [0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000];
    private static readonly uint[] X8B8G8R8_MASKS = [0x000000FF, 0x0000FF00, 0x00FF0000, 0x00000000];
    private static readonly uint[] A8R8G8B8_MASKS = [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000];
    private static readonly uint[] X8R8G8B8_MASKS = [0x00FF0000, 0x0000FF00, 0x000000FF, 0x00000000];
    private static readonly uint[] L8_MASKS = [0x000000FF, 0x0000];
    private static readonly uint[] A8L8_MASKS = [0x000000FF, 0x0F00];

    public class DDSHeader
    {
        public uint size = 124;
        public uint flags;
        public uint height;
        public uint width;
        public uint pitchOrLinearSize;
        public uint depth;
        public uint mipmapCount;
        public uint[] reserved1 = new uint[11];
        public DDSPixelFormat pixelFormat = new();
        public uint caps;
        public uint caps2;
        public uint caps3;
        public uint caps4;
        public uint reserved2;
    }

    public class DDSPixelFormat
    {
        public uint size = 32;
        public uint flags;
        public uint fourCC;
        public uint rgbBitCount;
        public uint rBitMask;
        public uint gBitMask;
        public uint bBitMask;
        public uint aBitMask;
    }

    public static Image<Bgra32> GetImage(Stream data)
    {
        using BinaryReader reader = new(data, Encoding.Default, leaveOpen: true);

        string magic = new(reader.ReadChars(4));
        if (magic != "DDS ")
            throw new InvalidOperationException("Invalid DDS file signature");

        DDSHeader header = ReadHeader(reader);
        byte[] imageData = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));

        return DecodeImageData(imageData, header);
    }

    private static DDSHeader ReadHeader(BinaryReader reader)
    {
        DDSHeader header = new()
        {
            size = reader.ReadUInt32(),
            flags = reader.ReadUInt32(),
            height = reader.ReadUInt32(),
            width = reader.ReadUInt32(),
            pitchOrLinearSize = reader.ReadUInt32(),
            depth = reader.ReadUInt32(),
            mipmapCount = reader.ReadUInt32()
        };
        for (int i = 0; i < 11; i++)
            header.reserved1[i] = reader.ReadUInt32();

        header.pixelFormat.size = reader.ReadUInt32();
        header.pixelFormat.flags = reader.ReadUInt32();
        header.pixelFormat.fourCC = reader.ReadUInt32();
        header.pixelFormat.rgbBitCount = reader.ReadUInt32();
        header.pixelFormat.rBitMask = reader.ReadUInt32();
        header.pixelFormat.gBitMask = reader.ReadUInt32();
        header.pixelFormat.bBitMask = reader.ReadUInt32();
        header.pixelFormat.aBitMask = reader.ReadUInt32();

        header.caps = reader.ReadUInt32();
        header.caps2 = reader.ReadUInt32();
        header.caps3 = reader.ReadUInt32();
        header.caps4 = reader.ReadUInt32();
        header.reserved2 = reader.ReadUInt32();

        return header;
    }

    private static Image<Bgra32> DecodeImageData(byte[] data, DDSHeader header)
    {
        bool isCompressed = (header.pixelFormat.flags & (uint)DDSPixelFormatFlags.FOURCC) != 0;

        if (isCompressed)
        {
            return DecodeCompressed(data, header);
        }

        bool isRGB = (header.pixelFormat.flags & (uint)DDSPixelFormatFlags.RGB) != 0;
        bool hasAlpha = (header.pixelFormat.flags & (uint)DDSPixelFormatFlags.ALPHAPIXELS) != 0;
        bool isLuminance = (header.pixelFormat.flags & (uint)DDSPixelFormatFlags.LUMINANCE) != 0;

        uint bpp = header.pixelFormat.rgbBitCount / 8;

        // Decode based on format
        if (isLuminance)
        {
            return DecodeLuminance(data, header, bpp, hasAlpha);
        }
        else if (isRGB)
        {
            return DecodeRGB(data, header, bpp, hasAlpha);
        }

        throw new NotSupportedException("Unsupported DDS format");
    }

    private static Image<Bgra32> DecodeCompressed(byte[] data, DDSHeader header)
    {
        uint fourCC = header.pixelFormat.fourCC;
        int width = (int)header.width;
        int height = (int)header.height;

        // Map FourCC to BCnEncoder CompressionFormat
        CompressionFormat format = fourCC switch
        {
            FOURCC_DXT1 => CompressionFormat.Bc1,
            FOURCC_DXT2 or FOURCC_DXT3 => CompressionFormat.Bc2,
            FOURCC_DXT4 or FOURCC_DXT5 => CompressionFormat.Bc3,
            FOURCC_ATI1 or FOURCC_BC4U => CompressionFormat.Bc4,
            FOURCC_BC4S => CompressionFormat.Bc4,
            FOURCC_ATI2 or FOURCC_BC5U => CompressionFormat.Bc5,
            FOURCC_BC5S => CompressionFormat.Bc5,
            _ => throw new NotSupportedException($"Unsupported compressed format: 0x{fourCC:X8}")
        };

        // Use BCnEncoder extension to decompress directly to Rgba32
        BcDecoder decoder = new();
        using Image<Rgba32> decodedImage = decoder.DecodeRawToImageRgba32(data, width, height, format);

        // Convert from Rgba32 to Bgra32
        Image<Bgra32> result = new(width, height);

        // Extract RGBA32 data and copy to BGRA32
        Rgba32[] pixelArray = new Rgba32[width * height];
        decodedImage.CopyPixelDataTo(pixelArray);

        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    Rgba32 pixel = pixelArray[(y * width) + x];
                    row[x] = new Bgra32(pixel.R, pixel.G, pixel.B, pixel.A);
                }
            }
        });

        return result;
    }

    private static Image<Bgra32> DecodeLuminance(byte[] data, DDSHeader header, uint bpp, bool hasAlpha)
    {
        Image<Bgra32> image = new((int)header.width, (int)header.height);
        int offset = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte luminance = data[offset++];
                    byte alpha = hasAlpha ? data[offset++] : (byte)255;
                    row[x] = new Bgra32(luminance, luminance, luminance, alpha);
                }
            }
        });

        return image;
    }

    private static Image<Bgra32> DecodeRGB(byte[] data, DDSHeader header, uint bpp, bool hasAlpha)
    {
        Image<Bgra32> image = new((int)header.width, (int)header.height);
        int offset = 0;

        if (bpp == 4)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        uint pixel = BitConverter.ToUInt32(data, offset);
                        offset += 4;

                        byte b = (byte)((pixel & header.pixelFormat.bBitMask) >> CountTrailingZeros(header.pixelFormat.bBitMask));
                        byte g = (byte)((pixel & header.pixelFormat.gBitMask) >> CountTrailingZeros(header.pixelFormat.gBitMask));
                        byte r = (byte)((pixel & header.pixelFormat.rBitMask) >> CountTrailingZeros(header.pixelFormat.rBitMask));
                        byte a = hasAlpha ? (byte)((pixel & header.pixelFormat.aBitMask) >> CountTrailingZeros(header.pixelFormat.aBitMask)) : (byte)255;

                        row[x] = new Bgra32(r, g, b, a);
                    }
                }
            });
        }
        else if (bpp == 3)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        byte b = data[offset++];
                        byte g = data[offset++];
                        byte r = data[offset++];
                        row[x] = new Bgra32(r, g, b, 255);
                    }
                }
            });
        }
        else if (bpp == 2)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Bgra32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        ushort pixel = BitConverter.ToUInt16(data, offset);
                        offset += 2;

                        uint b = (pixel & header.pixelFormat.bBitMask) >> CountTrailingZeros(header.pixelFormat.bBitMask);
                        uint g = (pixel & header.pixelFormat.gBitMask) >> CountTrailingZeros(header.pixelFormat.gBitMask);
                        uint r = (pixel & header.pixelFormat.rBitMask) >> CountTrailingZeros(header.pixelFormat.rBitMask);
                        uint a = hasAlpha ? (pixel & header.pixelFormat.aBitMask) >> CountTrailingZeros(header.pixelFormat.aBitMask) : 0xFF;

                        // Normalize to 8-bit by replicating high bits into low bits
                        int rBits = CountSetBits(header.pixelFormat.rBitMask);
                        int gBits = CountSetBits(header.pixelFormat.gBitMask);
                        int bBits = CountSetBits(header.pixelFormat.bBitMask);
                        int aBits = CountSetBits(header.pixelFormat.aBitMask);

                        byte rb = rBits > 0 ? (byte)((r << (8 - rBits)) | (r >> ((2 * rBits) - 8))) : (byte)255;
                        byte gb = gBits > 0 ? (byte)((g << (8 - gBits)) | (g >> ((2 * gBits) - 8))) : (byte)255;
                        byte bb = bBits > 0 ? (byte)((b << (8 - bBits)) | (b >> ((2 * bBits) - 8))) : (byte)255;
                        byte ab = aBits > 0 ? (byte)((a << (8 - aBits)) | (a >> ((2 * aBits) - 8))) : (byte)255;

                        row[x] = new Bgra32(rb, gb, bb, ab);
                    }
                }
            });
        }

        return image;
    }

    private static int CountTrailingZeros(uint value)
    {
        if (value == 0)
            return 32;
        int count = 0;
        while ((value & 1) == 0)
        {
            count++;
            value >>= 1;
        }

        return count;
    }

    private static int CountSetBits(uint value)
    {
        int count = 0;
        while (value != 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }

        return count;
    }

    public static byte[] GenerateHeader(uint mipCount, uint width, uint height, DDSFormat format, (uint, uint, uint, uint) compSel, uint size)
    {
        bool compressed = BCnFormats.Contains(format);
        byte[] hdr = new byte[128];
        (uint, uint, uint, uint, uint) compSels = (0, 0, 0, 0, 0);

        bool luminance = false;
        bool RGB = false;
        bool hasAlpha = false;
        uint fmtBPP = 0;

        switch (format)
        {
            //case XTX.XTXImageFormat.NVN_FORMAT_RGBA8:
            //case XTX.XTXImageFormat.NVN_FORMAT_RGBA8_SRGB:
            case DDSFormat.RGBA8:
            case DDSFormat.RGBA_SRGB:
                RGB = true;
                // Write order is BGRA, so masks should be: B at 0xFF, G at 0xFF00, R at 0xFF0000, A at 0xFF000000
                // But we present it as RGBA in the mask (standard DDS format)
                compSels = (0xFF0000, 0x00FF00, 0x0000FF, 0xFF000000, 0);
                fmtBPP = 4;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGB10A2:
            case DDSFormat.RGB10A2:
                RGB = true;
                compSels = (0x3FF00000, 0xFFC00, 0x3FF, 0xC0000000, 0);
                fmtBPP = 4;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGB565:
            case DDSFormat.RGB565:
                RGB = true;
                // Write order is BGR: B (0-4), G (5-10), R (11-15)
                compSels = (0xF800, 0x7E0, 0x1F, 0, 0);
                fmtBPP = 2;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGB5A1:
            case DDSFormat.RGB5A1:
                RGB = true;
                compSels = (0x1F, 0x3E0, 0x7C00, 0x8000, 0);
                fmtBPP = 2;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RGBA4:
            case DDSFormat.RGBA4:
                RGB = true;
                compSels = (0xF, 0xF0, 0xF00, 0xF000, 0);
                fmtBPP = 2;
                hasAlpha = true;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_R8:
            case DDSFormat.L8:
                luminance = true;
                compSels = (0xFF, 0, 0, 0, 0);
                fmtBPP = 1;
                break;

            //case XTX.XTXImageFormat.NVN_FORMAT_RG8:
            case DDSFormat.LA8:
                luminance = true;
                compSels = (0xFF, 0xFF00, 0, 0, 0);
                fmtBPP = 2;
                break;
        }

        uint flags = 0x00000001 | 0x00001000 | 0x00000004 | 0x00000002; // CAPS | PIXELFORMAT | WIDTH | HEIGHT
        uint caps = 0x00001000; // TEXTURE

        byte[] fourCC = new byte[4];

        if (mipCount == 0)
            mipCount = 1;
        else if (mipCount != 1)
        {
            flags |= 0x00020000;
            caps |= 0x00400008;
        }

        uint pFlags;
        if (compressed)
        {
            flags |= 0x00080000; // LINEARSIZE
            pFlags = 0x00000004; // FOURCC

            fourCC = format switch
            {
                DDSFormat.ETC1 => Encoding.ASCII.GetBytes("ETC1"),
                DDSFormat.DXT1 => Encoding.ASCII.GetBytes("DXT1"),
                DDSFormat.DXT3 => Encoding.ASCII.GetBytes("DXT3"),
                DDSFormat.DXT5 => Encoding.ASCII.GetBytes("DXT5"),
                DDSFormat.BC1 => Encoding.ASCII.GetBytes("DXT1"),
                DDSFormat.BC2 => Encoding.ASCII.GetBytes("DXT3"),
                DDSFormat.BC3 => Encoding.ASCII.GetBytes("DXT5"),
                DDSFormat.BC4U => Encoding.ASCII.GetBytes("ATI1"),
                DDSFormat.BC4S => Encoding.ASCII.GetBytes("BC4S"),
                DDSFormat.BC5U => Encoding.ASCII.GetBytes("BC5U"),
                DDSFormat.BC5S => Encoding.ASCII.GetBytes("BC5S"),
                _ => throw new Exception("Unsupported format!"),
            };
        }
        else
        {
            flags |= 0x00080000; // LINEARSIZE

            if (RGB && hasAlpha)
                pFlags = 0x00000040 | 0x00000001; // RGB | ALPHAPIXELS
            else if (luminance)
                pFlags = 0x00020000;
            else if (RGB)
                pFlags = 0x00000040;
            else
                throw new Exception("Unsupported format!");

            size = width * fmtBPP;
        }

        Array.Copy(Encoding.ASCII.GetBytes("DDS "), 0, hdr, 0, 4);
        Array.Copy(BitConverter.GetBytes((uint)124), 0, hdr, 4, 4);
        Array.Copy(BitConverter.GetBytes(flags), 0, hdr, 8, 4);
        Array.Copy(BitConverter.GetBytes(height), 0, hdr, 12, 4);
        Array.Copy(BitConverter.GetBytes(width), 0, hdr, 16, 4);
        Array.Copy(BitConverter.GetBytes(size), 0, hdr, 20, 4); // PitchOrLinearSize
        Array.Copy(BitConverter.GetBytes(mipCount), 0, hdr, 28, 4);
        Array.Copy(BitConverter.GetBytes((uint)32), 0, hdr, 76, 4); // PixelFormat Size
        Array.Copy(BitConverter.GetBytes(pFlags), 0, hdr, 80, 4);

        uint[] compSelsArr = [
            compSels.Item1,
            compSels.Item2,
            compSels.Item3,
            compSels.Item4,
            compSels.Item5
            ];

        if (compressed)
            Array.Copy(fourCC, 0, hdr, 84, 4);
        else
        {
            Array.Copy(BitConverter.GetBytes(fmtBPP << 3), 0, hdr, 88, 4);

            if (compSel.Item1 < 5)
                Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item1]), 0, hdr, 92, 4);
            if (compSel.Item2 < 5)
                Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item2]), 0, hdr, 96, 4);
            if (compSel.Item3 < 5)
                Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item3]), 0, hdr, 100, 4);
            if (compSel.Item4 < 5)
                Array.Copy(BitConverter.GetBytes(compSelsArr[compSel.Item4]), 0, hdr, 104, 4);
        }

        Array.Copy(BitConverter.GetBytes(caps), 0, hdr, 108, 4);

        hdr = format switch
        {
            DDSFormat.BC4U => [.. hdr, .. new byte[] { 0x50, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            DDSFormat.BC4S => [.. hdr, .. new byte[] { 0x51, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            DDSFormat.BC5U => [.. hdr, .. new byte[] { 0x53, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            DDSFormat.BC5S => [.. hdr, .. new byte[] { 0x54, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }],
            _ => hdr,
        };

        return hdr;
    }

    /// <summary>
    /// Converts an image to DDS format and writes it to a stream.
    /// </summary>
    public static void ConvertToFile(Image<Bgra32> image, DDSFormat format, Stream output, (uint, uint, uint, uint)? compSel = null)
    {
        uint width = (uint)image.Width;
        uint height = (uint)image.Height;
        uint mipCount = 1;

        // Default component selection based on format
        (uint, uint, uint, uint) cs = compSel ?? format switch
        {
            DDSFormat.L8 => (0, 0, 0, 5),
            DDSFormat.LA8 => (0, 1, 0, 5),
            DDSFormat.RGB565 => (0, 1, 2, 5),
            _ => (0, 1, 2, 3),
        };

        // Convert image data
        byte[] imageData = ConvertImageDataToFormat(image, format);

        // For BCn, size in header is typically pitch or linear size
        uint size = 0;
        if (BCnFormats.Contains(format))
        {
            // For DXT1, 8 bytes per block (4x4 pixels). DXT5, 16 bytes.
            int blockSize = (format == DDSFormat.DXT1 || format == DDSFormat.BC1) ? 8 : 16;
            int blocksWide = (int)((width + 3) / 4);
            int blocksHigh = (int)((height + 3) / 4);
            size = (uint)(blocksWide * blocksHigh * blockSize);
        }
        else
        {
            size = width * (uint)GetBPP_DDS(format);
        }

        byte[] header = GenerateHeader(mipCount, width, height, format, cs, size);

        output.Write(header);
        output.Write(imageData);
    }

    private static int GetBPP_DDS(DDSFormat format)
    {
        return format switch { DDSFormat.RGBA8 => 4, _ => 0 }; // Simplified
    }

    private static byte[] ConvertImageDataToFormat(Image<Bgra32> image, DDSFormat format)
    {
        return format switch
        {
            DDSFormat.RGBA8 => ConvertToRGBA8(image),
            DDSFormat.DXT1 or DDSFormat.BC1 => CompressBCn(image, CompressionFormat.Bc1),
            DDSFormat.DXT3 or DDSFormat.BC2 => CompressBCn(image, CompressionFormat.Bc2),
            DDSFormat.DXT5 or DDSFormat.BC3 => CompressBCn(image, CompressionFormat.Bc3),
            _ => throw new NotImplementedException($"Format {format} is not supported.")
        };
    }

    private static byte[] ConvertToRGBA8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 4];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = row[x].B;
                    result[offset++] = row[x].G;
                    result[offset++] = row[x].R;
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }

    private static byte[] CompressBCn(Image<Bgra32> image, CompressionFormat format)
    {
        // BCnEncoder expects Rgba32
        using Image<Rgba32> rgba = image.CloneAs<Rgba32>();

        BcEncoder encoder = new()
        {
            OutputOptions = { GenerateMipMaps = false, Quality = CompressionQuality.BestQuality, Format = format }
        };
        return encoder.EncodeToRawBytes(rgba)[0];
    }
}