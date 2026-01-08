using static TextureConverter.TextureType.GTX;

namespace TextureConverter.TextureConverterHelpers;

/// <summary>
/// GX2 texture swizzling/deswizzling for WiiU.
/// Ported from AboodXD's GTX Extractor and Switch Toolbox.
/// https://github.com/aboood40091/GTX-Extractor
/// https://github.com/KillzXGaming/Switch-Toolbox
/// </summary>
public static class GX2Swizzle
{
    public const uint SwizzleMask = 0xFF00FF;

    #region Format Info Tables

    public static readonly byte[] FormatHwInfo =
    [
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

    private static readonly byte[] FormatExInfo =
    [
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

    private static readonly byte[] BankSwapOrder = [0, 1, 3, 2, 6, 7, 5, 4, 0, 0];

    #endregion

    #region Surface Info Structure

    public class SurfaceOut
    {
        public uint Size;
        public uint Pitch;
        public uint Height;
        public uint Depth;
        public long SurfSize;
        public uint TileMode;
        public uint BaseAlign;
        public uint PitchAlign;
        public uint HeightAlign;
        public uint DepthAlign;
        public uint Bpp;
        public uint PixelPitch;
        public uint PixelHeight;
        public uint PixelBits;
        public uint SliceSize;
        public uint PitchTileMax;
        public uint HeightTileMax;
        public uint SliceTileMax;
        public uint TileType;
        public int TileIndex;
    }

    private class SurfaceIn
    {
        public uint Size;
        public uint TileMode;
        public uint Format;
        public uint Bpp;
        public uint NumSamples;
        public uint Width;
        public uint Height;
        public uint NumSlices;
        public uint Slice;
        public uint MipLevel;
        public uint Flags;
        public uint NumFrags;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Deswizzles texture data from a GX2Surface.
    /// </summary>
    public static byte[] Deswizzle(GX2Surface surface, int arrayLevel, int mipLevel)
    {
        uint blkWidth = IsFormatBCN(surface.Format) ? 4u : 1u;
        uint blkHeight = IsFormatBCN(surface.Format) ? 4u : 1u;

        uint bpp = GetBitsPerPixel(surface.Format);
        uint bytesPerPixel = bpp / 8;

        uint width = Math.Max(1, surface.Width >> mipLevel);
        uint height = Math.Max(1, surface.Height >> mipLevel);

        SurfaceOut surfInfo = GetSurfaceInfo(surface.Format, surface.Width, surface.Height, surface.Depth,
            (uint)surface.Dim, (uint)surface.TileMode, (uint)surface.AA, mipLevel);

        uint swizzle = surface.Swizzle;

        byte[] data;
        if (mipLevel == 0)
        {
            data = surface.Data;
        }
        else if (surface.MipData != null && surface.MipData.Length > 0 && surface.MipOffsets != null)
        {
            uint mipOffset = surface.MipOffsets[mipLevel - 1];
            if (mipLevel == 1)
            {
                SurfaceOut baseSurfInfo = GetSurfaceInfo(surface.Format, surface.Width, surface.Height, surface.Depth,
                    (uint)surface.Dim, (uint)surface.TileMode, (uint)surface.AA, 0);
                mipOffset -= (uint)baseSurfInfo.SurfSize;
            }

            int dataLen = (int)Math.Min(surfInfo.SliceSize, surface.MipData.Length - mipOffset);
            data = new byte[surfInfo.SliceSize];
            if (dataLen > 0)
                Array.Copy(surface.MipData, mipOffset, data, 0, dataLen);
        }
        else
        {
            data = surface.Data;
        }

        // For macro-tiled textures, use the pitch and height values from the file header, not the recalculated aligned values.
        // The file stores the data using the original pitch/height, not the alignment-padded dimensions.
        uint actualPitch = surfInfo.Pitch;
        uint actualHeight = surfInfo.Height;
        if (mipLevel == 0 && (uint)surface.TileMode > 3 && (uint)surface.TileMode != 16)
        {
            // Macro-tiled mode - use the values from the file
            actualPitch = surface.Pitch;
            actualHeight = surface.Height;
        }

        return DeswizzleSurface(width, height, surfInfo.Depth, actualHeight,
            (uint)surface.Format, (uint)surface.AA, surface.Use, surfInfo.TileMode,
            swizzle, actualPitch, surfInfo.Bpp, (uint)arrayLevel, 0, data);
    }

    /// <summary>
    /// Swizzles texture data for a GX2Surface.
    /// </summary>
    public static byte[] Swizzle(GX2Surface surface, byte[] data, int arrayLevel, int mipLevel)
    {
        uint width = Math.Max(1, surface.Width >> mipLevel);
        uint height = Math.Max(1, surface.Height >> mipLevel);

        SurfaceOut surfInfo = GetSurfaceInfo(surface.Format, surface.Width, surface.Height, surface.Depth,
            (uint)surface.Dim, (uint)surface.TileMode, (uint)surface.AA, mipLevel);

        return SwizzleSurface(width, height, surfInfo.Depth, surfInfo.Height,
            (uint)surface.Format, (uint)surface.AA, surface.Use, surfInfo.TileMode,
            surface.Swizzle, surfInfo.Pitch, surfInfo.Bpp, (uint)arrayLevel, 0, data, true);
    }

    /// <summary>
    /// Gets surface information for a GX2 texture.
    /// </summary>
    public static SurfaceOut GetSurfaceInfo(GX2SurfaceFormat format, uint width, uint height,
        uint depth, uint surfaceDim, uint tileMode, uint aa, int level)
    {
        uint hwFormat = (uint)format & 0x3F;
        SurfaceOut pSurfOut = new() { Size = 96 };

        if (tileMode == 16) // Linear special
        {
            uint numSamples = (uint)(1 << (int)aa);
            uint blockSize = hwFormat is < 0x31 or > 0x35 ? 1u : 4u;

            width = ~(blockSize - 1) & (Math.Max(1, width >> level) + blockSize - 1);

            pSurfOut.Bpp = FormatHwInfo[hwFormat * 4];
            pSurfOut.Pitch = width / blockSize;
            pSurfOut.PixelBits = FormatHwInfo[hwFormat * 4];
            pSurfOut.BaseAlign = 1;
            pSurfOut.PitchAlign = 1;
            pSurfOut.HeightAlign = 1;
            pSurfOut.DepthAlign = 1;

            switch (surfaceDim)
            {
                case 0:
                    pSurfOut.Height = 1;
                    pSurfOut.Depth = 1;
                    break;
                case 1:
                case 6:
                    pSurfOut.Height = Math.Max(1, height >> level);
                    pSurfOut.Depth = 1;
                    break;
                case 2:
                    pSurfOut.Height = Math.Max(1, height >> level);
                    pSurfOut.Depth = Math.Max(1, depth >> level);
                    break;
                case 3:
                    pSurfOut.Height = Math.Max(1, height >> level);
                    pSurfOut.Depth = Math.Max(6, depth);
                    break;
                case 4:
                    pSurfOut.Height = 1;
                    pSurfOut.Depth = depth;
                    break;
                case 5:
                case 7:
                    pSurfOut.Height = Math.Max(1, height >> level);
                    pSurfOut.Depth = depth;
                    break;
            }

            pSurfOut.PixelPitch = width;
            pSurfOut.PixelHeight = ~(blockSize - 1) & (pSurfOut.Height + blockSize - 1);
            pSurfOut.Height = pSurfOut.PixelHeight / blockSize;
            pSurfOut.SurfSize = (pSurfOut.Bpp * numSamples * pSurfOut.Depth * pSurfOut.Height * pSurfOut.Pitch) >> 3;

            pSurfOut.SliceSize = surfaceDim == 2 ? (uint)pSurfOut.SurfSize : (uint)(pSurfOut.SurfSize / pSurfOut.Depth);
            pSurfOut.PitchTileMax = (pSurfOut.Pitch >> 3) - 1;
            pSurfOut.HeightTileMax = (pSurfOut.Height >> 3) - 1;
            pSurfOut.SliceTileMax = ((pSurfOut.Height * pSurfOut.Pitch) >> 6) - 1;
        }
        else
        {
            ComputeSurfaceInfo(hwFormat, width, height, depth, surfaceDim, tileMode, aa, level, pSurfOut);
        }

        if (pSurfOut.TileMode == 0)
            pSurfOut.TileMode = 16;

        return pSurfOut;
    }

    #endregion

    #region Private Swizzle Methods

    private static byte[] DeswizzleSurface(uint width, uint height, uint depth, uint height_,
        uint format, uint aa, uint use, uint tileMode, uint swizzle,
        uint pitch, uint bitsPerPixel, uint slice, uint sample, byte[] data)
    {
        return SwizzleSurface(width, height, depth, height_, format, aa, use, tileMode,
            swizzle, pitch, bitsPerPixel, slice, sample, data, false);
    }

    private static byte[] SwizzleSurface(uint width, uint height, uint depth, uint height_,
        uint format, uint aa, uint use, uint tileMode, uint swizzle,
        uint pitch, uint bitsPerPixel, uint slice, uint sample, byte[] data, bool toSwizzled)
    {
        uint bytesPerPixel = bitsPerPixel / 8;
        byte[] result = new byte[data.Length];

        if (IsFormatBCN((GX2SurfaceFormat)format))
        {
            width = (width + 3) / 4;
            height = (height + 3) / 4;
        }

        uint pipeSwizzle = (swizzle >> 8) & 1;
        uint bankSwizzle = (swizzle >> 9) & 3;

        tileMode = GX2TileModeToAddrTileMode(tileMode);
        bool isDepth = (use & 4) != 0;
        uint numSamples = (uint)(1 << (int)aa);

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                ulong pos;
                if (tileMode is 0 or 1)
                {
                    pos = ComputeSurfaceAddrFromCoordLinear(x, y, slice, sample, bytesPerPixel, pitch, height_, depth);
                }
                else if (tileMode is 2 or 3)
                {
                    pos = ComputeSurfaceAddrFromCoordMicroTiled(x, y, slice, bitsPerPixel, pitch, height_, tileMode, isDepth);
                }
                else
                {
                    pos = ComputeSurfaceAddrFromCoordMacroTiled(x, y, slice, sample, bitsPerPixel, pitch, height_,
                        numSamples, tileMode, isDepth, pipeSwizzle, bankSwizzle);
                }

                uint pos_ = ((y * width) + x) * bytesPerPixel;

                if (toSwizzled)
                {
                    if (pos_ + bytesPerPixel <= data.Length && pos + bytesPerPixel <= (ulong)result.Length)
                    {
                        for (uint n = 0; n < bytesPerPixel; n++)
                            result[pos + n] = data[pos_ + n];
                    }
                }
                else
                {
                    if (pos + bytesPerPixel <= (ulong)data.Length && pos_ + bytesPerPixel <= result.Length)
                    {
                        for (uint n = 0; n < bytesPerPixel; n++)
                            result[pos_ + n] = data[pos + n];
                    }
                }
            }
        }

        return result;
    }

    #endregion

    #region Address Computation

    private static ulong ComputeSurfaceAddrFromCoordLinear(uint x, uint y, uint slice, uint sample,
        uint bpp, uint pitch, uint height, uint numSlices)
    {
        uint sliceOffset = pitch * height * (slice + (sample * numSlices));
        return ((y * pitch) + x + sliceOffset) * bpp;
    }

    private static ulong ComputeSurfaceAddrFromCoordMicroTiled(uint x, uint y, uint slice,
        uint bpp, uint pitch, uint height, uint tileMode, bool isDepth)
    {
        int microTileThickness = tileMode == 3 ? 4 : 1;
        uint microTileBytes = (uint)((64 * microTileThickness * bpp) + 7) / 8;
        uint microTilesPerRow = pitch >> 3;
        uint microTileIndexX = x >> 3;
        uint microTileIndexY = y >> 3;
        uint microTileIndexZ = slice / (uint)microTileThickness;

        ulong microTileOffset = microTileBytes * (microTileIndexX + (microTileIndexY * microTilesPerRow));
        ulong sliceBytes = (ulong)((pitch * height * microTileThickness * bpp) + 7) / 8;
        ulong sliceOffset = microTileIndexZ * sliceBytes;

        uint pixelIndex = ComputePixelIndexWithinMicroTile(x, y, slice, bpp, tileMode, isDepth);
        ulong pixelOffset = (bpp * pixelIndex) >> 3;

        return pixelOffset + microTileOffset + sliceOffset;
    }

    private static ulong ComputeSurfaceAddrFromCoordMacroTiled(uint x, uint y, uint slice, uint sample,
        uint bpp, uint pitch, uint height, uint numSamples, uint tileMode, bool isDepth,
        uint pipeSwizzle, uint bankSwizzle)
    {
        uint microTileThickness = ComputeSurfaceThickness(tileMode);
        uint microTileBits = numSamples * bpp * microTileThickness * 64;
        uint microTileBytes = (microTileBits + 7) / 8;

        uint pixelIndex = ComputePixelIndexWithinMicroTile(x, y, slice, bpp, tileMode, isDepth);
        uint bytesPerSample = microTileBytes / numSamples;

        uint sampleOffset, pixelOffset;
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

        uint elemOffset = pixelOffset + sampleOffset;
        uint samplesPerSlice, numSampleSplits, sampleSlice;

        if (numSamples <= 1 || microTileBytes <= 2048)
        {
            numSampleSplits = 1;
            sampleSlice = 0;
        }
        else
        {
            samplesPerSlice = 2048 / bytesPerSample;
            numSampleSplits = numSamples / samplesPerSlice;
            numSamples = samplesPerSlice;

            uint tileSliceBits = microTileBits / numSampleSplits;
            sampleSlice = elemOffset / tileSliceBits;
            elemOffset %= tileSliceBits;
        }

        elemOffset = (elemOffset + 7) / 8;

        uint pipe = ComputePipeFromCoordWoRotation(x, y);
        uint bank = ComputeBankFromCoordWoRotation(x, y);

        uint swizzle_ = pipeSwizzle + (2 * bankSwizzle);
        uint bankPipe = pipe + (2 * bank);
        uint rotation = ComputeSurfaceRotationFromTileMode(tileMode);
        uint sliceIn = slice;

        if (IsThickMacroTiled(tileMode) != 0)
            sliceIn >>= 2;

        bankPipe ^= (2 * sampleSlice * 3) ^ (swizzle_ + (sliceIn * rotation));
        bankPipe %= 8;

        pipe = bankPipe % 2;
        bank = bankPipe / 2;

        uint sliceBytes = ((height * pitch * microTileThickness * bpp * numSamples) + 7) / 8;
        uint sliceOffset = sliceBytes * (sampleSlice + (numSampleSplits * slice)) / microTileThickness;

        uint macroTilePitch = 32;
        uint macroTileHeight = 16;

        switch (tileMode)
        {
            case 5:
            case 9:
                macroTilePitch = 16;
                macroTileHeight = 32;
                break;
            case 6:
            case 10:
                macroTilePitch = 8;
                macroTileHeight = 64;
                break;
        }

        uint macroTilesPerRow = pitch / macroTilePitch;
        uint macroTileBytes = ((numSamples * microTileThickness * bpp * macroTileHeight * macroTilePitch) + 7) / 8;
        uint macroTileIndexX = x / macroTilePitch;
        uint macroTileIndexY = y / macroTileHeight;
        ulong macroTileOffset = (macroTileIndexX + (macroTilesPerRow * macroTileIndexY)) * macroTileBytes;

        if (IsBankSwappedTileMode(tileMode) != 0)
        {
            uint bankSwapWidth = ComputeSurfaceBankSwappedWidth(tileMode, bpp, 1, pitch);
            uint swapIndex = macroTilePitch * macroTileIndexX / bankSwapWidth;
            bank ^= BankSwapOrder[swapIndex & 3];
        }

        ulong totalOffset = elemOffset + ((macroTileOffset + sliceOffset) >> 3);
        return ((ulong)bank << 9) | ((ulong)pipe << 8) | (totalOffset & 255) | (ulong)(((long)totalOffset & -256) << 3);
    }

    #endregion

    #region Helper Methods

    private static uint GetBitsPerPixel(GX2SurfaceFormat format)
    {
        uint formatVal = (uint)format & 0x3F;
        return FormatHwInfo[formatVal * 4];
    }

    private static uint GX2TileModeToAddrTileMode(uint tileMode)
    {
        if (tileMode == 0)
            throw new InvalidOperationException("Use tileMode from GetDefaultGX2TileMode().");
        return tileMode == 16 ? 0 : tileMode;
    }

    private static uint ComputeSurfaceThickness(uint tileMode)
    {
        return tileMode switch
        {
            3 or 7 or 11 or 13 or 15 => 4,
            16 or 17 => 8,
            _ => 1
        };
    }

    private static uint ComputePixelIndexWithinMicroTile(uint x, uint y, uint z, uint bpp, uint tileMode, bool isDepth)
    {
        uint pixelBit6 = 0, pixelBit7 = 0, pixelBit8 = 0;
        uint thickness = ComputeSurfaceThickness(tileMode);

        uint pixelBit0;
        uint pixelBit1;
        uint pixelBit2;
        uint pixelBit3;
        uint pixelBit4;
        uint pixelBit5;
        if (isDepth)
        {
            pixelBit0 = x & 1;
            pixelBit1 = y & 1;
            pixelBit2 = (x & 2) >> 1;
            pixelBit3 = (y & 2) >> 1;
            pixelBit4 = (x & 4) >> 2;
            pixelBit5 = (y & 4) >> 2;
        }
        else
        {
            switch (bpp)
            {
                case 8:
                    pixelBit0 = x & 1;
                    pixelBit1 = (x & 2) >> 1;
                    pixelBit2 = (x & 4) >> 2;
                    pixelBit3 = (y & 2) >> 1;
                    pixelBit4 = y & 1;
                    pixelBit5 = (y & 4) >> 2;
                    break;
                case 0x10:
                    pixelBit0 = x & 1;
                    pixelBit1 = (x & 2) >> 1;
                    pixelBit2 = (x & 4) >> 2;
                    pixelBit3 = y & 1;
                    pixelBit4 = (y & 2) >> 1;
                    pixelBit5 = (y & 4) >> 2;
                    break;
                case 0x20:
                case 0x60:
                    pixelBit0 = x & 1;
                    pixelBit1 = (x & 2) >> 1;
                    pixelBit2 = y & 1;
                    pixelBit3 = (x & 4) >> 2;
                    pixelBit4 = (y & 2) >> 1;
                    pixelBit5 = (y & 4) >> 2;
                    break;
                case 0x40:
                    pixelBit0 = x & 1;
                    pixelBit1 = y & 1;
                    pixelBit2 = (x & 2) >> 1;
                    pixelBit3 = (x & 4) >> 2;
                    pixelBit4 = (y & 2) >> 1;
                    pixelBit5 = (y & 4) >> 2;
                    break;
                case 0x80:
                    pixelBit0 = y & 1;
                    pixelBit1 = x & 1;
                    pixelBit2 = (x & 2) >> 1;
                    pixelBit3 = (x & 4) >> 2;
                    pixelBit4 = (y & 2) >> 1;
                    pixelBit5 = (y & 4) >> 2;
                    break;
                default:
                    pixelBit0 = x & 1;
                    pixelBit1 = (x & 2) >> 1;
                    pixelBit2 = y & 1;
                    pixelBit3 = (x & 4) >> 2;
                    pixelBit4 = (y & 2) >> 1;
                    pixelBit5 = (y & 4) >> 2;
                    break;
            }
        }

        if (thickness > 1)
        {
            pixelBit6 = z & 1;
            pixelBit7 = (z & 2) >> 1;
        }

        if (thickness == 8)
            pixelBit8 = (z & 4) >> 2;

        return (pixelBit8 << 8) | (pixelBit7 << 7) | (pixelBit6 << 6) |
               (32 * pixelBit5) | (16 * pixelBit4) | (8 * pixelBit3) |
               (4 * pixelBit2) | pixelBit0 | (2 * pixelBit1);
    }

    private static uint ComputePipeFromCoordWoRotation(uint x, uint y)
    {
        return ((y >> 3) ^ (x >> 3)) & 1;
    }

    private static uint ComputeBankFromCoordWoRotation(uint x, uint y)
    {
        return (((y >> 5) ^ (x >> 3)) & 1) | (2 * (((y >> 4) ^ (x >> 4)) & 1));
    }

    private static uint ComputeSurfaceRotationFromTileMode(uint tileMode)
    {
        return tileMode switch
        {
            4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 => 2,
            12 or 13 or 14 or 15 => 1,
            _ => 0
        };
    }

    private static uint IsThickMacroTiled(uint tileMode)
    {
        return tileMode switch
        {
            7 or 11 or 13 or 15 => 1,
            _ => 0
        };
    }

    private static uint IsBankSwappedTileMode(uint tileMode)
    {
        return tileMode switch
        {
            8 or 9 or 10 or 11 or 14 or 15 => 1,
            _ => 0
        };
    }

    private static uint ComputeMacroTileAspectRatio(uint tileMode)
    {
        return tileMode switch
        {
            5 or 9 => 2,
            6 or 10 => 4,
            _ => 1
        };
    }

    private static uint ComputeSurfaceBankSwappedWidth(uint tileMode, uint bpp, uint numSamples, uint pitch)
    {
        if (IsBankSwappedTileMode(tileMode) == 0)
            return 0;

        uint bytesPerSample = 8 * bpp;
        uint samplesPerTile = bytesPerSample != 0 ? 2048 / bytesPerSample : 0;
        uint slicesPerTile = Math.Max(1, numSamples / Math.Max(1, samplesPerTile));

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

    #endregion

    #region Surface Info Computation

    private static void ComputeSurfaceInfo(uint hwFormat, uint width, uint height, uint depth,
        uint surfaceDim, uint tileMode, uint aa, int level, SurfaceOut pSurfOut)
    {
        SurfaceIn aSurfIn = new()
        {
            Size = 60,
            TileMode = tileMode & 0x0F,
            Format = hwFormat,
            Bpp = FormatHwInfo[hwFormat * 4],
            NumSamples = (uint)(1 << (int)aa),
            Width = Math.Max(1, width >> level)
        };

        aSurfIn.NumFrags = aSurfIn.NumSamples;

        switch (surfaceDim)
        {
            case 0:
                aSurfIn.Height = 1;
                aSurfIn.NumSlices = 1;
                break;
            case 1:
            case 6:
                aSurfIn.Height = Math.Max(1, height >> level);
                aSurfIn.NumSlices = 1;
                break;
            case 2:
                aSurfIn.Height = Math.Max(1, height >> level);
                aSurfIn.NumSlices = Math.Max(1, depth >> level);
                break;
            case 3:
                aSurfIn.Height = Math.Max(1, height >> level);
                aSurfIn.NumSlices = Math.Max(6, depth);
                aSurfIn.Flags |= 0x10;
                break;
            case 4:
                aSurfIn.Height = 1;
                aSurfIn.NumSlices = depth;
                break;
            case 5:
            case 7:
                aSurfIn.Height = Math.Max(1, height >> level);
                aSurfIn.NumSlices = depth;
                break;
        }

        aSurfIn.Slice = 0;
        aSurfIn.MipLevel = (uint)level;

        if (surfaceDim == 2)
            aSurfIn.Flags |= 0x20;

        if (level == 0)
            aSurfIn.Flags = (1 << 12) | (aSurfIn.Flags & 0xFFFFEFFF);
        else
            aSurfIn.Flags &= 0xFFFFEFFF;

        ComputeSurfaceInfoEx(aSurfIn, pSurfOut);
    }

    private static void ComputeSurfaceInfoEx(SurfaceIn pIn, SurfaceOut pOut)
    {
        uint tileMode = pIn.TileMode;
        uint bpp = pIn.Bpp;
        uint numSamples = Math.Max(1, pIn.NumSamples);
        uint pitch = pIn.Width;
        uint height = pIn.Height;
        uint numSlices = pIn.NumSlices;
        uint mipLevel = pIn.MipLevel;
        uint flags = pIn.Flags;

        // Convert to non-bank-swapped mode if needed
        if ((flags & 0x40) != 0)
            tileMode = ConvertToNonBankSwappedMode(tileMode);
        else
            tileMode = ComputeSurfaceMipLevelTileMode(tileMode, bpp, mipLevel, pitch, height, numSlices, numSamples, (flags >> 1) & 1);

        switch (tileMode)
        {
            case 0:
            case 1:
                ComputeSurfaceInfoLinear(tileMode, bpp, numSamples, pitch, height, numSlices, mipLevel, flags, pOut);
                pOut.TileMode = tileMode;
                break;
            case 2:
            case 3:
                ComputeSurfaceInfoMicroTiled(tileMode, bpp, numSamples, pitch, height, numSlices, mipLevel, flags, pOut);
                break;
            default:
                ComputeSurfaceInfoMacroTiled(tileMode, tileMode, bpp, numSamples, pitch, height, numSlices, mipLevel, flags, pOut);
                break;
        }

        pOut.PitchTileMax = (pOut.Pitch >> 3) - 1;
        pOut.HeightTileMax = (pOut.Height >> 3) - 1;
        pOut.SliceTileMax = ((pOut.Height * pOut.Pitch) >> 6) - 1;
    }

    private static void ComputeSurfaceInfoLinear(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height,
        uint numSlices, uint mipLevel, uint flags, SurfaceOut pOut)
    {
        uint microTileThickness = ComputeSurfaceThickness(tileMode);
        ComputeSurfaceAlignmentsLinear(tileMode, bpp, flags, out uint baseAlign, out uint pitchAlign, out uint heightAlign);

        if (((flags >> 9) & 1) != 0 && mipLevel == 0)
            pitch /= 3;

        if (mipLevel != 0)
        {
            pitch = NextPow2(pitch);
            height = NextPow2(height);
            if (((flags >> 4) & 1) == 0)
                numSlices = NextPow2(numSlices);
        }

        PadDimensions(tileMode, (flags >> 4) & 1, pitchAlign, heightAlign, microTileThickness,
            ref pitch, ref height, ref numSlices);

        if (((flags >> 9) & 1) != 0 && mipLevel == 0)
            pitch *= 3;

        uint slices = numSlices * numSamples / microTileThickness;

        pOut.Pitch = pitch;
        pOut.Height = height;
        pOut.Depth = numSlices;
        pOut.SurfSize = ((height * pitch * slices * bpp * numSamples) + 7) / 8;
        pOut.BaseAlign = baseAlign;
        pOut.PitchAlign = pitchAlign;
        pOut.HeightAlign = heightAlign;
        pOut.DepthAlign = microTileThickness;
        pOut.Bpp = bpp;
        pOut.SliceSize = (uint)pOut.SurfSize / Math.Max(1, numSlices);
    }

    private static void ComputeSurfaceInfoMicroTiled(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height,
        uint numSlices, uint mipLevel, uint flags, SurfaceOut pOut)
    {
        uint microTileThickness = ComputeSurfaceThickness(tileMode);
        uint expTileMode = tileMode;

        if (mipLevel != 0)
        {
            pitch = NextPow2(pitch);
            height = NextPow2(height);
            if (((flags >> 4) & 1) == 0)
                numSlices = NextPow2(numSlices);

            if (expTileMode == 3 && numSlices < 4)
            {
                expTileMode = 2;
                microTileThickness = 1;
            }
        }

        ComputeSurfaceAlignmentsMicroTiled(expTileMode, bpp, flags, numSamples,
            out uint baseAlign, out uint pitchAlign, out uint heightAlign);

        PadDimensions(expTileMode, (flags >> 4) & 1, pitchAlign, heightAlign, microTileThickness,
            ref pitch, ref height, ref numSlices);

        pOut.Pitch = pitch;
        pOut.Height = height;
        pOut.Depth = numSlices;
        pOut.SurfSize = ((height * pitch * numSlices * bpp * numSamples) + 7) / 8;
        pOut.TileMode = expTileMode;
        pOut.BaseAlign = baseAlign;
        pOut.PitchAlign = pitchAlign;
        pOut.HeightAlign = heightAlign;
        pOut.DepthAlign = microTileThickness;
        pOut.Bpp = bpp;
        pOut.SliceSize = (uint)pOut.SurfSize / Math.Max(1, numSlices);
    }

    private static void ComputeSurfaceInfoMacroTiled(uint tileMode, uint baseTileMode, uint bpp, uint numSamples,
        uint pitch, uint height, uint numSlices, uint mipLevel, uint flags, SurfaceOut pOut)
    {
        uint microTileThickness = ComputeSurfaceThickness(tileMode);
        uint expTileMode = tileMode;

        if (mipLevel != 0)
        {
            pitch = NextPow2(pitch);
            height = NextPow2(height);
            if (((flags >> 4) & 1) == 0)
                numSlices = NextPow2(numSlices);

            if (expTileMode == 7 && numSlices < 4)
            {
                expTileMode = 4;
                microTileThickness = 1;
            }
        }

        ComputeSurfaceAlignmentsMacroTiled(expTileMode, bpp, flags, numSamples,
            out uint baseAlign, out uint pitchAlign, out uint heightAlign, out uint macroWidth, out uint macroHeight);

        uint bankSwapWidth = ComputeSurfaceBankSwappedWidth(expTileMode, bpp, numSamples, pitch);
        if (bankSwapWidth > pitchAlign)
            pitchAlign = bankSwapWidth;

        PadDimensions(expTileMode, (flags >> 4) & 1, pitchAlign, heightAlign, microTileThickness,
            ref pitch, ref height, ref numSlices);

        pOut.Pitch = pitch;
        pOut.Height = height;
        pOut.Depth = numSlices;
        pOut.SurfSize = ((height * pitch * numSlices * bpp * numSamples) + 7) / 8;
        pOut.TileMode = expTileMode;
        pOut.BaseAlign = baseAlign;
        pOut.PitchAlign = pitchAlign;
        pOut.HeightAlign = heightAlign;
        pOut.DepthAlign = microTileThickness;
        pOut.Bpp = bpp;
        pOut.SliceSize = (uint)pOut.SurfSize / Math.Max(1, numSlices);
    }

    private static void ComputeSurfaceAlignmentsLinear(uint tileMode, uint bpp, uint flags,
        out uint baseAlign, out uint pitchAlign, out uint heightAlign)
    {
        if (tileMode == 0)
        {
            baseAlign = 1;
            pitchAlign = bpp != 1 ? 1u : 8u;
            heightAlign = 1;
        }
        else if (tileMode == 1)
        {
            uint pixelsPerPipeInterleave = 2048 / bpp;
            baseAlign = 256;
            pitchAlign = Math.Max(64, pixelsPerPipeInterleave);
            heightAlign = 1;
        }
        else
        {
            baseAlign = 1;
            pitchAlign = 1;
            heightAlign = 1;
        }

        pitchAlign = AdjustPitchAlignment(flags, pitchAlign);
    }

    private static void ComputeSurfaceAlignmentsMicroTiled(uint tileMode, uint bpp, uint flags, uint numSamples,
        out uint baseAlign, out uint pitchAlign, out uint heightAlign)
    {
        switch (bpp)
        {
            case 24:
            case 48:
            case 96:
                bpp /= 3;
                break;
        }

        uint thickness = ComputeSurfaceThickness(tileMode);
        baseAlign = 256;
        pitchAlign = Math.Max(8, 256 / bpp / numSamples / thickness);
        heightAlign = 8;

        pitchAlign = AdjustPitchAlignment(flags, pitchAlign);
    }

    private static void ComputeSurfaceAlignmentsMacroTiled(uint tileMode, uint bpp, uint flags, uint numSamples,
        out uint baseAlign, out uint pitchAlign, out uint heightAlign, out uint macroWidth, out uint macroHeight)
    {
        uint aspectRatio = ComputeMacroTileAspectRatio(tileMode);
        uint thickness = ComputeSurfaceThickness(tileMode);

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

        macroWidth = 32 / aspectRatio;
        macroHeight = aspectRatio * 16;

        pitchAlign = Math.Max(macroWidth, macroWidth * (256 / bpp / (8 * thickness) / numSamples));
        pitchAlign = AdjustPitchAlignment(flags, pitchAlign);
        heightAlign = macroHeight;

        uint macroTileBytes = numSamples * (((bpp * macroHeight * macroWidth) + 7) >> 3);

        if (thickness == 1)
            baseAlign = Math.Max(macroTileBytes, ((numSamples * heightAlign * bpp * pitchAlign) + 7) >> 3);
        else
            baseAlign = Math.Max(256, ((4 * heightAlign * bpp * pitchAlign) + 7) >> 3);

        uint microTileBytes = ((thickness * numSamples * (bpp << 6)) + 7) >> 3;
        uint numSlicesPerMicroTile = microTileBytes < 2048 ? 1u : microTileBytes / 2048;
        baseAlign /= numSlicesPerMicroTile;
    }

    private static uint AdjustPitchAlignment(uint flags, uint pitchAlign)
    {
        if (((flags >> 13) & 1) != 0)
            pitchAlign = PowTwoAlign(pitchAlign, 0x20);
        return pitchAlign;
    }

    private static void PadDimensions(uint tileMode, uint isCube, uint pitchAlign, uint heightAlign, uint sliceAlign,
        ref uint pitch, ref uint height, ref uint numSlices)
    {
        uint thickness = ComputeSurfaceThickness(tileMode);

        if ((pitchAlign & (pitchAlign - 1)) == 0)
            pitch = PowTwoAlign(pitch, pitchAlign);
        else
            pitch = (pitch + pitchAlign - 1) / pitchAlign * pitchAlign;

        height = PowTwoAlign(height, heightAlign);

        if (isCube != 0)
            numSlices = NextPow2(numSlices);

        if (thickness > 1)
            numSlices = PowTwoAlign(numSlices, sliceAlign);
    }

    private static uint ComputeSurfaceMipLevelTileMode(uint baseTileMode, uint bpp, uint level, uint width, uint height,
        uint numSlices, uint numSamples, uint isDepth)
    {
        uint expTileMode = baseTileMode;
        uint tileSlices = ComputeSurfaceTileSlices(baseTileMode, bpp, numSamples);

        if (numSamples > 1 || tileSlices > 1 || isDepth != 0)
        {
            expTileMode = baseTileMode switch
            {
                7 => 4,
                13 => 12,
                11 => 8,
                15 => 14,
                _ => expTileMode
            };
        }

        if (baseTileMode == 2 && numSamples > 1)
            expTileMode = 4;
        else if (baseTileMode == 3)
        {
            if (numSamples > 1 || isDepth != 0)
                expTileMode = 2;
            if (numSamples is 2 or 4)
                expTileMode = 7;
        }

        if (level == 0)
            return expTileMode;

        if (bpp is 24 or 48 or 96)
            bpp /= 3;

        uint widtha = NextPow2(width);
        uint heighta = NextPow2(height);
        uint numSlicesa = NextPow2(numSlices);

        expTileMode = ConvertToNonBankSwappedMode(expTileMode);
        uint thickness = ComputeSurfaceThickness(expTileMode);
        uint microTileBytes = ((numSamples * bpp * (thickness << 6)) + 7) >> 3;

        uint widthAlignFactor = 1;
        if (microTileBytes < 256)
            widthAlignFactor = Math.Max(1, 256 / microTileBytes);

        uint macroTileWidth = 32;
        uint macroTileHeight = 16;

        switch (expTileMode)
        {
            case 4:
            case 12:
                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                    expTileMode = 2;
                break;
            case 5:
                macroTileWidth = 16;
                macroTileHeight = 32;
                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                    expTileMode = 2;
                break;
            case 6:
                macroTileWidth = 8;
                macroTileHeight = 64;
                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                    expTileMode = 2;
                break;
            case 7:
            case 13:
                if (widtha < widthAlignFactor * macroTileWidth || heighta < macroTileHeight)
                    expTileMode = 3;
                break;
        }

        if (numSlicesa < 4)
        {
            expTileMode = expTileMode switch
            {
                3 => 2,
                7 => 4,
                13 => 12,
                _ => expTileMode
            };
        }

        return expTileMode;
    }

    private static uint ComputeSurfaceTileSlices(uint tileMode, uint bpp, uint numSamples)
    {
        uint bytePerSample = ((bpp << 6) + 7) >> 3;
        uint tileSlices = 1;

        if (ComputeSurfaceThickness(tileMode) > 1)
            numSamples = 4;

        if (bytePerSample != 0)
        {
            uint samplePerTile = 2048 / bytePerSample;
            if (samplePerTile < numSamples)
                tileSlices = Math.Max(1, numSamples / samplePerTile);
        }

        return tileSlices;
    }

    private static uint ConvertToNonBankSwappedMode(uint tileMode)
    {
        return tileMode switch
        {
            8 => 4,
            9 => 5,
            10 => 6,
            11 => 7,
            14 => 12,
            15 => 13,
            _ => tileMode
        };
    }

    private static uint NextPow2(uint v)
    {
        v -= 1;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }

    private static uint PowTwoAlign(uint x, uint align)
    {
        return (~(align - 1)) & (x + align - 1);
    }

    #endregion
}