using TextureConverter.Enums;
using TextureConverter.TextureType;

namespace TextureConverter.TextureConverterHelpers;

/// <summary>
/// Calculates texture surface information and alignments for GTX format processing.
/// Extracted from GTX.cs to isolate complex surface math from data storage concerns.
/// </summary>
internal static class GtxSurfaceCalculator
{
    private static uint _expPitch, _expHeight, _expNumSlices;

    /// <summary>
    /// Computes surface info based on input surface parameters, updating output values in place.
    /// </summary>
    public static void ComputeSurfaceInfo(GTX.SurfaceIn surfaceIn, GTX.SurfaceOut surfaceOut)
    {
        ArgumentNullException.ThrowIfNull(surfaceIn);
        ArgumentNullException.ThrowIfNull(surfaceOut);

        uint bpp = surfaceIn.Bpp;
        if (bpp > 0x80)
            return;

        // 1. Calculate mip dimensions based on original pixels (inline from GTX.ComputeMipLevel)
        if (surfaceIn.Format is >= 49 and <= 55 && (surfaceIn.MipLevel == 0 || ((surfaceIn.Flags >> 12) & 1) != 0))
        {
            surfaceIn.Width = PowTwoAlign(surfaceIn.Width, 4);
            surfaceIn.Height = PowTwoAlign(surfaceIn.Height, 4);
        }
        surfaceOut.PixelBits = bpp;

        if (surfaceIn.Format != 0)
        {
            (uint formattedBpp, uint expandX, uint expandY, uint elemMode) = GTX.GetBitsPerPixel((int)surfaceIn.Format);

            // 2. Adjust for BCn: Divide width/height by 4, set bpp to 64 or 128
            if (expandX > 1 || expandY > 1)
            {
                surfaceIn.Width = Math.Max(1, surfaceIn.Width / expandX);
                surfaceIn.Height = Math.Max(1, surfaceIn.Height / expandY);
            }

            bpp = elemMode switch
            {
                9 or 12 => 64,   // BC1, BC4
                10 or 11 or 13 => 128, // BC2, BC3, BC5
                _ => formattedBpp
            };

            surfaceIn.Bpp = bpp;
        }

        // 3. Calculate aligned Pitch and SurfSize based on adjusted dimensions
        if (ComputeSurfaceInfoEx(surfaceIn, surfaceOut) == 0)
        {
            surfaceOut.Bpp = bpp;
            surfaceOut.PixelPitch = surfaceOut.Pitch;
            surfaceOut.PixelHeight = surfaceOut.Height;

            // 4. Restore original pixel dimensions for the final output info
            if (surfaceIn.Format != 0)
            {
                (uint _, uint expandX, uint expandY, uint _) = GTX.GetBitsPerPixel((int)surfaceIn.Format);
                if (expandX > 1 || expandY > 1)
                {
                    surfaceOut.PixelPitch *= expandX;
                    surfaceOut.PixelHeight *= expandY;
                }
            }

            surfaceOut.SliceSize = (uint)(((surfaceIn.Flags >> 5) & 1) != 0
                ? surfaceOut.SurfSize
                : surfaceOut.SurfSize / surfaceOut.Depth);

            surfaceOut.PitchTileMax = (surfaceOut.Pitch >> 3) - 1;
            surfaceOut.HeightTileMax = (surfaceOut.Height >> 3) - 1;
            surfaceOut.SliceTileMax = ((surfaceOut.Height * surfaceOut.Pitch) >> 6) - 1;
        }

        if (surfaceOut.TileMode == 0)
        {
            surfaceOut.TileMode = 16;
        }
    }

    private static uint ComputeSurfaceInfoEx(GTX.SurfaceIn pIn, GTX.SurfaceOut pOut)
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
                uint[] compSurfInfoLinear = ComputeSurfaceInfoLinear(
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
                uint[] compSurfInfoMicroTile = ComputeSurfaceInfoMicroTiled(
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
                uint[] compSurfInfoMacoTile = ComputeSurfaceInfoMacroTiled(
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

        uint baseAlign = thickness == 1
            ? Math.Max(macroTileBytes, ((numSamples * heightAlign * bpp * pitchAlign) + 7) >> 3)
            : Math.Max(256, ((4 * heightAlign * bpp * pitchAlign) + 7) >> 3);
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

    private static Tuple<uint, uint, uint> PadDimensions(uint tileMode, uint padDims, uint isCube, uint pitchAlign, uint heightAlign, uint sliceAlign)
    {
        uint thickness = ComputeSurfaceThickness((AddrTileMode)tileMode);
        if (padDims == 0)
            padDims = 3;

        if ((pitchAlign & (pitchAlign - 1)) == 0)
            _expPitch = PowTwoAlign(_expPitch, pitchAlign);
        else
        {
            _expPitch += pitchAlign - 1;
            _expPitch /= pitchAlign;
            _expPitch *= pitchAlign;
        }

        if (padDims > 1)
            _expHeight = PowTwoAlign(_expHeight, heightAlign);

        if (padDims > 2 || thickness > 1)
        {
            if (isCube != 0)
                _expNumSlices = GTX.NextPow2(_expNumSlices);

            if (thickness > 1)
                _expNumSlices = PowTwoAlign(_expNumSlices, sliceAlign);
        }

        return new Tuple<uint, uint, uint>(_expPitch, _expHeight, _expNumSlices);
    }

    private static uint[] ComputeSurfaceInfoMacroTiled(uint tileMode, uint baseTileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        _expPitch = pitch;
        _expHeight = height;
        _expNumSlices = numSlices;

        uint valid = 1;
        uint expTileMode = tileMode;
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);

        uint baseAlign, pitchAlign, heightAlign;
        uint bankSwappedWidth, pitchAlignFactor;
        uint result, pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pTileModeOut, pBaseAlign, pDepthAlign;

        if (mipLevel != 0)
        {
            _expPitch = GTX.NextPow2(pitch);
            _expHeight = GTX.NextPow2(height);

            if (((flags >> 4) & 1) != 0)
            {
                _expNumSlices = numSlices;

                padDims = numSlices <= 1
                    ? 2
                    : (uint)0;
            }
            else
                _expNumSlices = GTX.NextPow2(numSlices);

            if (expTileMode == 7 && _expNumSlices < 4)
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
            Tuple<uint, uint, uint, uint, uint> tup = ComputeSurfaceAlignmentsMacroTiled(
                tileMode,
                bpp,
                flags,
                numSamples);

            baseAlign = tup.Item1;
            pitchAlign = tup.Item2;
            heightAlign = tup.Item3;

            bankSwappedWidth = ComputeSurfaceBankSwappedWidth((AddrTileMode)tileMode, bpp, numSamples, pitch);

            if (bankSwappedWidth > pitchAlign)
                pitchAlign = bankSwappedWidth;

            Tuple<uint, uint, uint> padDimens = PadDimensions(
                 tileMode,
                 padDims,
                 (flags >> 4) & 1,
                 pitchAlign,
                 heightAlign,
                 microTileThickness);

            _expPitch = padDimens.Item1;
            _expHeight = padDimens.Item2;
            _expNumSlices = padDimens.Item3;

            pPitchOut = _expPitch;
            pHeightOut = _expHeight;
            pNumSlicesOut = _expNumSlices;
            pSurfSize = ((_expHeight * _expPitch * _expNumSlices * bpp * numSamples) + 7) / 8;
            pTileModeOut = expTileMode;
            pBaseAlign = baseAlign;
            pDepthAlign = microTileThickness;
            result = valid;
        }

        else
        {
            Tuple<uint, uint, uint, uint, uint> tup = ComputeSurfaceAlignmentsMacroTiled(
                baseTileMode,
                bpp,
                flags,
                numSamples);

            pitchAlign = tup.Item2;
            heightAlign = tup.Item3;

            pitchAlignFactor = Math.Max(1, 32 / bpp);

            if (_expPitch < pitchAlign * pitchAlignFactor || _expHeight < heightAlign)
            {
                uint[] microTileInfo = ComputeSurfaceInfoMicroTiled(
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

                bankSwappedWidth = ComputeSurfaceBankSwappedWidth((AddrTileMode)tileMode, bpp, numSamples, pitch);
                if (bankSwappedWidth > pitchAlign)
                    pitchAlign = bankSwappedWidth;

                Tuple<uint, uint, uint> padDimens = PadDimensions(
                    tileMode,
                    padDims,
                    (flags >> 4) & 1,
                    pitchAlign,
                    heightAlign,
                    microTileThickness);

                _expPitch = padDimens.Item1;
                _expHeight = padDimens.Item2;
                _expNumSlices = padDimens.Item3;

                pPitchOut = _expPitch;
                pHeightOut = _expHeight;
                pNumSlicesOut = _expNumSlices;
                pSurfSize = ((_expHeight * _expPitch * _expNumSlices * bpp * numSamples) + 7) / 8;

                pTileModeOut = expTileMode;
                pBaseAlign = baseAlign;
                pDepthAlign = microTileThickness;
                result = valid;
            }
        }

        return [result, pPitchOut, pHeightOut,
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

    private static uint[] ComputeSurfaceInfoMicroTiled(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        _expPitch = pitch;
        _expHeight = height;
        _expNumSlices = numSlices;

        uint valid = 1;
        uint expTileMode = tileMode;
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);
        uint pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pTileModeOut, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign;

        if (mipLevel != 0)
        {
            _expPitch = GTX.NextPow2(pitch);
            _expHeight = GTX.NextPow2(height);
            if (((flags >> 4) & 1) != 0)
            {
                _expNumSlices = numSlices;

                padDims = numSlices <= 1
                    ? 2
                    : (uint)0;
            }

            else
                _expNumSlices = GTX.NextPow2(numSlices);

            if (expTileMode == 3 && _expNumSlices < 4)
            {
                expTileMode = 2;
                microTileThickness = 1;
            }
        }

        Tuple<uint, uint, uint> surfMicroAlign = ComputeSurfaceAlignmentsMicroTiled(
            expTileMode,
            bpp,
            flags,
            numSamples);

        uint baseAlign = surfMicroAlign.Item1;
        uint pitchAlign = surfMicroAlign.Item2;
        uint heightAlign = surfMicroAlign.Item3;

        Tuple<uint, uint, uint> padDimens = PadDimensions(
            expTileMode,
            padDims,
            (flags >> 4) & 1,
            pitchAlign,
            heightAlign,
            microTileThickness);

        _expPitch = padDimens.Item1;
        _expHeight = padDimens.Item2;
        _expNumSlices = padDimens.Item3;

        pPitchOut = _expPitch;
        pHeightOut = _expHeight;
        pNumSlicesOut = _expNumSlices;
        pSurfSize = ((_expHeight * _expPitch * _expNumSlices * bpp * numSamples) + 7) / 8;

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

    private static uint[] ComputeSurfaceInfoLinear(uint tileMode, uint bpp, uint numSamples, uint pitch, uint height, uint numSlices, uint mipLevel, uint padDims, uint flags)
    {
        _expPitch = pitch;
        _expHeight = height;
        _expNumSlices = numSlices;

        uint valid = 1;
        uint microTileThickness = ComputeSurfaceThickness((AddrTileMode)tileMode);

        uint baseAlign, pitchAlign, heightAlign;
        uint pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign;

        Tuple<uint, uint, uint> compAllignLinear = ComputeSurfaceAlignmentsLinear(tileMode, bpp, flags);
        baseAlign = compAllignLinear.Item1;
        pitchAlign = compAllignLinear.Item2;
        heightAlign = compAllignLinear.Item3;

        if ((((flags >> 9) & 1) != 0) && (mipLevel == 0))
        {
            _expPitch /= 3;
            _expPitch = GTX.NextPow2(_expPitch);
        }

        if (mipLevel != 0)
        {
            _expPitch = GTX.NextPow2(_expPitch);
            _expHeight = NextPow2(_expHeight);

            if (((flags >> 4) & 1) != 0)
            {
                _expNumSlices = numSlices;

                padDims = numSlices <= 1
                    ? 2
                    : (uint)0;
            }
            else
                _expNumSlices = GTX.NextPow2(numSlices);
        }

        Tuple<uint, uint, uint> padimens = PadDimensions(
        tileMode,
        padDims,
        (flags >> 4) & 1,
        pitchAlign,
        heightAlign,
        microTileThickness);

        _expPitch = padimens.Item1;
        _expHeight = padimens.Item2;
        _expNumSlices = padimens.Item3;

        if ((((flags >> 9) & 1) != 0) && (mipLevel == 0))
        {
            _expPitch *= 3;
        }

        pPitchOut = _expPitch;
        pHeightOut = _expHeight;
        pNumSlicesOut = _expNumSlices;
        pSurfSize = ((_expHeight * _expPitch * _expNumSlices * bpp * numSamples) + 7) / 8;

        pBaseAlign = baseAlign;
        pPitchAlign = pitchAlign;
        pHeightAlign = heightAlign;
        pDepthAlign = microTileThickness;

        return [valid, pPitchOut, pHeightOut, pNumSlicesOut, pSurfSize, 0, pBaseAlign, pPitchAlign, pHeightAlign, pDepthAlign];
    }

    private static uint ComputeSurfaceMipLevelTileMode(uint baseTileMode, uint bpp, uint level, uint pitch, uint height, uint numSlices, uint numSamples, uint isDepth, uint noRecurse)
    {
        if (level <= 0)
            return baseTileMode;

        uint widthAlignFactor = Math.Max(1, 32 / bpp);

        if (((pitch >> 3) * bpp) < 128 * widthAlignFactor)
        {
            if ((baseTileMode >= 15) && (baseTileMode < 16))
            {
                return 14;
            }

            if ((baseTileMode >= 7) && (baseTileMode < 8))
            {
                return 7;
            }
        }

        if (isDepth != 0 && baseTileMode < 8 && baseTileMode >= 2)
        {
            return baseTileMode;
        }

        if (pitch < 64 && pitch >= 32)
        {
            return 2;
        }

        if (pitch < 32)
        {
            return baseTileMode switch
            {
                15 or 14 => 2,
                _ => 0,
            };
        }

        return baseTileMode;
    }
}