using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

using System.Text;
using static TextureConverter.TextureType.DDS;
using static TextureConverter.TextureType.GTX;

namespace TextureConverter.TextureType;

/** To anyone reading this, I'm sorry for the mess.
 * GTX files for some reason are just crazy to work with.
 * If you're so brave to try and understand this, good luck.
 * And I truly am sorry for the pain you're about to endure.
 */
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

    static readonly uint[] formatExInfo = [
        0x00, 0x01, 0x01, 0x03, 0x08, 0x01, 0x01, 0x03, 0x08, 0x01, 0x01, 0x03, 0x08, 0x01, 0x01, 0x03,
        0x00, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03,
        0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03, 0x10, 0x01, 0x01, 0x03,
        0x10, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
        0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
        0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
        0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
        0x40, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03,
        0x40, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x80, 0x01, 0x01, 0x03, 0x80, 0x01, 0x01, 0x03,
        0x00, 0x01, 0x01, 0x03, 0x01, 0x08, 0x01, 0x05, 0x01, 0x08, 0x01, 0x06, 0x10, 0x01, 0x01, 0x07,
        0x10, 0x01, 0x01, 0x08, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03, 0x20, 0x01, 0x01, 0x03,
        0x18, 0x03, 0x01, 0x04, 0x30, 0x03, 0x01, 0x04, 0x30, 0x03, 0x01, 0x04, 0x60, 0x03, 0x01, 0x04,
        0x60, 0x03, 0x01, 0x04, 0x40, 0x04, 0x04, 0x09, 0x80, 0x04, 0x04, 0x0A, 0x80, 0x04, 0x04, 0x0B,
        0x40, 0x04, 0x04, 0x0C, 0x40, 0x04, 0x04, 0x0D, 0x40, 0x04, 0x04, 0x0D, 0x40, 0x04, 0x04, 0x0D,
        0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03,
        0x00, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03, 0x40, 0x01, 0x01, 0x03, 0x00, 0x01, 0x01, 0x03,
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

        bool shiftedType = header.MajorVersion switch
        {
            6 when header.MinorVersion == 0 => true,
            6 or 7 => false,
            _ => throw new Exception($"Unsupported GTX version {header.MajorVersion}"),
        };

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

        throw new NotImplementedException();
    }

    SurfaceIn pIn = new();
    SurfaceOut pOut = new();

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

    public SurfaceOut GetSurfaceInfo(
        GX2SurfaceFormat surfaceFormat,
        uint surfaceWidth,
        uint surfaceHeight,
        uint surfaceDepth,
        uint surfaceDim,
        uint surfaceTileMode,
        uint surfaceAA,
        int level)
    {
        uint dim = 0;
        uint width = 0;
        uint blockSize = 0;
        uint numSamples = 0;
        uint hwFormat = 0;

        SurfaceIn aSurfIn = new();
        SurfaceOut pSurfOut = new();

        hwFormat = (uint)((int)surfaceFormat & 0x3F);
        if (surfaceTileMode == 16)
        {
            numSamples = (uint)(1 << (int)surfaceAA);

            blockSize = (uint)(hwFormat is < 0x31 or > 0x35 
                ? 1 
                : 4);

            width = ~(blockSize - 1) & (Math.Max(1, surfaceWidth >> level) + blockSize - 1);

            pSurfOut.Bpp = formatHwInfo[hwFormat * 4];
            pSurfOut.Size = 96;
            pSurfOut.Pitch = width / blockSize;
            pSurfOut.PixelBits = formatHwInfo[hwFormat * 4];
            pSurfOut.BaseAlign = 1;
            pSurfOut.PitchAlign = 1;
            pSurfOut.HeightAlign = 1;
            pSurfOut.DepthAlign = 1;
            dim = surfaceDim;

            if (dim == 0)
            {
                pSurfOut.Height = 1;
                pSurfOut.Depth = 1;
            }
            else if (dim is 1 or 6)
            {
                pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                pSurfOut.Depth = 1;
            }
            else if (dim == 2)
            {
                pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                pSurfOut.Depth = Math.Max(1, surfaceDepth >> level);
            }
            else if (dim == 3)
            {
                pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                pSurfOut.Depth = Math.Max(6, surfaceDepth);
            }
            else if (dim == 4)
            {
                pSurfOut.Height = 1;
                pSurfOut.Depth = surfaceDepth;
            }
            else if (dim is 5 or 7)
            {
                pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                pSurfOut.Depth = surfaceDepth;
            }

            pSurfOut.PixelPitch = width;
            pSurfOut.PixelHeight = ~(blockSize - 1) & (pSurfOut.Height + blockSize - 1);
            pSurfOut.Height = pSurfOut.PixelHeight / blockSize;
            pSurfOut.SurfSize = (pSurfOut.Bpp * numSamples * pSurfOut.Depth * pSurfOut.Height * pSurfOut.Pitch) >> 3;

            pSurfOut.SliceSize = (uint)(surfaceDim == 2 
                ? pSurfOut.SurfSize 
                : pSurfOut.SurfSize / pSurfOut.Depth);

            pSurfOut.PitchTileMax = (pSurfOut.Pitch >> 3) - 1;
            pSurfOut.HeightTileMax = (pSurfOut.Height >> 3) - 1;
            pSurfOut.SliceTileMax = ((pSurfOut.Height * pSurfOut.Pitch) >> 6) - 1;
        }
        else
        {
            aSurfIn.Size = 60;
            aSurfIn.TileMode = surfaceTileMode & 0xF;
            aSurfIn.Format = hwFormat;
            aSurfIn.Bpp = formatHwInfo[hwFormat * 4];
            aSurfIn.NumSamples = (uint)1 << (int)surfaceAA;
            aSurfIn.NumFrags = aSurfIn.NumSamples;
            aSurfIn.Width = Math.Max(1, surfaceWidth >> level);
            dim = surfaceDim;

            if (dim == 0)
            {
                aSurfIn.Height = 1;
                aSurfIn.NumSlices = 1;
            }
            else if (dim is 1 or 6)
            {
                aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                aSurfIn.NumSlices = 1;
            }
            else if (dim == 2)
            {
                aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                aSurfIn.NumSlices = Math.Max(1, surfaceDepth >> level);
            }
            else if (dim == 3)
            {
                aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                aSurfIn.NumSlices = Math.Max(6, surfaceDepth);
                aSurfIn.Flags |= 0x10;
            }
            else if (dim == 4)
            {
                aSurfIn.Height = 1;
                aSurfIn.NumSlices = surfaceDepth;
            }
            else if (dim is 5 or 7)
            {
                aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                aSurfIn.NumSlices = surfaceDepth;
            }

            aSurfIn.Slice = 0;
            aSurfIn.MipLevel = (uint)level;

            if (surfaceDim == 2)
            {
                aSurfIn.Flags |= 0x20;
            }

            if (level == 0)
            {
                aSurfIn.Flags = (1 << 12) | (aSurfIn.Flags & 0xFFFFEFFF);
            }
            else
            {
                aSurfIn.Flags &= 0xFFFFEFFF;
            }

            pSurfOut.Size = 96;
            ComputeSurfaceInfo(aSurfIn, pSurfOut);
        }

        if (pSurfOut.TileMode == 0)
        {
            pSurfOut.TileMode = 16;
        }

        return pSurfOut;
    }

    public void ComputeSurfaceInfo(SurfaceIn aSurfIn, SurfaceOut pSurfOut)
    {
        pIn = aSurfIn;
        pOut = pSurfOut;

        uint returnCode = 0;

        uint width, height, bpp, elemMode = 0;
        uint expandY, expandX;

        if (pIn.Bpp > 0x80)
            returnCode = 3;

        if (returnCode == 0)
        {

            ComputeMipLevel();

            width = pIn.Width;
            height = pIn.Height;
            bpp = pIn.Bpp;
            expandX = 1;
            expandY = 1;

            pOut.PixelBits = pIn.Bpp;

            if (pIn.Format != 0)
            {
                bpp = formatExInfo[pIn.Format * 4];
                expandX = formatExInfo[(pIn.Format * 4) + 1];
                expandY = formatExInfo[(pIn.Format * 4) + 2];
                elemMode = formatExInfo[(pIn.Format * 4) + 3];

                if (elemMode == 4 && expandX == 3 && pIn.TileMode == 1)
                    pIn.Flags |= 0x200;

                bpp = AdjustSurfaceInfo(elemMode, expandX, expandY, bpp, width, height);
            }
            else if (pIn.Bpp != 0)
            {
                pIn.Width = Math.Max(1, pIn.Width);
                pIn.Height = Math.Max(1, pIn.Height);
            }
            else
                returnCode = 3;

            if (returnCode == 0)
                returnCode = ComputeSurfaceInfoEx();

            if (returnCode == 0)
            {
                pOut.Bpp = pIn.Bpp;
                pOut.PixelPitch = pOut.Pitch;
                pOut.PixelHeight = pOut.Height;

                if (pIn.Format != 0 && (((pIn.Flags >> 9) & 1) == 0 || pIn.MipLevel == 0))
                    bpp = RestoreSurfaceInfo(elemMode, expandX, expandY, bpp);

                if (((pIn.Flags >> 5) & 1) != 0)
                    pOut.SliceSize = (uint)pOut.SurfSize;

                else
                {
                    pOut.SliceSize = (uint)(pOut.SurfSize / pOut.Depth);

                    if (pIn.Slice == (pIn.NumSlices - 1) && pIn.NumSlices > 1)
                        pOut.SliceSize += pOut.SliceSize * (pOut.Depth - pIn.NumSlices);
                }

                pOut.PitchTileMax = (pOut.Pitch >> 3) - 1;
                pOut.HeightTileMax = (pOut.Height >> 3) - 1;
                pOut.SliceTileMax = ((pOut.Height * pOut.Pitch) >> 6) - 1;
            }
        }
    }

    private uint RestoreSurfaceInfo(uint elemMode, uint expandX, uint expandY, uint bpp)
    {
        uint width, height;

        if (pOut.PixelPitch != 0 && pOut.PixelHeight != 0)
        {
            width = pOut.PixelPitch;
            height = pOut.PixelHeight;

            if (expandX > 1 || expandY > 1)
            {
                if (elemMode == 4)
                {
                    width /= expandX;
                    height /= expandY;
                }

                else
                {
                    width *= expandX;
                    height *= expandY;
                }
            }

            pOut.PixelPitch = Math.Max(1, width);
            pOut.PixelHeight = Math.Max(1, height);
        }

        if (bpp != 0)
        {
            return elemMode switch
            {
                4 => expandY * expandX * bpp,
                5 or 6 => bpp / expandX / expandY,
                9 or 12 => 64,
                10 or 11 or 13 => 128,
                _ => bpp,
            };
        }

        return 0;
    }

    public enum AddrTileMode
    {
        ADDR_TM_LINEAR_GENERAL = 0x0,
        ADDR_TM_LINEAR_ALIGNED = 0x1,
        ADDR_TM_1D_TILED_THIN1 = 0x2,
        ADDR_TM_1D_TILED_THICK = 0x3,
        ADDR_TM_2D_TILED_THIN1 = 0x4,
        ADDR_TM_2D_TILED_THIN2 = 0x5,
        ADDR_TM_2D_TILED_THIN4 = 0x6,
        ADDR_TM_2D_TILED_THICK = 0x7,
        ADDR_TM_2B_TILED_THIN1 = 0x8,
        ADDR_TM_2B_TILED_THIN2 = 0x9,
        ADDR_TM_2B_TILED_THIN4 = 0x0A,
        ADDR_TM_2B_TILED_THICK = 0x0B,
        ADDR_TM_3D_TILED_THIN1 = 0x0C,
        ADDR_TM_3D_TILED_THICK = 0x0D,
        ADDR_TM_3B_TILED_THIN1 = 0x0E,
        ADDR_TM_3B_TILED_THICK = 0x0F,
        ADDR_TM_2D_TILED_XTHICK = 0x10,
        ADDR_TM_3D_TILED_XTHICK = 0x11,
        ADDR_TM_POWER_SAVE = 0x12,
        ADDR_TM_COUNT = 0x13,
    }

    private uint ComputeSurfaceInfoEx()
    {
        uint tileMode = pIn.TileMode;
        uint bpp = pIn.Bpp;
        uint numSamples = Math.Max(1, pIn.NumSamples);
        uint pitch = pIn.Width;
        uint height = pIn.Height;
        uint numSlices = pIn.NumSlices;
        uint mipLevel = pIn.MipLevel;
        uint flags = pIn.Flags;
        uint pPitchOut = pOut.Pitch;
        uint pHeightOut = pOut.Height;
        uint pNumSlicesOut = pOut.Depth;
        uint pTileModeOut = pOut.TileMode;
        uint pSurfSize = (uint)pOut.SurfSize;
        uint pBaseAlign = pOut.BaseAlign;
        uint pPitchAlign = pOut.PitchAlign;
        uint pHeightAlign = pOut.HeightAlign;
        uint pDepthAlign = pOut.DepthAlign;
        uint padDims = 0;
        uint valid = 0;
        uint baseTileMode = tileMode;

        if ((((flags >> 4) & 1) != 0) && (mipLevel == 0))
            padDims = 2;

        tileMode = ((flags >> 6) & 1) != 0
            ? (uint)(tileMode switch
            {
                8 => 4,
                9 => 5,
                10 => 6,
                11 => 7,
                14 => 12,
                15 => 13,
                _ => tileMode,
            })
            : ComputeSurfaceMipLevelTileMode(
            tileMode,
            bpp,
            mipLevel,
            pitch,
            height,
            numSlices,
            numSamples,
            (flags >> 1) & 1, 0);

        switch (tileMode)
        {
            case 0:
            case 1:
                var compSurfInfoLinear = ComputeSurfaceInfoLinear(
            tileMode,
            bpp,
            numSamples,
            pitch,
            height,
            numSlices,
            mipLevel,
            padDims,
            flags);

                valid = compSurfInfoLinear[0];
                pPitchOut = compSurfInfoLinear[1];
                pHeightOut = compSurfInfoLinear[2];
                pNumSlicesOut = compSurfInfoLinear[3];
                pSurfSize = compSurfInfoLinear[4];
                pBaseAlign = compSurfInfoLinear[5];
                pPitchAlign = compSurfInfoLinear[6];
                pHeightAlign = compSurfInfoLinear[7];
                pDepthAlign = compSurfInfoLinear[8];

                pTileModeOut = tileMode;
                break;
            case 2:
            case 3:
                var compSurfInfoMicroTile = ComputeSurfaceInfoMicroTiled(
            tileMode,
            bpp,
            numSamples,
            pitch,
            height,
            numSlices,
            mipLevel,
            padDims,
            flags);

                valid = compSurfInfoMicroTile[0];
                pPitchOut = compSurfInfoMicroTile[1];
                pHeightOut = compSurfInfoMicroTile[2];
                pNumSlicesOut = compSurfInfoMicroTile[3];
                pSurfSize = compSurfInfoMicroTile[4];
                pTileModeOut = compSurfInfoMicroTile[5];
                pBaseAlign = compSurfInfoMicroTile[6];
                pPitchAlign = compSurfInfoMicroTile[7];
                pHeightAlign = compSurfInfoMicroTile[8];
                pDepthAlign = compSurfInfoMicroTile[9];

                break;
            case 4:
            case 5:
            case 6:
            case 7:
            case 8:
            case 9:
            case 10:
            case 11:
            case 12:
            case 13:
            case 14:
            case 15:
                var compSurfInfoMacoTile = ComputeSurfaceInfoMacroTiled(
            tileMode,
            baseTileMode,
            bpp,
            numSamples,
            pitch,
            height,
            numSlices,
            mipLevel,
            padDims,
            flags);

                valid = compSurfInfoMacoTile[0];
                pPitchOut = compSurfInfoMacoTile[1];
                pHeightOut = compSurfInfoMacoTile[2];
                pNumSlicesOut = compSurfInfoMacoTile[3];
                pSurfSize = compSurfInfoMacoTile[4];
                pTileModeOut = compSurfInfoMacoTile[5];
                pBaseAlign = compSurfInfoMacoTile[6];
                pPitchAlign = compSurfInfoMacoTile[7];
                pHeightAlign = compSurfInfoMacoTile[8];
                pDepthAlign = compSurfInfoMacoTile[9];
                break;
        }

        pOut.Pitch = pPitchOut;
        pOut.Height = pHeightOut;
        pOut.Depth = pNumSlicesOut;
        pOut.TileMode = pTileModeOut;
        pOut.SurfSize = pSurfSize;
        pOut.BaseAlign = pBaseAlign;
        pOut.PitchAlign = pPitchAlign;
        pOut.HeightAlign = pHeightAlign;
        pOut.DepthAlign = pDepthAlign;

        return (uint)(valid == 0 
            ? 3 
            : 0);
    }

    private uint[] ComputeSurfaceInfoMacroTiled(uint tileMode, uint baseTileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
    private uint[] ComputeSurfaceInfoMicroTiled(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
    private uint[] ComputeSurfaceInfoLinear(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        // TODO: Implement
        throw new NotImplementedException();
    }
    private uint ComputeSurfaceMipLevelTileMode(uint tileMode, uint bpp, uint mipLevel, uint pitch, uint height, uint numSlices, uint numSamples, uint isDepth, uint noRecursive)
    {
        // TODO: Implement
        throw new NotImplementedException();
    }

    private uint AdjustSurfaceInfo(uint elemMode, uint expandX, uint expandY, uint bpp, uint width, uint height)
    {
        uint bBCnFormat = 0;
        uint widtha, heighta;

        switch (elemMode)
        {
            case 9:
            case 10:
            case 11:
            case 12:
            case 13:
                if (bpp != 0)
                    bBCnFormat = 1;

                break;
        }

        if (width != 0 && height != 0)
        {
            if (expandX > 1 || expandY > 1)
            {
                if (elemMode == 4)
                {
                    widtha = expandX * width;
                    heighta = expandY * height;
                }
                else if (bBCnFormat != 0)
                {
                    widtha = width / expandX;
                    heighta = height / expandY;
                }
                else
                {
                    widtha = (width + expandX - 1) / expandX;
                    heighta = (height + expandY - 1) / expandY;
                }

                pIn.Width = Math.Max(1, widtha);
                pIn.Height = Math.Max(1, heighta);
            }
        }

        if (bpp != 0)
        {
            pIn.Bpp = elemMode switch
            {
                4 => bpp / expandX / expandY,
                5 or 6 => expandY * expandX * bpp,
                9 or 12 => 64,
                10 or 11 or 13 => 128,
                _ => bpp,
            };

            return pIn.Bpp;
        }

        return 0;
    }

    public static (uint Bpp, uint ExpandX, uint ExpandY, uint ElemMode) GetBitsPerPixel(int format)
    {
        int fmtIdx = format * 4;
        return (formatExInfo[fmtIdx], formatExInfo[fmtIdx + 1], formatExInfo[fmtIdx + 2], formatExInfo[fmtIdx + 3]);
    }

    public void ComputeMipLevel()
    {
        uint slices = 0;
        uint height = 0;
        uint width = 0;
        uint hwlHandled = 0;

        if (49 <= pIn.Format && pIn.Format <= 55 && (pIn.MipLevel == 0 || ((pIn.Flags >> 12) & 1) != 0))
        {
            pIn.Width = PowTwoAlign(pIn.Width, 4);
            pIn.Height = PowTwoAlign(pIn.Height, 4);
        }

        hwlHandled = HwlComputeMipLevel();
        if (hwlHandled == 0 && pIn.MipLevel > 0 && ((pIn.Flags >> 12) & 1) != 0)
        {
            width = Math.Max(1, pIn.Width >> (int)pIn.MipLevel);
            height = Math.Max(1, pIn.Height >> (int)pIn.MipLevel);
            slices = Math.Max(1, pIn.NumSlices);

            if (((pIn.Flags >> 4) & 1) == 0)
            {
                slices = Math.Max(1, slices >> (int)pIn.MipLevel);
            }

            if (pIn.Format is not 47 and not 48)
            {
                width = NextPow2(width);
                height = NextPow2(height);
                slices = NextPow2(slices);
            }

            pIn.Width = width;
            pIn.Height = height;
            pIn.NumSlices = slices;
        }
    }

    public uint HwlComputeMipLevel()
    {
        uint handled = 0;

        if (pIn.Format is >= 49 and <= 55)
        {
            if (pIn.MipLevel > 0)
            {
                uint width = pIn.Width;
                uint height = pIn.Height;
                uint slices = pIn.NumSlices;

                if (((pIn.Flags >> 12) & 1) != 0)
                {
                    uint widtha = width >> (int)pIn.MipLevel;
                    uint heighta = height >> (int)pIn.MipLevel;

                    if (((pIn.Flags >> 4) & 1) == 0)
                    {
                        slices >>= (int)pIn.MipLevel;
                    }

                    width = Math.Max(1, widtha);
                    height = Math.Max(1, heighta);
                    slices = Math.Max(1, slices);
                }

                pIn.Width = NextPow2(width);
                pIn.Height = NextPow2(height);
                pIn.NumSlices = slices;
            }

            handled = 1;
        }

        return handled;
    }

    private static uint NextPow2(uint dim)
    {
        uint newDim = 1;

        while (newDim < dim && newDim < int.MaxValue)
        {
            newDim <<= 1;
        }

        return newDim;
    }

    private static uint PowTwoAlign(uint dim, uint align)
    {
        return (dim + align - 1) & ~(align - 1);
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
                : (reader.ReadByte(),
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

    public class SurfaceIn
    {
        public uint Size { get; set; }
        public uint TileMode { get; set; }
        public uint Format { get; set; }
        public uint Bpp { get; set; }
        public uint NumSamples { get; set; }
        public uint NumFrags { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public uint NumSlices { get; set; }
        public uint Slice { get; set; }
        public uint MipLevel { get; set; }
        public uint Flags { get; set; }
    }

    public class SurfaceOut
    {
        public uint Bpp { get; set; }
        public uint Size { get; set; }
        public uint Pitch { get; set; }
        public uint PixelBits { get; set; }
        public uint BaseAlign { get; set; }
        public uint PitchAlign { get; set; }
        public uint HeightAlign { get; set; }
        public uint DepthAlign { get; set; }
        public uint Height { get; set; }
        public uint Depth { get; set; }
        public uint PixelPitch { get; set; }
        public uint PixelHeight { get; set; }
        public long SurfSize { get; set; }
        public uint SliceSize { get; set; }
        public uint PitchTileMax { get; set; }
        public uint HeightTileMax { get; set; }
        public uint SliceTileMax { get; set; }
        public uint TileMode { get; set; }
    }
}
