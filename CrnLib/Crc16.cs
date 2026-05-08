namespace CrnLib;

internal static class Crc16
{
    public static ushort Compute(ReadOnlySpan<byte> data, ushort crc = 0)
    {
        crc = (ushort)~crc;

        foreach (byte value in data)
        {
            ushort q = (ushort)(value ^ (crc >> 8));
            crc <<= 8;

            ushort r = (ushort)((q >> 4) ^ q);
            crc ^= r;
            r <<= 5;
            crc ^= r;
            r <<= 7;
            crc ^= r;
        }

        return (ushort)~crc;
    }
}
