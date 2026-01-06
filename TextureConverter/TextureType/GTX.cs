using Pfim;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using TextureConverter.Enums;
using TextureConverter.TextureConverterHelpers;

using static TextureConverter.TextureType.DDS;

namespace TextureConverter.TextureType;

/** To anyone reading this, I'm sorry for the mess.
 * GTX files for some reason are just crazy to work with.
 * If you're so brave to try and understand this, good luck.
 * And I truly am sorry for the pain you're about to endure.
 */
public class GTX
{
    // GX2SurfaceFormat has been moved to TextureConverter.Enums.GtxEnums
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

    // formatHwInfo moved into TextureConverter.TextureConverterHelpers.GtxFormatLookup (immutable lookup)
    // See GtxFormatLookup.HwFlat for the original data; use GtxFormatLookup.GetHwEntry/getBPP instead.

    // formatExInfo moved into TextureConverter.TextureConverterHelpers.GtxFormatLookup (immutable lookup)
    // See GtxFormatLookup.ExFlat for the original data; use GtxFormatLookup.GetExEntry instead.

    public static uint GetBPP(GX2SurfaceFormat format)
    {
        return GtxFormatLookup.GetBPP(format);
    }

    private GTXHeader? header = null;
    public List<GX2Surface> GTXSurfaces { get; private set; } = [];
    public List<byte[]> ImageDatas { get; private set; } = [];
    public Dictionary<uint, byte[]> MipDatas { get; private set; } = [];
    public List<GTXDataBlock> Blocks { get; private set; } = [];

    public void LoadFile(Stream data)
    {
        using EndianBinaryReader reader = new(data, Encoding.Default, true, true);
        var parsed = GtxReader.Parse(reader);
        header = parsed.Header;
        GTXSurfaces = parsed.Surfaces;
        ImageDatas = parsed.ImageDatas;
        MipDatas = parsed.MipDatas;
        Blocks = parsed.Blocks;
    }

    public Image<Bgra32> ConvertToImage()
    {
        (byte[][] data, byte[] hdr) = DeswizzleData(0);

        byte[] output = [.. hdr, .. data.SelectMany(x => x)];

        MemoryStream memoryStream = new(output);

        // Using pfim, we can convert from DDS to PNG
        using IImage image = Pfimage.FromStream(memoryStream);
        return image.Format == ImageFormat.Rgba32
            ? Image.LoadPixelData<Bgra32>(image.Data, image.Width, image.Height)
            : throw new Exception("Image is not in Rgba32 format!");
    }

    public static Image<Bgra32> GetImage(Stream data)
    {
        GTX gtx = new();
        gtx.LoadFile(data);

        return gtx.ConvertToImage();
    }

    SurfaceIn pIn = new();
    SurfaceOut pOut = new();

    public (byte[][] data, byte[] hdr) DeswizzleData(int i)
    {
        GX2Surface texInfo = GTXSurfaces[i];

        // Try to get the mip data, else empty byte array
        byte[] mipData = MipDatas.TryGetValue((uint)i, out byte[]? mip) ? mip : [];

        if (texInfo.AA != 0)
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

        //uint bpp = GetBPP(texInfo.Format);

        if (!Enum.IsDefined(texInfo.Format) || texInfo.Format == GX2SurfaceFormat.GX2_SURFACE_FORMAT_INVALID)
            throw new Exception("Invalid format!");

        uint tilingDepth = surfOut.Depth;

        if (surfOut.TileMode == 3)
            tilingDepth >>= 2;

        if (tilingDepth != 1)
            throw new Exception("Unsupported tiling depth!");

        byte[][] result = new byte[texInfo.MipCount][];

        long baseSurfSize = surfOut.SurfSize;

        for (int level = 0; level < texInfo.MipCount; level++)
        {
            uint mipWidth = Math.Max(1, texInfo.Width >> level);
            uint mipHeight = Math.Max(1, texInfo.Height >> level);

            // Get aligned info for the current mip level
            SurfaceOut currentMipSurf = GetSurfaceInfo(texInfo.Format, texInfo.Width, texInfo.Height, texInfo.Depth, texInfo.Dim, texInfo.TileMode, texInfo.AA, level);

            byte[] sourceBuffer;

            if (level == 0)
            {
                sourceBuffer = ImageDatas[i];
            }
            else
            {
                ulong mipOffset = texInfo.MipOffsets[level - 1];

                // IMPORTANT: Only Level 1 is stored relative to the end of Level 0 in most GX2 containers
                if (level == 1 && mipOffset >= (ulong)baseSurfSize)
                    mipOffset -= (ulong)baseSurfSize;

                int start = (int)mipOffset;
                int length = (int)currentMipSurf.SurfSize;

                sourceBuffer = mipData.Length >= start + length
                    ? mipData[start..(start + length)]
                    : [];
            }

            if (sourceBuffer.Length < currentMipSurf.SurfSize)
            {
                byte[] padded = new byte[currentMipSurf.SurfSize];
                Array.Copy(sourceBuffer, 0, padded, 0, Math.Min(sourceBuffer.Length, (int)currentMipSurf.SurfSize));
                sourceBuffer = padded;
            }

            // Replace hardcoded '1' with currentMipSurf.Depth
            byte[] mipResult = Deswizzle(
                mipWidth,
                mipHeight,
                currentMipSurf.Depth,
                texInfo.Format,
                texInfo.TileMode,
                texInfo.AA,
                texInfo.Use,
                texInfo.Swizzle,
                currentMipSurf.Pitch,
                currentMipSurf.Bpp, // Pass the bit count from SurfOut
                0, 0, sourceBuffer);

            result[level] = mipResult;
        }

        byte[] hdr = GenerateHeader(texInfo.MipCount, texInfo.Width, texInfo.Height, ddsFormat, texInfo.CompSel, texInfo.RealSize);

        return (result, hdr);
    }

    private static byte[] Deswizzle(uint mipWidth, uint mipHeight, uint depth, GX2SurfaceFormat format, uint tileMode, uint aa, uint use, uint swizzle, uint pitch, uint bpp, uint slice, uint sample, byte[] data)
    {
        return SwizzleSurface(mipWidth, mipHeight, depth, format, tileMode, aa, use, swizzle, pitch, bpp, slice, sample, data, false);
    }

    private static byte[] SwizzleSurface(uint width, uint height, uint depth, GX2SurfaceFormat format, uint tileMode, uint aa, uint use, uint swizzle, uint pitch, uint bitsPerPixel, uint slice, uint sample, byte[] data, bool doSwizzle)
    {
        uint bytesPerPixel = bitsPerPixel / 8;
        if (bytesPerPixel == 0)
            bytesPerPixel = 1; // Fallback for very low bpp

        byte[] result = new byte[data.Length];

        if (BCnFormats.Contains(format))
        {
            width = (width + 3) / 4;
            height = (height + 3) / 4;
        }

        uint pipeSwizzle = (swizzle >> 8) & 0x1;
        uint bankSwizzle = (swizzle >> 9) & 0x3;

        // Linear Aligned (16) and Linear Special (0) are handled as linear (0/1)
        uint addrTileMode = (tileMode == 16) ? 0u : tileMode;
        bool isDepth = (use & 4) != 0;
        uint numSamples = (uint)(1 << (int)aa);

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                ulong pos = addrTileMode switch
                {
                    0 or 1 => ComputeSurfaceAddrFromCoordLinear(x, y, slice, sample, bytesPerPixel, pitch, height, depth),
                    2 or 3 => ComputeSurfaceAddrFromCoordMicroTiled(x, y, slice, bitsPerPixel, pitch, height, addrTileMode, isDepth),
                    _ => GtxSwizzleUtils.ComputeSurfaceAddrFromCoordMacroTiled(x, y, slice, sample, bitsPerPixel, pitch, height, numSamples, addrTileMode, isDepth, pipeSwizzle, bankSwizzle),
                };
                ulong pos2 = (((ulong)y * width) + x) * bytesPerPixel;

                if (pos2 + bytesPerPixel <= (ulong)result.Length && pos + bytesPerPixel <= (ulong)data.Length)
                {
                    if (doSwizzle)
                        Array.Copy(data, (int)pos2, result, (int)pos, (int)bytesPerPixel);
                    else
                        Array.Copy(data, (int)pos, result, (int)pos2, (int)bytesPerPixel);
                }
            }
        }

        return result;
    }

    private static ulong ComputeSurfaceAddrFromCoordMicroTiled(uint x, uint y, uint slice, uint bpp, uint pitch, uint height, uint tileMode, bool isDepth)
    {
        return GtxSwizzleUtils.ComputeSurfaceAddrFromCoordMicroTiled(x, y, slice, bpp, pitch, height, tileMode, isDepth);
    }

    private static ulong ComputeSurfaceAddrFromCoordLinear(uint x, uint y, uint slice, uint sample, uint bpp, uint pitch, uint height, uint depth)
    {
        return GtxSwizzleUtils.ComputeSurfaceAddrFromCoordLinear(x, y, slice, sample, bpp, pitch, height, depth);
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
        uint dim;
        uint width;
        uint blockSize;
        uint numSamples;
        uint hwFormat;

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

            pSurfOut.Bpp = GtxFormatLookup.GetHwEntry((int)hwFormat, 0);
            pSurfOut.Size = 96;
            pSurfOut.Pitch = width / blockSize;
            pSurfOut.PixelBits = GtxFormatLookup.GetHwEntry((int)hwFormat, 0);
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
            aSurfIn.Bpp = GtxFormatLookup.GetHwEntry((int)hwFormat, 0);
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

        // Delegate to surface calculator which handles all the complex math
        GtxSurfaceCalculator.ComputeSurfaceInfo(pIn, pOut);
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

    public static (uint Bpp, uint ExpandX, uint ExpandY, uint ElemMode) GetBitsPerPixel(int format)
    {
        return (
            GtxFormatLookup.GetExEntry(format, 0),
            GtxFormatLookup.GetExEntry(format, 1),
            GtxFormatLookup.GetExEntry(format, 2),
            GtxFormatLookup.GetExEntry(format, 3)
        );
    }

    public static uint NextPow2(uint dim)
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

        public GTXHeader(EndianBinaryReader reader)
        {
            long dataOffset = reader.BaseStream.Position;
            string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (signature != "Gfx2")
                throw new Exception($"Invalid signature {signature}! Expected Gfx2.");

            HeaderSize = reader.ReadUInt32();
            MajorVersion = reader.ReadUInt32();
            MinorVersion = reader.ReadUInt32();
            GpuVersion = reader.ReadUInt32();
            AlignMode = reader.ReadUInt32();

            reader.BaseStream.Seek(dataOffset + HeaderSize, SeekOrigin.Begin);
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

        public GTXDataBlock(EndianBinaryReader reader, bool shiftedType = false)
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

            DataSize = reader.ReadUInt32();
            Identifier = reader.ReadUInt32();
            Index = reader.ReadUInt32();

            reader.BaseStream.Seek(pos + HeaderSize, SeekOrigin.Begin);
            Data = reader.ReadBytes((int)DataSize);

            reader.BaseStream.Seek(pos + HeaderSize + DataSize, SeekOrigin.Begin);
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

        public GX2Surface(EndianBinaryReader reader)
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
                MipOffsets[i] = reader.ReadUInt32();
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