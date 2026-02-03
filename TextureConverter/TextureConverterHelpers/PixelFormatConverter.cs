using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TextureConverter.TextureConverterHelpers;

/// <summary>
/// Provides pixel format conversion utilities for various texture formats.
/// Consolidates format conversion logic used by DDS, XTX, and other texture types.
/// </summary>
public static class PixelFormatConverter
{
    public static byte[] ConvertToBGRA8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 4];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Write in BGRA order
                    result[offset++] = row[x].B;
                    result[offset++] = row[x].G;
                    result[offset++] = row[x].R;
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToRGB10A2(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 4];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 6) & 0x3FF;
                    uint g = (uint)(row[x].G >> 6) & 0x3FF;
                    uint b = (uint)(row[x].B >> 6) & 0x3FF;
                    uint a = (uint)(row[x].A >> 6) & 0x3;
                    uint packed = (a << 30) | (b << 20) | (g << 10) | r;
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 4);
                    offset += 4;
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToRGB565(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 3) & 0x1F;
                    uint g = (uint)(row[x].G >> 2) & 0x3F;
                    uint b = (uint)(row[x].B >> 3) & 0x1F;
                    // DDS format: R at bits 11-15 (0xF800), G at bits 5-10 (0x7E0), B at bits 0-4 (0x1F)
                    ushort packed = (ushort)((r << 11) | (g << 5) | b);
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToRGB5A1(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 3) & 0x1F;
                    uint g = (uint)(row[x].G >> 3) & 0x1F;
                    uint b = (uint)(row[x].B >> 3) & 0x1F;
                    uint a = (row[x].A > 128 ? 1U : 0U) & 0x1;
                    ushort packed = (ushort)((a << 15) | (b << 10) | (g << 5) | r);
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToRGBA4(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    uint r = (uint)(row[x].R >> 4) & 0xF;
                    uint g = (uint)(row[x].G >> 4) & 0xF;
                    uint b = (uint)(row[x].B >> 4) & 0xF;
                    uint a = (uint)(row[x].A >> 4) & 0xF;
                    ushort packed = (ushort)((a << 12) | (b << 8) | (g << 4) | r);
                    Array.Copy(BitConverter.GetBytes(packed), 0, result, offset, 2);
                    offset += 2;
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToL8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Convert to luminance using standard weights
                    result[offset++] = (byte)((0.299f * row[x].R) + (0.587f * row[x].G) + (0.114f * row[x].B));
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToLA8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte luminance = (byte)((0.299f * row[x].R) + (0.587f * row[x].G) + (0.114f * row[x].B));
                    result[offset++] = luminance;
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToR8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = (byte)((0.299f * row[x].R) + (0.587f * row[x].G) + (0.114f * row[x].B));
                }
            }
        });
        return result;
    }

    public static byte[] ConvertToRG8(Image<Bgra32> image)
    {
        byte[] result = new byte[image.Width * image.Height * 2];
        int offset = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    result[offset++] = (byte)((0.299f * row[x].R) + (0.587f * row[x].G) + (0.114f * row[x].B));
                    result[offset++] = row[x].A;
                }
            }
        });
        return result;
    }
}
