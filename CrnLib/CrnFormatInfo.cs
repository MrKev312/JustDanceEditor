using System.Text;

namespace CrnLib;

public static class CrnFormatInfo
{
    public static bool IsSupported(CrnFormat format)
    {
        return CrnFormatHelpers.IsSupportedCrnFormat(format);
    }

    public static bool CanEncode(CrnFormat format)
    {
        return CrnFormatHelpers.IsManagedEncodableCrnFormat(format);
    }

    public static CrnFormat GetFundamentalFormat(CrnFormat format)
    {
        return format switch
        {
            CrnFormat.Dxt5CcxY or CrnFormat.Dxt5XGxR or CrnFormat.Dxt5XGbr or CrnFormat.Dxt5Agbr => CrnFormat.Dxt5,
            _ when CrnFormatHelpers.IsSupportedCrnFormat(format) => format,
            _ => throw new NotSupportedException($"CRN format '{format}' is not supported."),
        };
    }

    public static int GetBitsPerTexel(CrnFormat format)
    {
        return format switch
        {
            CrnFormat.Dxt1 or CrnFormat.Dxt5A or CrnFormat.Etc1 => 4,
            CrnFormat.Dxt3 or CrnFormat.Dxt5 or
            CrnFormat.Dxt5CcxY or CrnFormat.Dxt5XGxR or CrnFormat.Dxt5XGbr or CrnFormat.Dxt5Agbr or
            CrnFormat.DxnXy or CrnFormat.DxnYx => 8,
            _ => throw new NotSupportedException($"CRN format '{format}' is not supported."),
        };
    }

    public static int GetBytesPerBlock(CrnFormat format)
    {
        return CrnFormatHelpers.BytesPerBlock(format);
    }

    public static uint GetFourCc(CrnFormat format)
    {
        return format switch
        {
            CrnFormat.Dxt1 => FourCc("DXT1"),
            CrnFormat.Dxt3 => FourCc("DXT3"),
            CrnFormat.Dxt5 => FourCc("DXT5"),
            CrnFormat.DxnXy => FourCc("A2XY"),
            CrnFormat.DxnYx => FourCc("ATI2"),
            CrnFormat.Dxt5A => FourCc("ATI1"),
            CrnFormat.Dxt5CcxY => FourCc("CCxY"),
            CrnFormat.Dxt5XGxR => FourCc("xGxR"),
            CrnFormat.Dxt5XGbr => FourCc("xGBR"),
            CrnFormat.Dxt5Agbr => FourCc("AGBR"),
            CrnFormat.Etc1 => FourCc("ETC1"),
            _ => throw new NotSupportedException($"CRN format '{format}' is not supported."),
        };
    }

    public static string GetFourCcString(CrnFormat format)
    {
        uint fourCc = GetFourCc(format);
        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)fourCc;
        bytes[1] = (byte)(fourCc >> 8);
        bytes[2] = (byte)(fourCc >> 16);
        bytes[3] = (byte)(fourCc >> 24);
        return Encoding.ASCII.GetString(bytes);
    }

    private static uint FourCc(string value)
    {
        return (uint)value[0] |
            ((uint)value[1] << 8) |
            ((uint)value[2] << 16) |
            ((uint)value[3] << 24);
    }
}
