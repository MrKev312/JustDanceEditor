using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Buffers.Binary;

namespace CrnLib;

internal static class BcBlockDecoder
{
    public static Image<Rgba32> Decode(ReadOnlySpan<byte> blocks, CrnFormat format, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");

        int bytesPerBlock = format switch
        {
            CrnFormat.Dxt1 => 8,
            CrnFormat.Dxt5 => 16,
            _ => throw new NotSupportedException($"BC block format '{format}' is not supported."),
        };

        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int expectedLength = checked(blockCountX * blockCountY * bytesPerBlock);
        if (blocks.Length != expectedLength)
            throw new InvalidDataException("BC block payload length does not match the supplied dimensions.");

        byte[] rgba = new byte[checked(width * height * 4)];
        if (format == CrnFormat.Dxt5)
            DecodeDxt5(blocks, width, height, rgba);
        else
            DecodeDxt1(blocks, width, height, rgba);

        return Image.LoadPixelData<Rgba32>(rgba, width, height);
    }

    private static void DecodeDxt1(ReadOnlySpan<byte> data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int dataIndex = 0;

        Span<Rgba32> colors = stackalloc Rgba32[4];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(data[dataIndex..]);
                ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(data[(dataIndex + 2)..]);
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data[(dataIndex + 4)..]);
                dataIndex += 8;

                colors[0] = FromRgb565(color0);
                colors[1] = FromRgb565(color1);

                if (color0 > color1)
                {
                    colors[2] = Interpolate(colors[0], colors[1], 2, 1);
                    colors[3] = Interpolate(colors[0], colors[1], 1, 2);
                }
                else
                {
                    colors[2] = Interpolate(colors[0], colors[1], 1, 1);
                    colors[3] = new Rgba32(0, 0, 0, 0);
                }

                WriteColorBlock(destination, colors, code, bx, by, width, height, [], hasAlpha: false, alphaBits: 0);
            }
        }
    }

    private static void DecodeDxt5(ReadOnlySpan<byte> data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int dataIndex = 0;

        Span<Rgba32> colors = stackalloc Rgba32[4];
        Span<byte> alphas = stackalloc byte[8];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                byte alpha0 = data[dataIndex];
                byte alpha1 = data[dataIndex + 1];
                ulong alphaBits = 0;
                for (int i = 0; i < 6; i++)
                    alphaBits |= (ulong)data[dataIndex + 2 + i] << (8 * i);
                dataIndex += 8;

                BuildAlphaTable(alpha0, alpha1, alphas);

                ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(data[dataIndex..]);
                ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(data[(dataIndex + 2)..]);
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data[(dataIndex + 4)..]);
                dataIndex += 8;

                colors[0] = FromRgb565(color0);
                colors[1] = FromRgb565(color1);
                colors[2] = Interpolate(colors[0], colors[1], 2, 1);
                colors[3] = Interpolate(colors[0], colors[1], 1, 2);

                WriteColorBlock(destination, colors, code, bx, by, width, height, alphas, hasAlpha: true, alphaBits);
            }
        }
    }

    private static void WriteColorBlock(
        byte[] destination,
        Span<Rgba32> colors,
        uint code,
        int blockX,
        int blockY,
        int width,
        int height,
        ReadOnlySpan<byte> alphaLut,
        bool hasAlpha,
        ulong alphaBits)
    {
        ulong alphaData = alphaBits;
        for (int py = 0; py < 4; py++)
        {
            for (int px = 0; px < 4; px++)
            {
                int pixelIndex = (int)(code & 0x3);
                code >>= 2;

                int x = (blockX * 4) + px;
                int y = (blockY * 4) + py;
                if (x >= width || y >= height)
                    continue;

                int destIndex = ((y * width) + x) * 4;
                Rgba32 color = colors[pixelIndex];

                destination[destIndex] = color.R;
                destination[destIndex + 1] = color.G;
                destination[destIndex + 2] = color.B;
                destination[destIndex + 3] = hasAlpha ? alphaLut[(int)(alphaData & 0x7)] : color.A;

                if (hasAlpha)
                    alphaData >>= 3;
            }
        }
    }

    private static void BuildAlphaTable(byte alpha0, byte alpha1, Span<byte> table)
    {
        table[0] = alpha0;
        table[1] = alpha1;
        if (alpha0 > alpha1)
        {
            table[2] = (byte)(((6 * alpha0) + alpha1) / 7);
            table[3] = (byte)(((5 * alpha0) + (2 * alpha1)) / 7);
            table[4] = (byte)(((4 * alpha0) + (3 * alpha1)) / 7);
            table[5] = (byte)(((3 * alpha0) + (4 * alpha1)) / 7);
            table[6] = (byte)(((2 * alpha0) + (5 * alpha1)) / 7);
            table[7] = (byte)((alpha0 + (6 * alpha1)) / 7);
        }
        else
        {
            table[2] = (byte)(((4 * alpha0) + alpha1) / 5);
            table[3] = (byte)(((3 * alpha0) + (2 * alpha1)) / 5);
            table[4] = (byte)(((2 * alpha0) + (3 * alpha1)) / 5);
            table[5] = (byte)((alpha0 + (4 * alpha1)) / 5);
            table[6] = 0;
            table[7] = 255;
        }
    }

    private static Rgba32 FromRgb565(ushort value)
    {
        byte r = (byte)(((((value >> 11) & 0x1F) * 527) + 23) >> 6);
        byte g = (byte)(((((value >> 5) & 0x3F) * 259) + 33) >> 6);
        byte b = (byte)((((value & 0x1F) * 527) + 23) >> 6);
        return new Rgba32(r, g, b, 255);
    }

    private static Rgba32 Interpolate(Rgba32 c0, Rgba32 c1, int weight0, int weight1)
    {
        int total = weight0 + weight1;
        byte r = (byte)(((c0.R * weight0) + (c1.R * weight1)) / total);
        byte g = (byte)(((c0.G * weight0) + (c1.G * weight1)) / total);
        byte b = (byte)(((c0.B * weight0) + (c1.B * weight1)) / total);
        byte a = (byte)(((c0.A * weight0) + (c1.A * weight1)) / total);
        return new Rgba32(r, g, b, a);
    }
}
