namespace JustDanceEditor.IPK;

public static class UbiArtCRC
{
    private static void Shifter(ref uint a, ref uint b, ref uint c)
    {
        unchecked
        {
            a = (a - b - c) ^ (c >> 0xd);
            b = (b - a - c) ^ (a << 0x8);
            c = (c - a - b) ^ (b >> 0xd);
            a = (a - c - b) ^ (c >> 0xc);
            uint d = (b - a - c) ^ (a << 0x10);
            c = (c - a - d) ^ (d >> 0x5);
            a = (a - c - d) ^ (c >> 0x3);
            b = (d - a - c) ^ (a << 0xa);
            c = (c - a - b) ^ (b >> 0xf);
        }
    }

    public static uint Compute(byte[] data)
    {
        unchecked
        {
            uint a = 0x9E3779B9;
            uint b = 0x9E3779B9;
            uint c = 0;
            int length = data.Length;

            int i = 0;
            while (i < length / 0xc)
            {
                int k = i * 0xc;
                a += (uint)(((((data[k + 0x3] << 8) + data[k + 0x2]) << 8) + data[k + 0x1]) << 8) + data[k];
                b += (uint)(((((data[k + 0x7] << 8) + data[k + 0x6]) << 8) + data[k + 0x5]) << 8) + data[k + 0x4];
                c += (uint)(((((data[k + 0xb] << 8) + data[k + 0xa]) << 8) + data[k + 0x9]) << 8) + data[k + 0x8];

                Shifter(ref a, ref b, ref c);
                i++;
            }

            c += (uint)length;
            int offset = length - (length % 0xc);
            int decide = (length % 0xc) - 1;

            if (decide >= 0xa) c += (uint)data[offset + 0xa] << 0x18;
            if (decide >= 0x9) c += (uint)data[offset + 0x9] << 0x10;
            if (decide >= 0x8) c += (uint)data[offset + 0x8] << 0x8;
            if (decide >= 0x7) b += (uint)data[offset + 0x7] << 0x18;
            if (decide >= 0x6) b += (uint)data[offset + 0x6] << 0x10;
            if (decide >= 0x5) b += (uint)data[offset + 0x5] << 0x8;
            if (decide >= 0x4) b += data[offset + 0x4];
            if (decide >= 0x3) a += (uint)data[offset + 0x3] << 0x18;
            if (decide >= 0x2) a += (uint)data[offset + 0x2] << 0x10;
            if (decide >= 0x1) a += (uint)data[offset + 0x1] << 0x8;
            if (decide >= 0x0) a += data[offset + 0x0];

            Shifter(ref a, ref b, ref c);

            return c;
        }
    }
}