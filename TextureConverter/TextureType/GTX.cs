using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using TextureConverter.TextureConverterHelpers;

using static TextureConverter.TextureType.DDS;

namespace TextureConverter.TextureType;

/// <summary>
/// GTX texture format handler for WiiU.
/// Based on Switch Toolbox implementation by AboodXD.
/// </summary>
public class GTX
{
    public const string Magic = "Gfx2";
    public const string BlockMagic = "BLK{";

    #region Enums

    public enum GX2SurfaceFormat : uint
    {
        Invalid = 0x0,
        TC_R8_UNORM = 0x1,
        TC_R8_UINT = 0x101,
        TC_R8_SNORM = 0x201,
        TC_R8_SINT = 0x301,
        T_R4_G4_UNORM = 0x2,
        TCD_R16_UNORM = 0x5,
        TC_R16_UINT = 0x105,
        TC_R16_SNORM = 0x205,
        TC_R16_SINT = 0x305,
        TC_R16_FLOAT = 0x806,
        TC_R8_G8_UNORM = 0x7,
        TC_R8_G8_UINT = 0x107,
        TC_R8_G8_SNORM = 0x207,
        TC_R8_G8_SINT = 0x307,
        TCS_R5_G6_B5_UNORM = 0x8,
        TC_R5_G5_B5_A1_UNORM = 0xA,
        TC_R4_G4_B4_A4_UNORM = 0xB,
        TC_A1_B5_G5_R5_UNORM = 0xC,
        TC_R32_UINT = 0x10D,
        TC_R32_SINT = 0x30D,
        TCD_R32_FLOAT = 0x80E,
        TC_R16_G16_UNORM = 0xF,
        TC_R16_G16_UINT = 0x10F,
        TC_R16_G16_SNORM = 0x20F,
        TC_R16_G16_SINT = 0x30F,
        TC_R16_G16_FLOAT = 0x810,
        D_D24_S8_UNORM = 0x11,
        T_R24_UNORM_X8 = 0x11,
        T_X24_G8_UINT = 0x111,
        D_D24_S8_FLOAT = 0x811,
        TC_R11_G11_B10_FLOAT = 0x816,
        TCS_R10_G10_B10_A2_UNORM = 0x19,
        TC_R10_G10_B10_A2_UINT = 0x119,
        TC_R10_G10_B10_A2_SNORM = 0x219,
        TC_R10_G10_B10_A2_SINT = 0x319,
        TCS_R8_G8_B8_A8_UNORM = 0x1A,
        TC_R8_G8_B8_A8_UINT = 0x11A,
        TC_R8_G8_B8_A8_SNORM = 0x21A,
        TC_R8_G8_B8_A8_SINT = 0x31A,
        TCS_R8_G8_B8_A8_SRGB = 0x41A,
        TCS_A2_B10_G10_R10_UNORM = 0x1B,
        TC_A2_B10_G10_R10_UINT = 0x11B,
        D_D32_FLOAT_S8_UINT_X24 = 0x81C,
        T_R32_FLOAT_X8_X24 = 0x81C,
        T_X32_G8_UINT_X24 = 0x11C,
        TC_R32_G32_UINT = 0x11D,
        TC_R32_G32_SINT = 0x31D,
        TC_R32_G32_FLOAT = 0x81E,
        TC_R16_G16_B16_A16_UNORM = 0x1F,
        TC_R16_G16_B16_A16_UINT = 0x11F,
        TC_R16_G16_B16_A16_SNORM = 0x21F,
        TC_R16_G16_B16_A16_SINT = 0x31F,
        TC_R16_G16_B16_A16_FLOAT = 0x820,
        TC_R32_G32_B32_A32_UINT = 0x122,
        TC_R32_G32_B32_A32_SINT = 0x322,
        TC_R32_G32_B32_A32_FLOAT = 0x823,
        T_BC1_UNORM = 0x31,
        T_BC1_SRGB = 0x431,
        T_BC2_UNORM = 0x32,
        T_BC2_SRGB = 0x432,
        T_BC3_UNORM = 0x33,
        T_BC3_SRGB = 0x433,
        T_BC4_UNORM = 0x34,
        T_BC4_SNORM = 0x234,
        T_BC5_UNORM = 0x35,
        T_BC5_SNORM = 0x235,
    }

    public enum GX2TileMode : uint
    {
        Default = 0x0,
        LinearAligned = 0x1,
        Tiled1DThin1 = 0x2,
        Tiled1DThick = 0x3,
        Tiled2DThin1 = 0x4,
        Tiled2DThin2 = 0x5,
        Tiled2DThin4 = 0x6,
        Tiled2DThick = 0x7,
        Tiled2BThin1 = 0x8,
        Tiled2BThin2 = 0x9,
        Tiled2BThin4 = 0xA,
        Tiled2BThick = 0xB,
        Tiled3DThin1 = 0xC,
        Tiled3DThick = 0xD,
        Tiled3BThin1 = 0xE,
        Tiled3BThick = 0xF,
        LinearSpecial = 0x10,
    }

    public enum GX2AAMode : uint
    {
        Mode1X = 0x0,
        Mode2X = 0x1,
        Mode4X = 0x2,
        Mode8X = 0x3,
    }

    public enum GX2SurfaceDimension : uint
    {
        Dim1D = 0x0,
        Dim2D = 0x1,
        Dim3D = 0x2,
        DimCube = 0x3,
        Dim1DArray = 0x4,
        Dim2DArray = 0x5,
        Dim2DMSAA = 0x6,
        Dim2DMSAAArray = 0x7,
    }

    public enum BlockType : uint
    {
        Invalid = 0x00,
        EndOfFile = 0x01,
        AlignData = 0x02,
        VertexShaderHeader = 0x03,
        VertexShaderProgram = 0x05,
        PixelShaderHeader = 0x06,
        PixelShaderProgram = 0x07,
        GeometryShaderHeader = 0x08,
        GeometryShaderProgram = 0x09,
        GeometryShaderProgram2 = 0x10,
        ImageInfo = 0x0B,
        ImageData = 0x0C,
        MipData = 0x0D,
        ComputeShaderHeader = 0x14,
        ComputeShader = 0x15,
        UserBlock = 0x16,
    }

    #endregion

    #region Properties

    public uint HeaderSize { get; set; }
    public uint MajorVersion { get; set; }
    public uint MinorVersion { get; set; }
    public uint GpuVersion { get; set; }
    public uint AlignMode { get; set; }

    public List<GTXDataBlock> Blocks { get; set; } = [];
    public List<GX2Surface> Textures { get; set; } = [];
    public List<byte[]> TextureData { get; set; } = [];
    public List<byte[]> MipData { get; set; } = [];

    #endregion

    #region Helper Methods

    public static int GetBPP(GX2SurfaceFormat format)
    {
        return format switch
        {
            GX2SurfaceFormat.TC_R8_UNORM or GX2SurfaceFormat.TC_R8_UINT or
            GX2SurfaceFormat.TC_R8_SNORM or GX2SurfaceFormat.TC_R8_SINT => 1,

            GX2SurfaceFormat.T_R4_G4_UNORM or GX2SurfaceFormat.TCD_R16_UNORM or
            GX2SurfaceFormat.TC_R16_UINT or GX2SurfaceFormat.TC_R16_SNORM or
            GX2SurfaceFormat.TC_R16_SINT or GX2SurfaceFormat.TC_R16_FLOAT or
            GX2SurfaceFormat.TC_R8_G8_UNORM or GX2SurfaceFormat.TC_R8_G8_UINT or
            GX2SurfaceFormat.TC_R8_G8_SNORM or GX2SurfaceFormat.TC_R8_G8_SINT or
            GX2SurfaceFormat.TCS_R5_G6_B5_UNORM or GX2SurfaceFormat.TC_R5_G5_B5_A1_UNORM or
            GX2SurfaceFormat.TC_R4_G4_B4_A4_UNORM or GX2SurfaceFormat.TC_A1_B5_G5_R5_UNORM => 2,

            GX2SurfaceFormat.TC_R32_UINT or GX2SurfaceFormat.TC_R32_SINT or
            GX2SurfaceFormat.TCD_R32_FLOAT or GX2SurfaceFormat.TC_R16_G16_UNORM or
            GX2SurfaceFormat.TC_R16_G16_UINT or GX2SurfaceFormat.TC_R16_G16_SNORM or
            GX2SurfaceFormat.TC_R16_G16_SINT or GX2SurfaceFormat.TC_R16_G16_FLOAT or
            GX2SurfaceFormat.D_D24_S8_UNORM or GX2SurfaceFormat.TC_R11_G11_B10_FLOAT or
            GX2SurfaceFormat.TCS_R10_G10_B10_A2_UNORM or GX2SurfaceFormat.TC_R10_G10_B10_A2_UINT or
            GX2SurfaceFormat.TC_R10_G10_B10_A2_SNORM or GX2SurfaceFormat.TC_R10_G10_B10_A2_SINT or
            GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM or GX2SurfaceFormat.TC_R8_G8_B8_A8_UINT or
            GX2SurfaceFormat.TC_R8_G8_B8_A8_SNORM or GX2SurfaceFormat.TC_R8_G8_B8_A8_SINT or
            GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB or GX2SurfaceFormat.TCS_A2_B10_G10_R10_UNORM or
            GX2SurfaceFormat.TC_A2_B10_G10_R10_UINT => 4,

            GX2SurfaceFormat.TC_R32_G32_UINT or GX2SurfaceFormat.TC_R32_G32_SINT or
            GX2SurfaceFormat.TC_R32_G32_FLOAT or GX2SurfaceFormat.TC_R16_G16_B16_A16_UNORM or
            GX2SurfaceFormat.TC_R16_G16_B16_A16_UINT or GX2SurfaceFormat.TC_R16_G16_B16_A16_SNORM or
            GX2SurfaceFormat.TC_R16_G16_B16_A16_SINT or GX2SurfaceFormat.TC_R16_G16_B16_A16_FLOAT => 8,

            GX2SurfaceFormat.TC_R32_G32_B32_A32_UINT or GX2SurfaceFormat.TC_R32_G32_B32_A32_SINT or
            GX2SurfaceFormat.TC_R32_G32_B32_A32_FLOAT => 16,

            // BCn formats - these return bits per pixel
            GX2SurfaceFormat.T_BC1_UNORM or GX2SurfaceFormat.T_BC1_SRGB or
            GX2SurfaceFormat.T_BC4_UNORM or GX2SurfaceFormat.T_BC4_SNORM => 8,

            GX2SurfaceFormat.T_BC2_UNORM or GX2SurfaceFormat.T_BC2_SRGB or
            GX2SurfaceFormat.T_BC3_UNORM or GX2SurfaceFormat.T_BC3_SRGB or
            GX2SurfaceFormat.T_BC5_UNORM or GX2SurfaceFormat.T_BC5_SNORM => 16,

            _ => 0
        };
    }

    public static uint GetBitsPerPixel(GX2SurfaceFormat format)
    {
        uint formatVal = (uint)format & 0x3F;
        return GX2Swizzle.FormatHwInfo[formatVal * 4];
    }

    public static bool IsFormatBCN(GX2SurfaceFormat format)
    {
        return format switch
        {
            GX2SurfaceFormat.T_BC1_UNORM or GX2SurfaceFormat.T_BC1_SRGB or
            GX2SurfaceFormat.T_BC2_UNORM or GX2SurfaceFormat.T_BC2_SRGB or
            GX2SurfaceFormat.T_BC3_UNORM or GX2SurfaceFormat.T_BC3_SRGB or
            GX2SurfaceFormat.T_BC4_UNORM or GX2SurfaceFormat.T_BC4_SNORM or
            GX2SurfaceFormat.T_BC5_UNORM or GX2SurfaceFormat.T_BC5_SNORM => true,
            _ => false
        };
    }

    public static DDSFormat ConvertGX2ToDDSFormat(GX2SurfaceFormat format)
    {
        return format switch
        {
            GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM => DDSFormat.RGBA8,
            GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB => DDSFormat.RGBA_SRGB,
            GX2SurfaceFormat.TCS_R10_G10_B10_A2_UNORM => DDSFormat.RGB10A2,
            GX2SurfaceFormat.TCS_R5_G6_B5_UNORM => DDSFormat.RGB565,
            GX2SurfaceFormat.TC_R5_G5_B5_A1_UNORM => DDSFormat.RGB5A1,
            GX2SurfaceFormat.TC_R4_G4_B4_A4_UNORM => DDSFormat.RGBA4,
            GX2SurfaceFormat.TC_R8_UNORM => DDSFormat.L8,
            GX2SurfaceFormat.TC_R8_G8_UNORM => DDSFormat.LA8,
            GX2SurfaceFormat.T_BC1_UNORM or GX2SurfaceFormat.T_BC1_SRGB => DDSFormat.BC1,
            GX2SurfaceFormat.T_BC2_UNORM or GX2SurfaceFormat.T_BC2_SRGB => DDSFormat.BC2,
            GX2SurfaceFormat.T_BC3_UNORM or GX2SurfaceFormat.T_BC3_SRGB => DDSFormat.BC3,
            GX2SurfaceFormat.T_BC4_UNORM => DDSFormat.BC4U,
            GX2SurfaceFormat.T_BC4_SNORM => DDSFormat.BC4S,
            GX2SurfaceFormat.T_BC5_UNORM => DDSFormat.BC5U,
            GX2SurfaceFormat.T_BC5_SNORM => DDSFormat.BC5S,
            _ => throw new NotSupportedException($"Format {format} is not supported for DDS conversion.")
        };
    }

    public static GX2SurfaceFormat ConvertDDSToGX2Format(DDSFormat format)
    {
        return format switch
        {
            DDSFormat.RGBA8 => GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM,
            DDSFormat.RGBA_SRGB => GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB,
            DDSFormat.RGB10A2 => GX2SurfaceFormat.TCS_R10_G10_B10_A2_UNORM,
            DDSFormat.RGB565 => GX2SurfaceFormat.TCS_R5_G6_B5_UNORM,
            DDSFormat.RGB5A1 => GX2SurfaceFormat.TC_R5_G5_B5_A1_UNORM,
            DDSFormat.RGBA4 => GX2SurfaceFormat.TC_R4_G4_B4_A4_UNORM,
            DDSFormat.L8 => GX2SurfaceFormat.TC_R8_UNORM,
            DDSFormat.LA8 => GX2SurfaceFormat.TC_R8_G8_UNORM,
            DDSFormat.BC1 => GX2SurfaceFormat.T_BC1_UNORM,
            DDSFormat.BC2 => GX2SurfaceFormat.T_BC2_UNORM,
            DDSFormat.BC3 => GX2SurfaceFormat.T_BC3_UNORM,
            DDSFormat.BC4U => GX2SurfaceFormat.T_BC4_UNORM,
            DDSFormat.BC4S => GX2SurfaceFormat.T_BC4_SNORM,
            DDSFormat.BC5U => GX2SurfaceFormat.T_BC5_UNORM,
            DDSFormat.BC5S => GX2SurfaceFormat.T_BC5_SNORM,
            _ => throw new NotSupportedException($"Format {format} is not supported for GX2 conversion.")
        };
    }

    #endregion

    #region File Loading

    public void LoadFile(Stream data)
    {
        Blocks = [];
        Textures = [];
        TextureData = [];
        MipData = [];

        EndianBinaryReader reader = new(data, true); // GTX is big endian

        string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (signature != Magic)
            throw new InvalidDataException($"Invalid GTX signature: {signature}. Expected: {Magic}");

        HeaderSize = reader.ReadUInt32();
        MajorVersion = reader.ReadUInt32();
        MinorVersion = reader.ReadUInt32();
        GpuVersion = reader.ReadUInt32();
        AlignMode = reader.ReadUInt32();

        // Determine block types based on version
        uint surfBlockType, dataBlockType, mipBlockType;
        if (MajorVersion == 6 && MinorVersion == 0)
        {
            surfBlockType = 0x0A;
            dataBlockType = 0x0B;
            mipBlockType = 0x0C;
        }
        else if (MajorVersion == 6 || MajorVersion == 7)
        {
            surfBlockType = 0x0B;
            dataBlockType = 0x0C;
            mipBlockType = 0x0D;
        }
        else
        {
            throw new NotSupportedException($"Unsupported GTX version: {MajorVersion}.{MinorVersion}");
        }

        if (GpuVersion != 2)
            throw new NotSupportedException($"Unsupported GPU version: {GpuVersion}");

        data.Seek(HeaderSize, SeekOrigin.Begin);

        while (data.Position < data.Length)
        {
            GTXDataBlock block = new();
            block.Read(reader);
            Blocks.Add(block);

            bool isEmptyBlock = block.BlockType == BlockType.AlignData ||
                               block.BlockType == BlockType.EndOfFile;

            uint blockTypeValue = (uint)block.BlockType;

            if (blockTypeValue == surfBlockType)
            {
                GX2Surface surface = new();
                using MemoryStream ms = new(block.Data);
                surface.Read(new EndianBinaryReader(ms, true));
                Textures.Add(surface);
            }
            else if (blockTypeValue == dataBlockType)
            {
                TextureData.Add(block.Data);
            }
            else if (blockTypeValue == mipBlockType)
            {
                MipData.Add(block.Data);
            }
        }

        // Assign data to textures
        for (int i = 0; i < Textures.Count && i < TextureData.Count; i++)
        {
            Textures[i].Data = TextureData[i];
            Textures[i].Bpp = GetBitsPerPixel(Textures[i].Format) / 8;

            if (Textures[i].NumMips > 1 && i < MipData.Count)
            {
                Textures[i].MipData = MipData[i];
            }
            else
            {
                Textures[i].MipData = [];
            }
        }
    }

    public Image<Bgra32> ConvertToImage(int textureIndex = 0)
    {
        if (textureIndex < 0 || textureIndex >= Textures.Count)
            throw new ArgumentOutOfRangeException(nameof(textureIndex));

        GX2Surface texture = Textures[textureIndex];
        byte[] decodedData = DecodeTexture(texture);

        DDSFormat ddsFormat = ConvertGX2ToDDSFormat(texture.Format);
        (uint compR, uint compG, uint compB, uint compA) = GetCompSel(texture.Format);

        byte[] ddsHeader = DDS.GenerateHeader(
            texture.NumMips,
            texture.Width,
            texture.Height,
            ddsFormat,
            (compR, compG, compB, compA),
            (uint)decodedData.Length);

        byte[] fullDds = [.. ddsHeader, .. decodedData];

        using MemoryStream ms = new(fullDds);
        return DDS.GetImage(ms);
    }

    public static Image<Bgra32> GetImage(Stream data)
    {
        GTX gtx = new();
        gtx.LoadFile(data);
        return gtx.ConvertToImage();
    }

    private static (uint, uint, uint, uint) GetCompSel(GX2SurfaceFormat format)
    {
        return format switch
        {
            GX2SurfaceFormat.TC_R8_UNORM => (0, 0, 0, 5),
            GX2SurfaceFormat.TC_R8_G8_UNORM => (0, 0, 0, 1),
            GX2SurfaceFormat.TCS_R5_G6_B5_UNORM => (0, 1, 2, 5),
            _ => (0, 1, 2, 3),
        };
    }

    private static byte[] DecodeTexture(GX2Surface texture)
    {
        uint blkWidth = IsFormatBCN(texture.Format) ? 4u : 1u;
        uint blkHeight = IsFormatBCN(texture.Format) ? 4u : 1u;

        int bpp = GetBPP(texture.Format);
        DDSFormat ddsFormat = ConvertGX2ToDDSFormat(texture.Format);

        List<byte> result = [];

        for (int mipLevel = 0; mipLevel < Math.Max(1, texture.NumMips); mipLevel++)
        {
            uint width = Math.Max(1, texture.Width >> mipLevel);
            uint height = Math.Max(1, texture.Height >> mipLevel);

            int linearSize = BCnFormats.Contains(ddsFormat)
                ? (int)(((width + 3) >> 2) * ((height + 3) >> 2) * bpp)
                : (int)(width * height * bpp);

            byte[] deswizzled = GX2Swizzle.Deswizzle(texture, 0, mipLevel);

            // Take only the linear size from the deswizzled data
            result.AddRange(deswizzled.Take(linearSize));
        }

        return [.. result];
    }

    #endregion

    #region File Writing

    public static void ConvertToFile(Image<Bgra32> image, GX2SurfaceFormat format, Stream output)
    {
        uint width = (uint)image.Width;
        uint height = (uint)image.Height;

        byte[] imageData = ConvertImageDataToFormat(image, format);
        GX2Surface surface = CreateGX2Surface(width, height, format, imageData);

        WriteGTXFile(output, surface);
    }

    private static GX2Surface CreateGX2Surface(uint width, uint height, GX2SurfaceFormat format, byte[] data)
    {
        GX2Surface surface = new()
        {
            Dim = GX2SurfaceDimension.Dim2D,
            Width = width,
            Height = height,
            Depth = 1,
            NumMips = 1,
            Format = format,
            AA = GX2AAMode.Mode1X,
            Use = 1,
            TileMode = GX2TileMode.LinearAligned,
            Swizzle = 0,
            Alignment = 4096,
            Pitch = 0,
            Bpp = GetBitsPerPixel(format) / 8,
            MipOffsets = new uint[13],
            FirstMip = 0,
            ImageCount = 1,
            FirstSlice = 0,
            NumSlices = 1,
            CompSel = [0, 1, 2, 3],
            TexRegs = new uint[5],
        };

        // Calculate surface info to get proper pitch alignment
        var surfOut = GX2Swizzle.GetSurfaceInfo(format, width, height, 1, (uint)surface.Dim, (uint)surface.TileMode, (uint)surface.AA, 0);
        surface.ImageSize = (uint)surfOut.SurfSize;
        surface.Pitch = surfOut.Pitch;

        // Linear modes don't need swizzle
        surface.Swizzle = 0;

        // Layout data according to the aligned pitch
        uint bpp = GetBitsPerPixel(format) / 8;
        uint srcRowBytes = width * bpp;
        uint dstRowBytes = surface.Pitch * bpp;

        byte[] paddedData = new byte[surface.ImageSize];
        
        for (uint y = 0; y < height; y++)
        {
            uint srcOffset = y * srcRowBytes;
            uint dstOffset = y * dstRowBytes;
            
            if (srcOffset + srcRowBytes <= data.Length && dstOffset + srcRowBytes <= paddedData.Length)
            {
                Array.Copy(data, srcOffset, paddedData, dstOffset, srcRowBytes);
            }
        }

        surface.Data = paddedData;
        surface.MipData = [];

        return surface;
    }

    private static void WriteGTXFile(Stream output, GX2Surface surface)
    {
        using EndianBinaryWriter writer = new(output, Encoding.Default, true, true);

        // Write header
        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(32U); // HeaderSize
        writer.Write(7U);  // MajorVersion
        writer.Write(1U);  // MinorVersion
        writer.Write(2U);  // GpuVersion
        writer.Write(0U);  // AlignMode

        // Padding to header size
        for (int i = (int)output.Position; i < 32; i++)
            writer.Write((byte)0);

        // Write surface info block
        WriteSurfaceBlock(writer, surface);

        // Write data block
        WriteDataBlock(writer, surface.Data);

        // Write EOF block
        WriteEOFBlock(writer);
    }

    private static void WriteSurfaceBlock(EndianBinaryWriter writer, GX2Surface surface)
    {
        MemoryStream surfData = new();
        using (EndianBinaryWriter surfWriter = new(surfData, Encoding.Default, true, true))
        {
            surface.Write(surfWriter);
        }

        byte[] data = surfData.ToArray();

        writer.Write(Encoding.ASCII.GetBytes(BlockMagic));
        writer.Write(32U); // HeaderSize
        writer.Write(1U);  // MajorVersion
        writer.Write(0U);  // MinorVersion
        writer.Write(0x0BU); // BlockType (ImageInfo for version 7)
        writer.Write((uint)data.Length);
        writer.Write(0U);  // Identifier
        writer.Write(0U);  // Index

        writer.Write(data);
    }

    private static void WriteDataBlock(EndianBinaryWriter writer, byte[] data)
    {
        // Write align block first
        uint alignment = 4096;
        uint currentPos = (uint)((MemoryStream)writer.BaseStream).Position;
        uint alignSize = GetAlignBlockSize(currentPos + 32, alignment);

        if (alignSize > 0)
        {
            writer.Write(Encoding.ASCII.GetBytes(BlockMagic));
            writer.Write(32U);
            writer.Write(1U);
            writer.Write(0U);
            writer.Write((uint)BlockType.AlignData);
            writer.Write(alignSize);
            writer.Write(0U);
            writer.Write(0U);

            for (uint i = 0; i < alignSize; i++)
                writer.Write((byte)0);
        }

        // Write data block
        writer.Write(Encoding.ASCII.GetBytes(BlockMagic));
        writer.Write(32U);
        writer.Write(1U);
        writer.Write(0U);
        writer.Write(0x0CU); // BlockType (ImageData for version 7)
        writer.Write((uint)data.Length);
        writer.Write(0U);
        writer.Write(0U);

        writer.Write(data);
    }

    private static void WriteEOFBlock(EndianBinaryWriter writer)
    {
        writer.Write(Encoding.ASCII.GetBytes(BlockMagic));
        writer.Write(32U);
        writer.Write(1U);
        writer.Write(0U);
        writer.Write((uint)BlockType.EndOfFile);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
    }

    private static uint GetAlignBlockSize(uint dataOffset, uint alignment)
    {
        uint alignedOffset = (dataOffset + alignment - 1) & ~(alignment - 1);
        if (alignedOffset <= dataOffset + 32)
            return 0;
        return alignedOffset - dataOffset - 32;
    }

    private static byte[] ConvertImageDataToFormat(Image<Bgra32> image, GX2SurfaceFormat format)
    {
        return format switch
        {
            GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM or GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB => ConvertToRGBA8(image),
            GX2SurfaceFormat.TCS_R5_G6_B5_UNORM => ConvertToRGB565(image),
            GX2SurfaceFormat.TC_R5_G5_B5_A1_UNORM => ConvertToRGB5A1(image),
            GX2SurfaceFormat.TC_R4_G4_B4_A4_UNORM => ConvertToRGBA4(image),
            GX2SurfaceFormat.TC_R8_UNORM => ConvertToR8(image),
            GX2SurfaceFormat.TC_R8_G8_UNORM => ConvertToRG8(image),
            _ => throw new NotImplementedException($"Format {format} is not supported for export.")
        };
    }

    private static byte[] ConvertToRGBA8(Image<Bgra32> image)
    {
        // Write as BGRA to match DDS mask expectations when read as little-endian uint
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

    private static byte[] ConvertToRGB565(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 3) & 0x1F;
                    uint g = (uint)(row[x].G >> 2) & 0x3F;
                    uint b = (uint)(row[x].B >> 3) & 0x1F;
                    ushort packed = (ushort)((r << 11) | (g << 5) | b);
                    // Write little-endian for DDS compatibility
                    byte[] bytes = BitConverter.GetBytes(packed);
                    Array.Copy(bytes, 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRGB5A1(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 3) & 0x1F;
                    uint g = (uint)(row[x].G >> 3) & 0x1F;
                    uint b = (uint)(row[x].B >> 3) & 0x1F;
                    uint a = (row[x].A > 128 ? 1U : 0U) & 0x1;
                    ushort packed = (ushort)((a << 15) | (r << 10) | (g << 5) | b);
                    // Write little-endian for DDS compatibility
                    byte[] bytes = BitConverter.GetBytes(packed);
                    Array.Copy(bytes, 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRGBA4(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 4) & 0xF;
                    uint g = (uint)(row[x].G >> 4) & 0xF;
                    uint b = (uint)(row[x].B >> 4) & 0xF;
                    uint a = (uint)(row[x].A >> 4) & 0xF;
                    ushort packed = (ushort)((r << 12) | (g << 8) | (b << 4) | a);
                    // Write little-endian for DDS compatibility
                    byte[] bytes = BitConverter.GetBytes(packed);
                    Array.Copy(bytes, 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToR8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = (byte)(0.299f * row[x].R + 0.587f * row[x].G + 0.114f * row[x].B);
                }
            }
        });
        return result;
    }

    private static byte[] ConvertToRG8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = (byte)(0.299f * row[x].R + 0.587f * row[x].G + 0.114f * row[x].B);
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }

    #endregion

    #region Data Structures

    public class GTXDataBlock
    {
        public uint HeaderSize { get; set; }
        public uint MajorVersion { get; set; }
        public uint MinorVersion { get; set; }
        public BlockType BlockType { get; set; }
        public uint DataSize { get; set; }
        public uint Identifier { get; set; }
        public uint Index { get; set; }
        public byte[] Data { get; set; } = [];

        public void Read(EndianBinaryReader reader)
        {
            long blockStart = reader.BaseStream.Position;

            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != BlockMagic)
                throw new InvalidDataException($"Invalid block signature: {signature}. Expected: {BlockMagic}");

            HeaderSize = reader.ReadUInt32();
            MajorVersion = reader.ReadUInt32();
            MinorVersion = reader.ReadUInt32();
            BlockType = (BlockType)reader.ReadUInt32();
            DataSize = reader.ReadUInt32();
            Identifier = reader.ReadUInt32();
            Index = reader.ReadUInt32();

            reader.BaseStream.Seek(blockStart + HeaderSize, SeekOrigin.Begin);
            Data = reader.ReadBytes((int)DataSize);
        }

        public void Write(EndianBinaryWriter writer)
        {
            long blockStart = writer.BaseStream.Position;

            writer.Write(Encoding.ASCII.GetBytes(BlockMagic));
            writer.Write(HeaderSize);
            writer.Write(MajorVersion);
            writer.Write(MinorVersion);
            writer.Write((uint)BlockType);
            writer.Write((uint)Data.Length);
            writer.Write(Identifier);
            writer.Write(Index);

            writer.BaseStream.Seek(blockStart + HeaderSize, SeekOrigin.Begin);
            writer.Write(Data);
        }
    }

    public class GX2Surface
    {
        public GX2SurfaceDimension Dim { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint Depth { get; set; }
        public uint NumMips { get; set; }
        public GX2SurfaceFormat Format { get; set; }
        public GX2AAMode AA { get; set; }
        public uint Use { get; set; }
        public uint ImageSize { get; set; }
        public uint ImagePtr { get; set; }
        public uint MipSize { get; set; }
        public uint MipPtr { get; set; }
        public GX2TileMode TileMode { get; set; }
        public uint Swizzle { get; set; }
        public uint Alignment { get; set; }
        public uint Pitch { get; set; }
        public uint[] MipOffsets { get; set; } = new uint[13];
        public uint FirstMip { get; set; }
        public uint ImageCount { get; set; }
        public uint FirstSlice { get; set; }
        public uint NumSlices { get; set; }
        public byte[] CompSel { get; set; } = new byte[4];
        public uint[] TexRegs { get; set; } = new uint[5];

        // Runtime data (not stored in file)
        public byte[] Data { get; set; } = [];
        public byte[] MipData { get; set; } = [];
        public uint Bpp { get; set; }

        public void Read(EndianBinaryReader reader)
        {
            Dim = (GX2SurfaceDimension)reader.ReadUInt32();
            Width = reader.ReadUInt32();
            Height = reader.ReadUInt32();
            Depth = reader.ReadUInt32();
            NumMips = reader.ReadUInt32();
            Format = (GX2SurfaceFormat)reader.ReadUInt32();
            AA = (GX2AAMode)reader.ReadUInt32();
            Use = reader.ReadUInt32();
            ImageSize = reader.ReadUInt32();
            ImagePtr = reader.ReadUInt32();
            MipSize = reader.ReadUInt32();
            MipPtr = reader.ReadUInt32();
            TileMode = (GX2TileMode)reader.ReadUInt32();
            Swizzle = reader.ReadUInt32();
            Alignment = reader.ReadUInt32();
            Pitch = reader.ReadUInt32();

            for (int i = 0; i < 13; i++)
                MipOffsets[i] = reader.ReadUInt32();

            FirstMip = reader.ReadUInt32();
            ImageCount = reader.ReadUInt32();
            FirstSlice = reader.ReadUInt32();
            NumSlices = reader.ReadUInt32();

            for (int i = 0; i < 4; i++)
                CompSel[i] = reader.ReadByte();

            for (int i = 0; i < 5; i++)
                TexRegs[i] = reader.ReadUInt32();
        }

        public void Write(EndianBinaryWriter writer)
        {
            writer.Write((uint)Dim);
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(Depth);
            writer.Write(NumMips);
            writer.Write((uint)Format);
            writer.Write((uint)AA);
            writer.Write(Use);
            writer.Write(ImageSize);
            writer.Write(ImagePtr);
            writer.Write(MipSize);
            writer.Write(MipPtr);
            writer.Write((uint)TileMode);
            writer.Write(Swizzle);
            writer.Write(Alignment);
            writer.Write(Pitch);

            for (int i = 0; i < 13; i++)
            {
                if (MipOffsets != null && i < MipOffsets.Length)
                    writer.Write(MipOffsets[i]);
                else
                    writer.Write(0U);
            }

            writer.Write(FirstMip);
            writer.Write(ImageCount);
            writer.Write(FirstSlice);
            writer.Write(NumSlices);

            for (int i = 0; i < 4; i++)
            {
                if (CompSel != null && i < CompSel.Length)
                    writer.Write(CompSel[i]);
                else
                    writer.Write((byte)0);
            }

            for (int i = 0; i < 5; i++)
            {
                if (TexRegs != null && i < TexRegs.Length)
                    writer.Write(TexRegs[i]);
                else
                    writer.Write(0U);
            }
        }
    }

    #endregion
}
