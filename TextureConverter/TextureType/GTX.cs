using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

using System.Text;
using static TextureConverter.TextureType.DDS;

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

    static uint DivRoundUp(uint a, uint b)
    {
        return (a + b - 1) / b;
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

        SurfaceOut surfOut = GetSurfaceInfo(
            texInfo.Format,
            texInfo.Width,
            texInfo.Height,
            texInfo.Depth,
            texInfo.Dim,
            texInfo.TileMode,
            texInfo.AA,
            0);

        uint bpp = GetBPP(texInfo.Format);

		if (!Enum.IsDefined(typeof(GX2SurfaceFormat), texInfo.Format) || texInfo.Format == GX2SurfaceFormat.GX2_SURFACE_FORMAT_INVALID)
            throw new Exception("Invalid format!");

        if (texInfo.AA == 0)
            throw new Exception("Unsupported AA value!");

        uint tilingDepth = surfOut.Depth;

        if (surfOut.TileMode == 3)
            tilingDepth >>= 2;

        if (tilingDepth != 1)
            throw new Exception("Unsupported tiling depth!");

        uint blkWidth = 1;
        uint blkHeight = 1;

        if (BCnFormats.Contains(texInfo.Format))
        {
            blkWidth = 4;
            blkHeight = 4;
        }

        List<byte> result = [];

        for (int mipLevel = 0; mipLevel < texInfo.MipCount; mipLevel++) 
        {
            uint mipWidth = Math.Max(1, texInfo.Width >> mipLevel);
            uint mipHeight = Math.Max(1, texInfo.Height >> mipLevel);

            uint mipSize = DivRoundUp(mipWidth, blkWidth) * DivRoundUp(mipHeight, blkHeight) * bpp;

            if (mipLevel != 0)
            {
                uint mipOffset = texInfo.MipOffsets[mipLevel - 1];

                if (mipLevel == 1)
                    mipOffset -= (uint)surfOut.SurfSize;

                surfOut = GetSurfaceInfo(texInfo.Format, texInfo.Width, texInfo.Height, texInfo.Depth, texInfo.Dim, texInfo.TileMode, texInfo.AA, mipLevel);

                data = mipData[(int)mipOffset..(int)(mipOffset + surfOut.SurfSize)];
            }

            // TODO
            byte[] mipResult = Deswizzle(mipWidth, mipHeight, 1, texInfo.Format, 0, texInfo.Use, surfOut.TileMode,
                texInfo.Swizzle, surfOut.Pitch, surfOut.Bpp, 0, 0, data);
        }

        // TODO
    }

    private byte[] Deswizzle(uint mipWidth, uint mipHeight, uint depth, GX2SurfaceFormat format, uint aa, uint use, uint tileMode, uint swizzle, uint pitch, uint bpp, uint slice, uint sample, byte[] data)
    {
        return SwizzleSurface(mipWidth, mipHeight, depth, format, aa, use, swizzle, pitch, bpp, slice, sample, data, false);
    }

    private byte[] SwizzleSurface(uint width, uint height, uint depth, GX2SurfaceFormat format, uint aa, uint use, uint swizzle, uint pitch, uint bitsPerPixel, uint slice, uint sample, byte[] data, bool doSwizzle)
    {
        uint bytesPerPixel = bitsPerPixel / 8;

        byte[] result = new byte[data.Length];

        if (BCnFormats.Contains(format))
        {
            width = (width + 3) / 4;
            height = (height + 3) / 4;
        }

        uint pipeSwizzle = (swizzle >> 8) & 0x1;
        uint bankSwizzle = (swizzle >> 9) & 0x3;

        uint tileMode = (uint)format == 16 ? 0 : (uint)format;

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                uint pos = tileMode switch
                {
                    0 or 1 => ComputeSurfaceAddrFromCoordLinear(x, y, slice, sample, bytesPerPixel, pitch, height, depth),
                    2 or 3 => ComputeSurfaceAddrFromCoordMicroTiled(x, y, slice, bitsPerPixel, pitch, height, tileMode, (use & 3) != 0),
                    _ => ComputeSurfaceAddrFromCoordMacroTiled(x, y, slice, sample, bitsPerPixel,pitch, height, (uint)(1 << (int)aa), tileMode, (use & 4) != 0, pipeSwizzle, bankSwizzle),
                };

                // TODO pos stuff
            }
        }
    }

    private uint ComputeSurfaceAddrFromCoordMacroTiled(uint x, uint y, uint slice, uint sample, uint bpp, uint pitch, uint height, uint numSamples, uint tileMode, bool isDepth, uint pipeSwizzle, uint bankSwizzle)
    {
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);
        uint microTileBits = numSamples * bpp * 64 * microTileThickness;
        uint microTileBytes = (microTileBits + 7) / 8;

        uint pixelIndex = ComputePixelIndexWithinMicroTile(x, y, slice, bpp, tileMode, isDepth);
        uint bytesPerSample = microTileBytes / numSamples;

        uint sampleOffset;
        uint pixelOffset;
        if (isDepth)
        {
            sampleOffset = bpp * sample;
            pixelOffset = numSamples * bpp * pixelIndex;
        }
        else
        {
            sampleOffset = sample * (microTileBits / numSamples);
            pixelOffset = bpp * pixelIndex;
        }

        uint elemOffset = sampleOffset + pixelOffset;

        uint samplesPerSlice;
        uint numSampleSplits;
        uint sampleSlice;
        uint tileSliceBits;
        if (numSamples <= 1 || microTileBytes <= 2048)
        {
            samplesPerSlice = numSamples;
            numSampleSplits = 1;
            sampleSlice = 0;
        }
        else
        {
            samplesPerSlice = 2048 / bytesPerSample;
            numSampleSplits = numSamples / samplesPerSlice;
            numSamples = samplesPerSlice;

            tileSliceBits = microTileBits / numSampleSplits;
            sampleSlice = elemOffset / tileSliceBits;
            elemOffset %= tileSliceBits;
        }

        uint pipe = ((y >> 3) ^ (x >> 3)) & 1;
        uint bank = (((y >> 5) ^ (x >> 3)) & 1) | (2 * (((y >> 4) ^ (x >> 4)) & 1));

        uint swizzle = pipeSwizzle + (2 * bankSwizzle);
        uint bankPipe = pipe + (2 * bank);
        uint rotation = tileMode switch
        {
            >= 4 and <= 11 => 2,
            >= 12 and <= 15 => 1,
            _ => 0,
        };

        uint sliceIn = slice;
        if (tileMode is 7 or 11 or 13 or 15)
        {
            sliceIn >>= 2;
        }

        bankPipe ^= (2 * sampleSlice * 3) ^ (swizzle + (sliceIn * rotation));
        bankPipe %= 8;
        pipe = bankPipe % 2;
        bank = bankPipe / 2;

        uint sliceBytes = ((pitch * height * microTileThickness * bpp * numSamples) + 7) / 8;
        uint sliceOffset = sliceBytes * ((sampleSlice + (numSampleSplits * slice)) / microTileThickness);

        uint macroTilePitch = 32;
        uint macroTileHeight = 16;

        if (tileMode is 5 or 9)
        {
            macroTilePitch = 16;
            macroTileHeight = 32;
        }
        else if (tileMode is 6 or 10)
        {
            macroTilePitch = 8;
            macroTileHeight = 64;
        }

        uint macroTilesPerRow = pitch / macroTilePitch;
        uint macroTileBytes = ((numSamples * microTileThickness * bpp * macroTileHeight * macroTilePitch) + 7) / 8;
        uint macroTileIndexX = x / macroTilePitch;
        uint macroTileIndexY = y / macroTileHeight;
        uint macroTileOffset = macroTileBytes * (macroTileIndexX + (macroTileIndexY * macroTilesPerRow));

        if (tileMode is 8 or 9 or 10 or 11 or 14 or 15)
        {
            uint[] bankSwapOrder = [0, 1, 3, 2, 6, 7, 5, 4, 0, 0];
            uint bankSwapWidth = ComputeSurfaceBankSwappedWidth((AddrTileMode)tileMode, bpp, numSamples, pitch);
            uint swapIndex = macroTilePitch * macroTileIndexX / bankSwapWidth;
            bank ^= bankSwapOrder[swapIndex & 3];
        }

        uint totalOffset = elemOffset + ((macroTileOffset + sliceOffset) >> 3);
        return (bank << 9) | (pipe << 8) | (totalOffset & 0xFF) | (((uint)((int)totalOffset & -256)) << 3);
    }

    private uint ComputeSurfaceAddrFromCoordMicroTiled(uint x, uint y, uint slice, uint bpp, uint pitch, uint height, uint tileMode, bool isDepth)
    {
        uint microTileThickness = tileMode == 3 ? 4u : 1u;
        uint microTileBytes = ((64 * microTileThickness * bpp) + 7) / 8;
        uint microTilesPerRow = pitch >> 3;
        uint microTileIndexX = x >> 3;
        uint microTileIndexY = y >> 3;
        uint microTileIndexZ = slice / microTileThickness;

        uint microTileOffset = microTileBytes * (microTileIndexX + (microTileIndexY * microTilesPerRow));
        uint sliceBytes = ((pitch * height * microTileThickness * bpp) + 7) / 8;
        uint sliceOffset = microTileIndexZ * sliceBytes;

        uint pixelIndex = ComputePixelIndexWithinMicroTile(x, y, slice, bpp, tileMode, isDepth);
        uint pixelOffset = (pixelIndex * bpp) >> 3;

        return pixelOffset + microTileOffset + sliceOffset;
    }

    static uint ComputePixelIndexWithinMicroTile(uint x, uint y, uint z, uint bpp, uint tileMode, bool isDepth)
    {
        uint pixelIndex = 0;

        uint thickness = ComputeSurfaceThickness((AddrTileMode)tileMode);

        if (isDepth)
        {
            pixelIndex |= (x & 1) << 0;
            pixelIndex |= (y & 1) << 1;
            pixelIndex |= (x & 2) >> 1 << 2;
            pixelIndex |= (y & 2) >> 1 << 3;
            pixelIndex |= (x & 4) >> 2 << 4;
            pixelIndex |= (y & 4) >> 2 << 5;
        }
        else
        {
            switch (bpp)
            {
                case 8:
                    pixelIndex |= (x & 1) << 0;
                    pixelIndex |= (x & 2) >> 1 << 1;
                    pixelIndex |= (x & 4) >> 2 << 2;
                    pixelIndex |= (y & 2) >> 1 << 3;
                    pixelIndex |= (y & 1) << 4;
                    pixelIndex |= (y & 4) >> 2 << 5;
                    break;

                case 0x10:
                    pixelIndex |= (x & 1) << 0;
                    pixelIndex |= (x & 2) >> 1 << 1;
                    pixelIndex |= (x & 4) >> 2 << 2;
                    pixelIndex |= (y & 1) << 3;
                    pixelIndex |= (y & 2) >> 1 << 4;
                    pixelIndex |= (y & 4) >> 2 << 5;
                    break;

                case 0x20:
                case 0x60:
                    pixelIndex |= (x & 1) << 0;
                    pixelIndex |= (x & 2) >> 1 << 1;
                    pixelIndex |= (y & 1) << 2;
                    pixelIndex |= (x & 4) >> 2 << 3;
                    pixelIndex |= (y & 2) >> 1 << 4;
                    pixelIndex |= (y & 4) >> 2 << 5;
                    break;

                case 0x40:
                    pixelIndex |= (x & 1) << 0;
                    pixelIndex |= (y & 1) << 1;
                    pixelIndex |= (x & 2) >> 1 << 2;
                    pixelIndex |= (x & 4) >> 2 << 3;
                    pixelIndex |= (y & 2) >> 1 << 4;
                    pixelIndex |= (y & 4) >> 2 << 5;
                    break;

                case 0x80:
                    pixelIndex |= (y & 1) << 0;
                    pixelIndex |= (x & 1) << 1;
                    pixelIndex |= (x & 2) >> 1 << 2;
                    pixelIndex |= (x & 4) >> 2 << 3;
                    pixelIndex |= (y & 2) >> 1 << 4;
                    pixelIndex |= (y & 4) >> 2 << 5;
                    break;

                default:
                    pixelIndex |= (x & 1) << 0;
                    pixelIndex |= (x & 2) >> 1 << 1;
                    pixelIndex |= (y & 1) << 2;
                    pixelIndex |= (x & 4) >> 2 << 3;
                    pixelIndex |= (y & 2) >> 1 << 4;
                    pixelIndex |= (y & 4) >> 2 << 5;
                    break;
            }
        }

        if (thickness > 1)
        {
            pixelIndex |= (z & 1) << 6;
            pixelIndex |= (z & 2) >> 1 << 7;
        }

        if (thickness == 8)
        {
            pixelIndex |= (z & 4) >> 2 << 8;
        }

        return pixelIndex;
    }


    private static uint ComputeSurfaceAddrFromCoordLinear(uint x, uint y, uint slice, uint sample, uint bpp, uint pitch, uint height, uint depth)
    {
        return ((y * pitch) + x + (pitch * height * (slice + (sample * depth)))) * bpp;
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

            switch (dim)
            {
                case 0:
                    pSurfOut.Height = 1;
                    pSurfOut.Depth = 1;
                    break;
                case 1 or 6:
                    pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                    pSurfOut.Depth = 1;
                    break;
                case 2:
                    pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                    pSurfOut.Depth = Math.Max(1, surfaceDepth >> level);
                    break;
                case 3:
                    pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                    pSurfOut.Depth = Math.Max(6, surfaceDepth);
                    break;
                case 4:
                    pSurfOut.Height = 1;
                    pSurfOut.Depth = surfaceDepth;
                    break;
                case 5 or 7:
                    pSurfOut.Height = Math.Max(1, surfaceHeight >> level);
                    pSurfOut.Depth = surfaceDepth;
                    break;
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

            switch (dim)
            {
                case 0:
                    aSurfIn.Height = 1;
                    aSurfIn.NumSlices = 1;
                    break;
                case 1 or 6:
                    aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                    aSurfIn.NumSlices = 1;
                    break;
                case 2:
                    aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                    aSurfIn.NumSlices = Math.Max(1, surfaceDepth >> level);
                    break;
                case 3:
                    aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                    aSurfIn.NumSlices = Math.Max(6, surfaceDepth);
                    aSurfIn.Flags |= 0x10;
                    break;
                case 4:
                    aSurfIn.Height = 1;
                    aSurfIn.NumSlices = surfaceDepth;
                    break;
                case 5 or 7:
                    aSurfIn.Height = Math.Max(1, surfaceHeight >> level);
                    aSurfIn.NumSlices = surfaceDepth;
                    break;
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
            ? tileMode switch
            {
                8 => 4,
                9 => 5,
                10 => 6,
                11 => 7,
                14 => 12,
                15 => 13,
                _ => tileMode,
            }
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

    private static uint ComputeSurfaceThickness(AddrTileMode tileMode)
    {
        return tileMode switch
        {
            AddrTileMode.ADDR_TM_1D_TILED_THICK or AddrTileMode.ADDR_TM_2D_TILED_THICK or AddrTileMode.ADDR_TM_2B_TILED_THICK or AddrTileMode.ADDR_TM_3D_TILED_THICK or AddrTileMode.ADDR_TM_3B_TILED_THICK => 4,
            AddrTileMode.ADDR_TM_2D_TILED_XTHICK or AddrTileMode.ADDR_TM_3D_TILED_XTHICK => 8,
            _ => 1,
        };
    }

    private static uint IsThickMacroTiled(AddrTileMode tileMode)
    {
        return tileMode switch
        {
            AddrTileMode.ADDR_TM_2D_TILED_THICK or AddrTileMode.ADDR_TM_2B_TILED_THICK or AddrTileMode.ADDR_TM_3D_TILED_THICK or AddrTileMode.ADDR_TM_3B_TILED_THICK => 1,
            _ => 0,
        };
    }

    private static uint ComputeMacroTileAspectRatio(AddrTileMode tileMode)
    {
        return tileMode switch
        {
            AddrTileMode.ADDR_TM_2D_TILED_THIN2 or AddrTileMode.ADDR_TM_2B_TILED_THIN2 => 2,
            AddrTileMode.ADDR_TM_2D_TILED_THIN4 or AddrTileMode.ADDR_TM_2B_TILED_THIN4 => 4,
            _ => 1,
        };
    }

    private static uint AdjustPitchAlignment(uint flags, uint pitchAlign)
    {
        if (((flags >> 13) & 1) != 0)
            pitchAlign = PowTwoAlign(pitchAlign, 0x20);

        return pitchAlign;
    }

    private static Tuple<uint, uint, uint, uint, uint> ComputeSurfaceAlignmentsMacroTiled(uint tileMode, uint bpp, uint flags, uint numSamples)
    {
        uint aspectRatio = ComputeMacroTileAspectRatio((AddrTileMode)tileMode);
        uint thickness = ComputeSurfaceThickness((AddrTileMode)tileMode);

        switch (bpp)
        {
            case 24:
            case 48:
            case 96:
                bpp /= 3;
                break;
            case 3:
                bpp = 1;
                break;
        }

        uint macroTileWidth = 32 / aspectRatio;
        uint macroTileHeight = aspectRatio * 16;

        uint pitchAlign = Math.Max(macroTileWidth, macroTileWidth * (256 / bpp / (8 * thickness) / numSamples));
        pitchAlign = AdjustPitchAlignment(flags, pitchAlign);

        uint heightAlign = macroTileHeight;
        uint macroTileBytes = numSamples * (((bpp * macroTileHeight * macroTileWidth) + 7) >> 3);

        uint baseAlign;

        if (thickness == 1)
            baseAlign = Math.Max(macroTileBytes, ((numSamples * heightAlign * bpp * pitchAlign) + 7) >> 3);
        else
            baseAlign = Math.Max(256, ((4 * heightAlign * bpp * pitchAlign) + 7) >> 3);

        uint microTileBytes = ((thickness * numSamples * (bpp << 6)) + 7) >> 3;
        uint numSlicesPerMicroTile = microTileBytes < 2048 ? 1 : microTileBytes / 2048;

        baseAlign /= numSlicesPerMicroTile;

        return new Tuple<uint, uint, uint, uint, uint>(baseAlign, pitchAlign, heightAlign, macroTileWidth, macroTileHeight);
    }

    private static uint IsBankSwappedTileMode(AddrTileMode tileMode)
    {
        return tileMode switch
        {
            AddrTileMode.ADDR_TM_2B_TILED_THIN1 or AddrTileMode.ADDR_TM_2B_TILED_THIN2 or AddrTileMode.ADDR_TM_2B_TILED_THIN4 or AddrTileMode.ADDR_TM_2B_TILED_THICK or AddrTileMode.ADDR_TM_3B_TILED_THIN1 or AddrTileMode.ADDR_TM_3B_TILED_THICK => 1,
            _ => 0,
        };
    }

    private static uint ComputeSurfaceBankSwappedWidth(AddrTileMode tileMode, uint bpp, uint numSamples, uint pitch)
    {
        if (IsBankSwappedTileMode(tileMode) == 0)
            return 0;

        uint bytesPerSample = 8 * bpp;
        uint samplesPerTile, slicesPerTile;

        if (bytesPerSample != 0)
        {
            samplesPerTile = 2048 / bytesPerSample;
            slicesPerTile = Math.Max(1, numSamples / samplesPerTile);
        }

        else
            slicesPerTile = 1;

        if (IsThickMacroTiled(tileMode) != 0)
            numSamples = 4;

        uint bytesPerTileSlice = numSamples * bytesPerSample / slicesPerTile;

        uint factor = ComputeMacroTileAspectRatio(tileMode);
        uint swapTiles = Math.Max(1, 128 / bpp);

        uint swapWidth = swapTiles * 32;
        uint heightBytes = numSamples * factor * bpp * 2 / slicesPerTile;
        uint swapMax = 0x4000 / heightBytes;
        uint swapMin = 256 / bytesPerTileSlice;

        uint bankSwapWidth = Math.Min(swapMax, Math.Max(swapMin, swapWidth));

        while (bankSwapWidth >= 2 * pitch)
            bankSwapWidth >>= 1;

        return bankSwapWidth;
    }

    private Tuple<uint, uint, uint> PadDimensions(uint tileMode, uint padDims, uint isCube, uint pitchAlign, uint heightAlign, uint sliceAlign)
    {
        uint thickness = ComputeSurfaceThickness((AddrTileMode)tileMode);
        if (padDims == 0)
            padDims = 3;

        if ((pitchAlign & (pitchAlign - 1)) == 0)
            expPitch = PowTwoAlign(expPitch, pitchAlign);
        else
        {
            expPitch += pitchAlign - 1;
            expPitch /= pitchAlign;
            expPitch *= pitchAlign;
        }

        if (padDims > 1)
            expHeight = PowTwoAlign(expHeight, heightAlign);

        if (padDims > 2 || thickness > 1)
        {
            if (isCube != 0)
                expNumSlices = NextPow2(expNumSlices);

            if (thickness > 1)
                expNumSlices = PowTwoAlign(expNumSlices, sliceAlign);
        }

        return new Tuple<uint, uint, uint>(expPitch, expHeight, expNumSlices);
    }

    uint expPitch, expHeight, expNumSlices;
    private uint[] ComputeSurfaceInfoMacroTiled(uint tileMode, uint baseTileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        expPitch = pitch;
        expHeight = height;
        expNumSlices = numSlices;

        uint valid = 1;
        uint expTileMode = tileMode;
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);

        uint baseAlign, pitchAlign, heightAlign, macroWidth, macroHeight;
        uint bankSwappedWidth, pitchAlignFactor;
        uint result, pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pTileModeOut, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign;

        if (mipLevel != 0)
        {
            expPitch = NextPow2(pitch);
            expHeight = NextPow2(height);

            if (((flags >> 4) & 1) != 0)
            {
                expNumSlices = numSlices;

                padDims = numSlices <= 1 
                    ? 2 
                    : (uint)0;
            }
            else
                expNumSlices = NextPow2(numSlices);

            if (expTileMode == 7 && expNumSlices < 4)
            {
                expTileMode = 4;
                microTileThickness = 1;
            }
        }

        if (tileMode == baseTileMode
            || mipLevel == 0
            || IsThickMacroTiled((AddrTileMode)baseTileMode) == 0
            || IsThickMacroTiled((AddrTileMode)tileMode) != 0)
        {
            var tup = ComputeSurfaceAlignmentsMacroTiled(
                tileMode,
                bpp,
                flags,
                numSamples);

            baseAlign = tup.Item1;
            pitchAlign = tup.Item2;
            heightAlign = tup.Item3;
            macroWidth = tup.Item4;
            macroHeight = tup.Item5;

            bankSwappedWidth = ComputeSurfaceBankSwappedWidth((AddrTileMode)tileMode, bpp, numSamples, pitch);

            if (bankSwappedWidth > pitchAlign)
                pitchAlign = bankSwappedWidth;

            var padDimens = PadDimensions(
                 tileMode,
                 padDims,
                 (flags >> 4) & 1,
                 pitchAlign,
                 heightAlign,
                 microTileThickness);

            expPitch = padDimens.Item1;
            expHeight = padDimens.Item2;
            expNumSlices = padDimens.Item3;

            pPitchOut = expPitch;
            pHeightOut = expHeight;
            pNumSlicesOut = expNumSlices;
            pSurfSize = ((expHeight * expPitch * expNumSlices * bpp * numSamples) + 7) / 8;
            pTileModeOut = expTileMode;
            pBaseAlign = baseAlign;
            pPitchAlign = pitchAlign;
            pHeightAlign = heightAlign;
            pDepthAlign = microTileThickness;
            result = valid;
        }

        else
        {
            var tup = ComputeSurfaceAlignmentsMacroTiled(
                baseTileMode,
                bpp,
                flags,
                numSamples);

            baseAlign = tup.Item1;
            pitchAlign = tup.Item2;
            heightAlign = tup.Item3;
            macroWidth = tup.Item4;
            macroHeight = tup.Item5;

            pitchAlignFactor = Math.Max(1, 32 / bpp);

            if (expPitch < pitchAlign * pitchAlignFactor || expHeight < heightAlign)
            {
                expTileMode = 2;

                var microTileInfo = ComputeSurfaceInfoMicroTiled(
                    2,
                    bpp,
                    numSamples,
                    pitch,
                    height,
                    numSlices,
                    mipLevel,
                    padDims,
                    flags);

                result = microTileInfo[0];
                pPitchOut = microTileInfo[1];
                pHeightOut = microTileInfo[2];
                pNumSlicesOut = microTileInfo[3];
                pSurfSize = microTileInfo[4];
                pTileModeOut = microTileInfo[5];
                pBaseAlign = microTileInfo[6];
                pPitchAlign = microTileInfo[7];
                pHeightAlign = microTileInfo[8];
                pDepthAlign = microTileInfo[9];
            }

            else
            {
                tup = ComputeSurfaceAlignmentsMacroTiled(
                    tileMode,
                    bpp,
                    flags,
                    numSamples);

                baseAlign = tup.Item1;
                pitchAlign = tup.Item2;
                heightAlign = tup.Item3;
                macroWidth = tup.Item4;
                macroHeight = tup.Item5;

                bankSwappedWidth = ComputeSurfaceBankSwappedWidth((AddrTileMode)tileMode, bpp, numSamples, pitch);
                if (bankSwappedWidth > pitchAlign)
                    pitchAlign = bankSwappedWidth;

                var padDimens = PadDimensions(
                    tileMode,
                    padDims,
                    (flags >> 4) & 1,
                    pitchAlign,
                    heightAlign,
                    microTileThickness);

                expPitch = padDimens.Item1;
                expHeight = padDimens.Item2;
                expNumSlices = padDimens.Item3;

                pPitchOut = expPitch;
                pHeightOut = expHeight;
                pNumSlicesOut = expNumSlices;
                pSurfSize = ((expHeight * expPitch * expNumSlices * bpp * numSamples) + 7) / 8;

                pTileModeOut = expTileMode;
                pBaseAlign = baseAlign;
                pPitchAlign = pitchAlign;
                pHeightAlign = heightAlign;
                pDepthAlign = microTileThickness;
                result = valid;
            }
        }

        return [ result, pPitchOut, pHeightOut,
                pNumSlicesOut, pSurfSize, pTileModeOut, pBaseAlign, pitchAlign, heightAlign, pDepthAlign];
    }

    private static Tuple<uint, uint, uint> ComputeSurfaceAlignmentsMicroTiled(uint tileMode, uint bpp, uint flags, uint numSamples)
    {
        switch (bpp)
        {
            case 24:
            case 48:
            case 96:
                bpp /= 3;
                break;
        }

        uint thickness = ComputeSurfaceThickness((AddrTileMode)tileMode);
        uint baseAlign = 256;
        uint pitchAlign = Math.Max(8, 256 / bpp / numSamples / thickness);
        uint heightAlign = 8;

        pitchAlign = AdjustPitchAlignment(flags, pitchAlign);

        return new Tuple<uint, uint, uint>(baseAlign, pitchAlign, heightAlign);

    }

    private uint[] ComputeSurfaceInfoMicroTiled(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        expPitch = pitch;
        expHeight = height;
        expNumSlices = numSlices;

        uint valid = 1;
        uint expTileMode = tileMode;
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);
        uint pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pTileModeOut, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign;

        if (mipLevel != 0)
        {
            expPitch = NextPow2(pitch);
            expHeight = NextPow2(height);
            if (((flags >> 4) & 1) != 0)
            {
                expNumSlices = numSlices;

                padDims = numSlices <= 1 
                    ? 2 
                    : (uint)0;
            }

            else
                expNumSlices = NextPow2(numSlices);

            if (expTileMode == 3 && expNumSlices < 4)
            {
                expTileMode = 2;
                microTileThickness = 1;
            }
        }

        var surfMicroAlign = ComputeSurfaceAlignmentsMicroTiled(
            expTileMode,
            bpp,
            flags,
            numSamples);

        uint baseAlign = surfMicroAlign.Item1;
        uint pitchAlign = surfMicroAlign.Item2;
        uint heightAlign = surfMicroAlign.Item3;

        var padDimens = PadDimensions(
            expTileMode,
            padDims,
            (flags >> 4) & 1,
            pitchAlign,
            heightAlign,
            microTileThickness);

        expPitch = padDimens.Item1;
        expHeight = padDimens.Item2;
        expNumSlices = padDimens.Item3;

        pPitchOut = expPitch;
        pHeightOut = expHeight;
        pNumSlicesOut = expNumSlices;
        pSurfSize = ((expHeight * expPitch * expNumSlices * bpp * numSamples) + 7) / 8;

        pTileModeOut = expTileMode;
        pBaseAlign = baseAlign;
        pPitchAlign = pitchAlign;
        pHeightAlign = heightAlign;
        pDepthAlign = microTileThickness;

        return [valid, pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pTileModeOut, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign];
    }

    private static Tuple<uint, uint, uint> ComputeSurfaceAlignmentsLinear(uint tileMode, uint bpp, uint flags)
    {
        uint pixelsPerPipeInterleave;
        uint baseAlign, pitchAlign, heightAlign;

        if (tileMode == 0)
        {
            baseAlign = 1;
            pitchAlign = bpp != 1 ? (uint)1 : 8;
            heightAlign = 1;
        }
        else if (tileMode == 1)
        {
            pixelsPerPipeInterleave = 2048 / bpp;
            baseAlign = 256;
            pitchAlign = Math.Max(0x40, pixelsPerPipeInterleave);
            heightAlign = 1;
        }
        else
        {
            baseAlign = 1;
            pitchAlign = 1;
            heightAlign = 1;
        }

        pitchAlign = AdjustPitchAlignment(flags, pitchAlign);

        return new Tuple<uint, uint, uint>(baseAlign, pitchAlign, heightAlign);
    }

    private uint[] ComputeSurfaceInfoLinear(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        expPitch = pitch;
        expHeight = height;
        expNumSlices = numSlices;

        uint valid = 1;
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);

        uint baseAlign, pitchAlign, heightAlign, slices;
        uint pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign;

        var compAllignLinear = ComputeSurfaceAlignmentsLinear(tileMode, bpp, flags);
        baseAlign = compAllignLinear.Item1;
        pitchAlign = compAllignLinear.Item2;
        heightAlign = compAllignLinear.Item3;

        if ((((flags >> 9) & 1) != 0) && (mipLevel == 0))
        {
            expPitch /= 3;
            expPitch = NextPow2(expPitch);
        }

        if (mipLevel != 0)
        {
            expPitch = NextPow2(expPitch);
            expHeight = NextPow2(expHeight);

            if (((flags >> 4) & 1) != 0)
            {
                expNumSlices = numSlices;

                padDims = numSlices <= 1 
                    ? 2 
                    : (uint)0;
            }
            else
                expNumSlices = NextPow2(numSlices);
        }

        var padimens = PadDimensions(
        tileMode,
        padDims,
        (flags >> 4) & 1,
        pitchAlign,
        heightAlign,
        microTileThickness);

        expPitch = padimens.Item1;
        expHeight = padimens.Item2;
        expNumSlices = padimens.Item3;

        if ((((flags >> 9) & 1) != 0) && (mipLevel == 0))
            expPitch *= 3;

        slices = expNumSlices * numSamples / microTileThickness;
        pPitchOut = expPitch;
        pHeightOut = expHeight;
        pNumSlicesOut = expNumSlices;
        pSurfSize = ((expHeight * expPitch * slices * bpp * numSamples) + 7) / 8;
        pBaseAlign = baseAlign;
        pPitchAlign = pitchAlign;
        pHeightAlign = heightAlign;
        pDepthAlign = microTileThickness;

        return [valid, pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign];
    }

    private static uint ComputeSurfaceTileSlices(uint tileMode, uint bpp, uint numSamples)
    {
        uint bytePerSample = ((bpp << 6) + 7) >> 3;
        uint tileSlices = 1;
        uint samplePerTile;

        if (ComputeSurfaceThickness((AddrTileMode)tileMode) > 1)
            numSamples = 4;

        if (bytePerSample != 0)
        {
            samplePerTile = 2048 / bytePerSample;
            if (samplePerTile < numSamples)
                tileSlices = Math.Max(1, numSamples / samplePerTile);
        }

        return tileSlices;
    }

    private static uint ConvertToNonBankSwappedMode(AddrTileMode tileMode)
    {
        return (uint)tileMode switch
        {
            8 => 4,
            9 => 5,
            10 => 6,
            11 => 7,
            14 => 12,
            15 => 13,
            _ => (uint)tileMode,
        };
    }

    private static uint ComputeSurfaceMipLevelTileMode(uint baseTileMode, uint bpp, uint level, uint width, uint height,
            uint numSlices, uint numSamples, uint isDepth, uint noRecursive)
    {
        uint widthAlignFactor = 1;
        uint macroTileWidth = 32;
        uint macroTileHeight = 16;
        uint tileSlices = ComputeSurfaceTileSlices(baseTileMode, bpp, numSamples);
        uint expTileMode = baseTileMode;

        uint widtha, heighta, numSlicesa, thickness, microTileBytes;

        if (numSamples > 1 || tileSlices > 1 || isDepth != 0)
        {
            if (baseTileMode == 7)
                expTileMode = 4;
            else if (baseTileMode == 13)
                expTileMode = 12;
            else if (baseTileMode == 11)
                expTileMode = 8;
            else if (baseTileMode == 15)
                expTileMode = 14;
        }

        if (baseTileMode == 2 && numSamples > 1)
        {
            expTileMode = 4;
        }
        else if (baseTileMode == 3)
        {
            if (numSamples > 1 || isDepth != 0)
                expTileMode = 2;

            if (numSamples is 2 or 4)
                expTileMode = 7;
        }
        else
        {
            expTileMode = baseTileMode;
        }

        if (noRecursive != 0 || level == 0)
            return expTileMode;

        switch (bpp)
        {
            case 24:
            case 48:
            case 96:
                bpp /= 3;
                break;
        }

        widtha = NextPow2(width);
        heighta = NextPow2(height);
        numSlicesa = NextPow2(numSlices);

        expTileMode = ConvertToNonBankSwappedMode((AddrTileMode)expTileMode);
        thickness = ComputeSurfaceThickness((AddrTileMode)expTileMode);
        microTileBytes = ((numSamples * bpp * (thickness << 6)) + 7) >> 3;

        if (microTileBytes < 256)
        {
            widthAlignFactor = Math.Max(1, 256 / microTileBytes);
        }

        if (expTileMode is 4 or 12)
        {
            if ((widtha < widthAlignFactor * macroTileWidth) || heighta < macroTileHeight)
                expTileMode = 2;
        }
        else if (expTileMode == 5)
        {
            macroTileWidth = 16;
            macroTileHeight = 32;

            if ((widtha < widthAlignFactor * macroTileWidth) || heighta < macroTileHeight)
                expTileMode = 2;
        }
        else if (expTileMode == 6)
        {
            macroTileWidth = 8;
            macroTileHeight = 64;

            if ((widtha < widthAlignFactor * macroTileWidth) || heighta < macroTileHeight)
                expTileMode = 2;
        }
        else if (expTileMode is 7 or 13)
        {
            if ((widtha < widthAlignFactor * macroTileWidth) || heighta < macroTileHeight)
                expTileMode = 3;
        }

        if (numSlicesa < 4)
        {
            if (expTileMode == 3)
                expTileMode = 2;
            else if (expTileMode == 7)
                expTileMode = 4;
            else if (expTileMode == 13)
                expTileMode = 12;
        }

        return ComputeSurfaceMipLevelTileMode(
            expTileMode,
            bpp,
            level,
            widtha,
            heighta,
            numSlicesa,
            numSamples,
            isDepth,
            1);
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
