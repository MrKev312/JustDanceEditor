using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

using System.Text;
using static SwitchTexture.TextureType.XTX;
using static SwitchTexture.TextureType.DDS;

namespace SwitchTexture.TextureType;

public class GTX
{
    public enum GX2SurfaceFormat : uint
    {
        GX2_SURFACE_FORMAT_INVALID = 0x000,
        GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_UNORM = 0x01a,
        GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_SRGB = 0x041a,
        GX2_SURFACE_FORMAT_TCS_R10_G10_B10_A2_UNORM = 0x019,
        GX2_SURFACE_FORMAT_TCS_R5_G6_B5_UNORM = 0x008,
        GX2_SURFACE_FORMAT_TC_R5_G5_B5_A1_UNORM = 0x00a,
        GX2_SURFACE_FORMAT_TC_R4_G4_B4_A4_UNORM = 0x00b,
        GX2_SURFACE_FORMAT_TC_R8_UNORM = 0x001,
        GX2_SURFACE_FORMAT_TC_R8_G8_UNORM = 0x007,
        GX2_SURFACE_FORMAT_TC_R4_G4_UNORM = 0x002,
        GX2_SURFACE_FORMAT_T_BC1_UNORM = 0x031,
        GX2_SURFACE_FORMAT_T_BC1_SRGB = 0x431,
        GX2_SURFACE_FORMAT_T_BC2_UNORM = 0x032,
        GX2_SURFACE_FORMAT_T_BC2_SRGB = 0x432,
        GX2_SURFACE_FORMAT_T_BC3_UNORM = 0x033,
        GX2_SURFACE_FORMAT_T_BC3_SRGB = 0x433,
        GX2_SURFACE_FORMAT_T_BC4_UNORM = 0x034,
        GX2_SURFACE_FORMAT_T_BC4_SNORM = 0x234,
        GX2_SURFACE_FORMAT_T_BC5_UNORM = 0x035,
        GX2_SURFACE_FORMAT_T_BC5_SNORM = 0x235
    }

    public static readonly GX2SurfaceFormat[] BCnFormats =
    [
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC1_UNORM,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC1_SRGB,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC2_UNORM,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC2_SRGB,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC3_UNORM,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC3_SRGB,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC4_UNORM,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC4_SNORM,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC5_UNORM,
        GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC5_SNORM
    ];

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
        UserDataBlock = 0x10,
        SurfaceInfo = 0x0A,
        SurfaceData = 0x0B,
        MipData2 = 0x0C,
        ImageInfo = 0x11,
        ImageData = 0x12,
        MipData = 0x13,
        ComputeShaderHeader = 0x14,
        ComputeShader = 0x15,
        UserBlock = 0x16,
    }

    static readonly uint[] formatHwInfo = [
            0x00, 0x00, 0x00, 0x01, 0x08, 0x03, 0x00, 0x01, 0x08, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x10, 0x07, 0x00, 0x00, 0x10, 0x03, 0x00, 0x01, 0x10, 0x03, 0x00, 0x01,
            0x10, 0x0B, 0x00, 0x01, 0x10, 0x01, 0x00, 0x01, 0x10, 0x03, 0x00, 0x01, 0x10, 0x03, 0x00, 0x01,
            0x10, 0x03, 0x00, 0x01, 0x20, 0x03, 0x00, 0x00, 0x20, 0x07, 0x00, 0x00, 0x20, 0x03, 0x00, 0x00,
            0x20, 0x03, 0x00, 0x01, 0x20, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x03, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x20, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x01, 0x20, 0x0B, 0x00, 0x01, 0x20, 0x0B, 0x00, 0x01, 0x20, 0x0B, 0x00, 0x01,
            0x40, 0x05, 0x00, 0x00, 0x40, 0x03, 0x00, 0x00, 0x40, 0x03, 0x00, 0x00, 0x40, 0x03, 0x00, 0x00,
            0x40, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x80, 0x03, 0x00, 0x00, 0x80, 0x03, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x10, 0x01, 0x00, 0x00,
            0x10, 0x01, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00,
            0x00, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x60, 0x01, 0x00, 0x00,
            0x60, 0x01, 0x00, 0x00, 0x40, 0x01, 0x00, 0x01, 0x80, 0x01, 0x00, 0x01, 0x80, 0x01, 0x00, 0x01,
            0x40, 0x01, 0x00, 0x01, 0x80, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

    public static uint GetBPP(GX2SurfaceFormat format)
    {
        return ((formatHwInfo[((int)format & 0x3F) * 4] - 1) | 7) + 1;
    }

    private GTXHeader? header = null;


    public List<GX2Surface> GTXSurfaces { get; private set; } = [];
    public List<byte[]> ImageDatas { get; private set; } = [];
    public Dictionary<uint, byte[]> MipDatas { get; private set; } = [];
    public List<GTXDataBlock> Blocks { get; private set; } = [];

    public void LoadFile(Stream data)
    {
        using BinaryReader reader = new(data, Encoding.Default, true);
        header = new GTXHeader(reader);

        bool shiftedType;
        if (header.MajorVersion == 6 && header.MinorVersion == 0)
            shiftedType = true;
        else if (header.MajorVersion is 6 or 7)
            shiftedType = false;
        else
            throw new Exception($"Unsupported GTX version {header.MajorVersion}");

        if (header.GpuVersion != 2)
            throw new Exception($"Unsupported GPU version {header.GpuVersion}");

        bool blockB = false;
        bool blockC = false;

        uint imageInfo = 0;
        uint images = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            GTXDataBlock block = new(reader);
            Blocks.Add(block);

            bool blockIsEmpty = block.BlockType is BlockType.AlignData or BlockType.EndOfFile;

            switch (block.BlockType)
            {
                case BlockType.SurfaceInfo:
                    {
                        imageInfo++;
                        blockB = true;

                        MemoryStream stream = new(block.Data);
                        BinaryReader dataReader = new(stream);
                        GX2Surface surface = new(dataReader);

                        if (surface.TileMode is 0 or > 16)
                            throw new Exception($"Invalid tileMode {surface.TileMode}!");

                        if (surface.MipCount > 14)
                            throw new Exception($"Invalid number of mip maps {surface.MipCount}!");

                        GTXSurfaces.Add(surface);
                        break;
                    }
                case BlockType.SurfaceData:
                    {
                        images++;
                        blockC = true;

                        ImageDatas.Add(block.Data);

                        break;
                    }
                case BlockType.MipData2:
                    {
                        if (!blockC)
                            throw new Exception("MipData2 block without SurfaceData block!");

                        MipDatas.Add(images - 1, block.Data);

                        break;
                    }
                default:
                    break;
            }
        }

        if (imageInfo != images)
            throw new Exception("Number of imageInfo blocks does not match number of image blocks!");

        if (!blockB || !blockC)
            throw new Exception("Missing SurfaceInfo or SurfaceData block!");
    }

    public static Image<Bgra32> GetImage(string inputPath)
    {
        GTX gtx = new();
        using FileStream fs = new(inputPath, FileMode.Open);
        gtx.LoadFile(fs);

        return null;
    }

    public (byte[][] data, byte[] hdr) DeswizzleData(int i)
    {
        GX2Surface texInfo = GTXSurfaces[i];
        byte[] data = ImageDatas[i];

        // Try to get the mip data, else empty byte array
        byte[] mipData = MipDatas.TryGetValue((uint)i, out byte[]? mip) ? mip : [];

        if (texInfo.AA != 1)
            throw new Exception("Unsupported AA value!");

        if (texInfo.Format == GX2SurfaceFormat.GX2_SURFACE_FORMAT_INVALID)
            throw new Exception("Invalid format!");

        DDSFormat ddsFormat = texInfo.Format switch
        {
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_UNORM => DDSFormat.RGBA8,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_SRGB => DDSFormat.RGBA_SRGB,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R10_G10_B10_A2_UNORM => DDSFormat.RGB10A2,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R5_G6_B5_UNORM => DDSFormat.RGB565,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R5_G5_B5_A1_UNORM => DDSFormat.RGB5A1,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R4_G4_B4_A4_UNORM => DDSFormat.RGBA4,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R8_UNORM => DDSFormat.L8,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R8_G8_UNORM => DDSFormat.LA8,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R4_G4_UNORM => DDSFormat.LA4,
            // BCn formats
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC1_UNORM => DDSFormat.BC1,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC1_SRGB => DDSFormat.BC1,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC2_UNORM => DDSFormat.BC2,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC2_SRGB => DDSFormat.BC2,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC3_UNORM => DDSFormat.BC3,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC3_SRGB => DDSFormat.BC3,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC4_UNORM => DDSFormat.BC4U,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC4_SNORM => DDSFormat.BC4S,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC5_UNORM => DDSFormat.BC5U,
            GX2SurfaceFormat.GX2_SURFACE_FORMAT_T_BC5_SNORM => DDSFormat.BC5S,
            
            _ => throw new Exception("Unsupported format!")
        };

        throw new NotImplementedException();
    }

    public class GTXHeader
    {
        public uint HeaderSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint GpuVersion;
        public uint AlignMode;

        public GTXHeader(BinaryReader reader)
        {
            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != "Gfx2")
                throw new Exception($"Invalid signature {signature}! Expected Gfx2.");

            HeaderSize = reader.ReadUInt32();
            MajorVersion = reader.ReadUInt32();
            MinorVersion = reader.ReadUInt32();
            GpuVersion = reader.ReadUInt32();
            AlignMode = reader.ReadUInt32();

            reader.BaseStream.Seek(HeaderSize, SeekOrigin.Begin);
        }
    }

    public class GTXDataBlock
    {
        public uint HeaderSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public BlockType BlockType;
        public uint Identifier;
        public uint Index;
        public uint DataSize;
        public byte[] Data = [];

        public GTXDataBlock(BinaryReader reader, bool shiftedType = false)
        {
            long pos = reader.BaseStream.Position;

            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != "BLK{")
                throw new Exception($"Invalid signature {signature}! Expected BLK{{.");

            HeaderSize = reader.ReadUInt32();
            MajorVersion = reader.ReadUInt32();
            MinorVersion = reader.ReadUInt32();
            uint blockType = reader.ReadUInt32();
            BlockType = blockType >= 0x0B && blockType <= 0x0D && shiftedType 
                ? (BlockType)(blockType - 1) 
                : (BlockType)blockType;

            Identifier = reader.ReadUInt32();
            Index = reader.ReadUInt32();
            DataSize = reader.ReadUInt32();

            reader.BaseStream.Seek(pos + HeaderSize, SeekOrigin.Begin);
            Data = reader.ReadBytes((int)DataSize);
        }
    }

    public class GX2Surface
    {
        public uint Dim { get; private set; }
        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public uint Depth { get; private set; }
        public uint MipCount { get; private set; }
        public GX2SurfaceFormat Format { get; private set; }
        public uint AA { get; private set; }
        public uint Use { get; private set; }
        public uint ImageSize { get; private set; }
        public uint ImagePtr { get; private set; }
        public uint MipSize { get; private set; }
        public uint MipPtr { get; private set; }
        public uint TileMode { get; private set; }
        public uint Swizzle { get; private set; }
        public uint Alignment { get; private set; }
        public uint Pitch { get; private set; }

        public uint[] MipOffsets { get; private set; }
        public uint RealSize { get; private set; }

        public (uint, uint, uint, uint) CompSel { get; private set; }

        public GX2Surface(BinaryReader reader)
        {
            Dim = reader.ReadUInt32();
            Width = reader.ReadUInt32();
            Height = reader.ReadUInt32();
            Depth = reader.ReadUInt32();
            MipCount = reader.ReadUInt32();
            Format = (GX2SurfaceFormat)reader.ReadUInt32();
            AA = reader.ReadUInt32();
            Use = reader.ReadUInt32();
            ImageSize = reader.ReadUInt32();
            ImagePtr = reader.ReadUInt32();
            MipSize = reader.ReadUInt32();
            MipPtr = reader.ReadUInt32();
            TileMode = reader.ReadUInt32();
            Swizzle = reader.ReadUInt32();
            Alignment = reader.ReadUInt32();
            Pitch = reader.ReadUInt32();

            long pos = reader.BaseStream.Position;

            MipOffsets = new uint[13];
            for (int i = 0; i < 13; i++)
            {
                // Read uint32 
                byte[] bytes = reader.ReadBytes(4);
                // Reverse the bytes
                Array.Reverse(bytes);
                // Convert the reversed bytes to uint32
                MipOffsets[i] = BitConverter.ToUInt32(bytes);
            }

            pos += 0x44;
            reader.BaseStream.Seek(pos, SeekOrigin.Begin);

            CompSel = Format switch
            {
                GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R5_G5_B5_A1_UNORM or
                    GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R4_G4_B4_A4_UNORM or
                    GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R10_G10_B10_A2_UNORM or
                    GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_UNORM or
                    GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R8_G8_B8_A8_SRGB => (0, 1, 2, 3),
                GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R4_G4_UNORM or
                    GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R8_G8_UNORM => (0, 5, 5, 1),
                GX2SurfaceFormat.GX2_SURFACE_FORMAT_TC_R8_UNORM => (0, 5, 5, 5),
                GX2SurfaceFormat.GX2_SURFACE_FORMAT_TCS_R5_G6_B5_UNORM => (0, 1, 2, 5),
                _ => BCnFormats.Contains(Format) 
                ? (0u, 1u, 2u, 3u) 
                : ( reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte(),
                    reader.ReadByte())
            };
            pos += 0x18;
            reader.BaseStream.Seek(pos, SeekOrigin.Begin);

            RealSize = BCnFormats.Contains(Format)
                ? ((Width - 1) | 0x3) * ((Height - 1) | 0x3) * GetBPP(Format) / 8
                : Width * Height * GetBPP(Format) / 8;
        }
    }
}
