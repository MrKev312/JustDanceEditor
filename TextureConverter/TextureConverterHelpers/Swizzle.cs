using TextureConverter.TextureType;

namespace TextureConverter.TextureConverterHelpers;

internal class Swizzle
{
    private static readonly Dictionary<int, int> padds = new()
    {
        { 1, 64 },
        { 2, 32 },
        { 4, 16 },
        { 8, 8 },
        { 16, 4 }
    };

    private static readonly Dictionary<int, int> xBases = new()
    {
        { 1, 4 },
        { 2, 3 },
        { 4, 2 },
        { 8, 1 },
        { 16, 0 }
    };

    internal static byte[] Deswizzle(uint width, uint height, XTX.XTXImageFormat format, byte[] data)
    {
        int pos_ = 0;

        int bpp = XTX.GetBPP(format);

        uint originWidth = width;
        uint originHeight = height;

        if (XTX.GetBPP(format) != 0)
        {
            originWidth = (originWidth + 3) / 4;
            originHeight = (originHeight + 3) / 4;
        }

        int xb = CountZeros(Pow2RoundUp(originWidth));
        int yb = CountZeros(Pow2RoundUp(originHeight));

        uint hh = Pow2RoundUp(originHeight) >> 1;

        if (!IsPow2(originHeight) && originHeight <= hh + (hh / 3) && yb > 3)
            yb -= 1;

        width = RoundSize(originWidth, padds[bpp]);

        byte[] result = new byte[data.Length];
        Array.Copy(data, result, data.Length);

        int xBase = xBases[bpp];

        for (uint y = 0; y < originHeight; y++)
        {
            for (uint x = 0; x < originWidth; x++)
            {
                int pos = GetAddr(x, y, xb, yb, width, xBase) * bpp;

                if (pos_ + bpp <= data.Length && pos + bpp <= data.Length)
                    Array.Copy(data, pos, result, pos_, bpp);

                pos_ += bpp;
            }
        }

        return result;
    }

    private static uint RoundSize(uint size, int pad)
    {
        uint mask = (uint)(pad - 1);
        if ((size & mask) != 0)
        {
            size &= ~mask;
            size += (uint)pad;
        }

        return size;
    }

    private static uint Pow2RoundUp(uint v)
    {
        v -= 1;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }

    private static bool IsPow2(uint v)
    {
        return v != 0 && (v & (v - 1)) == 0;
    }

    private static int CountZeros(uint v)
    {
        int numZeros = 0;
        for (int i = 0; i < 32; i++)
        {
            if ((v & (1 << i)) != 0)
                break;

            numZeros += 1;
        }

        return numZeros;
    }

    private static int GetAddr(uint x, uint y, int xb, int yb, uint width, int xBase)
    {
        int xCnt = xBase;
        int yCnt = 1;
        int xUsed = 0;
        int yUsed = 0;
        int address = 0;

        while (xUsed < xBase + 2 && xUsed + xCnt < xb)
        {
            int xMask = (1 << xCnt) - 1;
            int yMask = (1 << yCnt) - 1;

            address |= (int)((x & (uint)xMask) << (xUsed + yUsed));
            address |= (int)((y & (uint)yMask) << (xUsed + yUsed + xCnt));

            x >>= xCnt;
            y >>= yCnt;

            xUsed += xCnt;
            yUsed += yCnt;

            xCnt = Math.Max(Math.Min(xb - xUsed, 1), 0);
            yCnt = Math.Max(Math.Min(yb - yUsed, yCnt << 1), 0);
        }

        address |= (int)((x + (y * (width >> xUsed))) << (xUsed + yUsed));

        return address;
    }
}
