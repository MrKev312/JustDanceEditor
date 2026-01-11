using System;

namespace TextureConverter.TextureConverterHelpers;

public static class Swizzle
{
    private const int GOB_WIDTH_BYTES = 64;
    private const int GOB_HEIGHT = 8;
    private const int GOB_SIZE = 512;

    public static byte[] Deswizzle(int width, int height, int bpp, int blockHeightLog2, byte[] data)
    {
        return SwizzleOperation(width, height, bpp, blockHeightLog2, data, toLinear: true);
    }

    public static byte[] SwizzleData(int width, int height, int bpp, int blockHeightLog2, byte[] data)
    {
        // For Swizzling, we must allocate the full aligned size including padding
        int blockHeight = 1 << blockHeightLog2;
        int widthInGobs = DivRoundUp(width * bpp, GOB_WIDTH_BYTES);
        int heightInBlocks = DivRoundUp(height, GOB_HEIGHT * blockHeight);
        int heightInGobs = heightInBlocks * blockHeight;

        int alignedSize = widthInGobs * heightInGobs * GOB_SIZE;

        byte[] resultBuffer = new byte[alignedSize];
        SwizzleOperation(width, height, bpp, blockHeightLog2, data, toLinear: false, resultBuffer);

        return resultBuffer;
    }

    private static byte[] SwizzleOperation(int width, int height, int bpp, int blockHeightLog2, byte[] data, bool toLinear, byte[] outputBuffer = null)
    {
        int blockHeight = 1 << blockHeightLog2;
        int widthInGobs = DivRoundUp(width * bpp, GOB_WIDTH_BYTES);

        // Calculate the aligned size
        int heightInBlocks = DivRoundUp(height, GOB_HEIGHT * blockHeight);
        int heightInGobs = heightInBlocks * blockHeight;
        int alignedSize = widthInGobs * heightInGobs * GOB_SIZE;

        int linearSize = width * height * bpp;

        byte[] result = outputBuffer ?? new byte[toLinear ? linearSize : alignedSize];
        byte[] source = data;

        // Loop over the LINEAR image dimensions
        // It is often easier to iterate source pixels and calculate destination address
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceOffset = ((y * width) + x) * bpp;

                // If we are reading past the available data (e.g. tight buffer), skip
                if (sourceOffset + bpp > (toLinear ? result.Length : source.Length))
                    continue;

                // --- Calculate Swizzled Address ---

                // 1. Determine which GOB this coordinate belongs to
                int xBytes = x * bpp;
                int gobX = xBytes / GOB_WIDTH_BYTES;
                int gobY = y / GOB_HEIGHT;

                // 2. Determine which SuperBlock (Block Linear) the GOB belongs to
                int blockY = gobY / blockHeight;
                int subBlockY = gobY % blockHeight; // The vertical GOB index within the SuperBlock

                // 3. Calculate the Base Address of the GOB
                //    Stride of a SuperBlock Row = (Width in GOBs) * (Size of SuperBlock Column)
                //    Size of SuperBlock Column = blockHeight * GOB_SIZE
                int superBlockRowOffset = blockY * widthInGobs * blockHeight * GOB_SIZE;
                int gobColumnOffset = gobX * blockHeight * GOB_SIZE;
                int gobRowOffset = subBlockY * GOB_SIZE;

                int gobBaseAddress = superBlockRowOffset + gobColumnOffset + gobRowOffset;

                // 4. Calculate the offset INSIDE the GOB (Tegra Internal Swizzling)
                int gx = xBytes % GOB_WIDTH_BYTES;
                int gy = y % GOB_HEIGHT;
                int internalGobOffset = GetGobOffset(gx, gy);

                int swizzledOffset = gobBaseAddress + internalGobOffset;

                // Perform Copy
                if (toLinear)
                {
                    // Swizzled -> Linear
                    if (swizzledOffset + bpp <= source.Length && sourceOffset + bpp <= result.Length)
                        Array.Copy(source, swizzledOffset, result, sourceOffset, bpp);
                }
                else
                {
                    // Linear -> Swizzled
                    if (sourceOffset + bpp <= source.Length && swizzledOffset + bpp <= result.Length)
                        Array.Copy(source, sourceOffset, result, swizzledOffset, bpp);
                }
            }
        }

        return result;
    }

    // Reference: Tegra TRM v1.3 page 1218
    // Calculates the offset within a 512-byte GOB for a specific (x, y) byte coordinate.
    // x must be 0-63, y must be 0-7.
    private static int GetGobOffset(int x, int y)
    {
        return (x % 64 / 32 * 256) +
               (y % 8 / 2 * 64) +
               (x % 32 / 16 * 32) +
               (y % 2 * 16) +
               (x % 16);
    }

    // Matches tegra_swizzle Rust library logic exactly
    public static int GetBlockHeightMip0(int heightInBlocks)
    {
        // "heightInBlocks" should be the height of the mipmap in compressed blocks (pixels / 4) for BCn
        // or pixels for Uncompressed.

        int heightAndHalf = heightInBlocks + (heightInBlocks / 2);

        if (heightAndHalf >= 128)
            return 16;
        if (heightAndHalf >= 64)
            return 8;
        if (heightAndHalf >= 32)
            return 4;
        if (heightAndHalf >= 16)
            return 2;
        return 1;
    }

    public static int GetBlockHeightLog2(int heightInBlocks)
    {
        int bh = GetBlockHeightMip0(heightInBlocks);
        return (int)Math.Log2(bh);
    }

    private static int DivRoundUp(int value, int divisor)
    {
        return (value + divisor - 1) / divisor;
    }
}