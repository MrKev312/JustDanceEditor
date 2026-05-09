using BCnEncoder.Shared;

namespace CrnLib;

internal static class CrnFormatHelpers
{
    public static bool IsSupportedCrnFormat(CrnFormat format)
    {
        return format is >= CrnFormat.Dxt1 and <= CrnFormat.Etc1;
    }

    public static bool IsManagedEncodableCrnFormat(CrnFormat format)
    {
        return format is CrnFormat.Dxt1 or
            CrnFormat.Dxt5 or
            CrnFormat.Dxt5CcxY or
            CrnFormat.Dxt5XGxR or
            CrnFormat.Dxt5XGbr or
            CrnFormat.Dxt5Agbr or
            CrnFormat.DxnXy or
            CrnFormat.DxnYx or
            CrnFormat.Dxt5A;
    }

    public static bool IsColorOnly(CrnFormat format)
    {
        return format == CrnFormat.Dxt1;
    }

    public static bool IsColorAlpha(CrnFormat format)
    {
        return format is CrnFormat.Dxt5 or
            CrnFormat.Dxt5CcxY or
            CrnFormat.Dxt5XGxR or
            CrnFormat.Dxt5XGbr or
            CrnFormat.Dxt5Agbr;
    }

    public static bool IsAlphaOnly(CrnFormat format)
    {
        return format == CrnFormat.Dxt5A;
    }

    public static bool IsDualAlpha(CrnFormat format)
    {
        return format is CrnFormat.DxnXy or CrnFormat.DxnYx;
    }

    public static bool HasColorPalette(CrnFormat format)
    {
        return IsColorOnly(format) || IsColorAlpha(format);
    }

    public static bool HasAlphaPalette(CrnFormat format)
    {
        return IsColorAlpha(format) || IsAlphaOnly(format) || IsDualAlpha(format);
    }

    public static int BytesPerBlock(CrnFormat format)
    {
        return format switch
        {
            CrnFormat.Dxt1 or CrnFormat.Dxt5A or CrnFormat.Etc1 => 8,
            CrnFormat.Dxt3 or CrnFormat.Dxt5 or
            CrnFormat.Dxt5CcxY or CrnFormat.Dxt5XGxR or CrnFormat.Dxt5XGbr or CrnFormat.Dxt5Agbr or
            CrnFormat.DxnXy or CrnFormat.DxnYx => 16,
            _ => throw new NotSupportedException($"CRN format '{format}' is not supported."),
        };
    }

    public static CompressionFormat ToCompressionFormat(CrnFormat format)
    {
        return format switch
        {
            CrnFormat.Dxt1 => CompressionFormat.Bc1,
            CrnFormat.Dxt3 => CompressionFormat.Bc2,
            CrnFormat.Dxt5 or CrnFormat.Dxt5CcxY or CrnFormat.Dxt5XGxR or CrnFormat.Dxt5XGbr or CrnFormat.Dxt5Agbr => CompressionFormat.Bc3,
            CrnFormat.DxnXy or CrnFormat.DxnYx => CompressionFormat.Bc5,
            CrnFormat.Dxt5A => CompressionFormat.Bc4,
            _ => throw new NotSupportedException($"CRN format '{format}' cannot be encoded by the managed BCn encoder."),
        };
    }
}
