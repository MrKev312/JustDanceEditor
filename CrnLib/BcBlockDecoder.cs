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

        int bytesPerBlock = CrnFormatHelpers.BytesPerBlock(format);

        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int expectedLength = checked(blockCountX * blockCountY * bytesPerBlock);
        if (blocks.Length != expectedLength)
            throw new InvalidDataException("BC block payload length does not match the supplied dimensions.");

        byte[] rgba = new byte[checked(width * height * 4)];
        switch (format)
        {
            case CrnFormat.Dxt1:
                DecodeDxt1(blocks, width, height, rgba);
                break;
            case CrnFormat.Dxt3:
                DecodeDxt3(blocks, width, height, rgba);
                break;
            case CrnFormat.Dxt5:
            case CrnFormat.Dxt5CcxY:
            case CrnFormat.Dxt5XGxR:
            case CrnFormat.Dxt5XGbr:
            case CrnFormat.Dxt5Agbr:
                DecodeDxt5(blocks, width, height, rgba);
                ApplyDxt5DerivativeConversion(rgba, format);
                break;
            case CrnFormat.Dxt5A:
                DecodeDxt5A(blocks, width, height, rgba);
                break;
            case CrnFormat.DxnXy:
            case CrnFormat.DxnYx:
                DecodeDxn(blocks, width, height, rgba, format);
                break;
            default:
                throw new NotSupportedException($"BC block format '{format}' is not supported.");
        }

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

                WriteColorBlock(destination, colors, code, bx, by, width, height, ReadOnlySpan<byte>.Empty, hasAlpha: false, alphaBits: 0);
            }
        }
    }

    private static void DecodeDxt5(ReadOnlySpan<byte> data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int dataIndex = 0;

        Span<Rgba32> colors = stackalloc Rgba32[4];
        Span<byte> alphas = stackalloc byte[16];

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

    private static void DecodeDxt3(ReadOnlySpan<byte> data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int dataIndex = 0;

        Span<Rgba32> colors = stackalloc Rgba32[4];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                ReadOnlySpan<byte> alphaBlock = data.Slice(dataIndex, 8);
                dataIndex += 8;

                ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(data[dataIndex..]);
                ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(data[(dataIndex + 2)..]);
                uint code = BinaryPrimitives.ReadUInt32LittleEndian(data[(dataIndex + 4)..]);
                dataIndex += 8;

                colors[0] = FromRgb565(color0);
                colors[1] = FromRgb565(color1);
                colors[2] = Interpolate(colors[0], colors[1], 2, 1);
                colors[3] = Interpolate(colors[0], colors[1], 1, 2);

                WriteDxt3Block(destination, colors, code, alphaBlock, bx, by, width, height);
            }
        }
    }

    private static void DecodeDxt5A(ReadOnlySpan<byte> data, int width, int height, byte[] destination)
    {
        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int dataIndex = 0;

        Span<byte> alphas = stackalloc byte[16];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                DecodeAlphaBlock(data.Slice(dataIndex, 8), alphas, destination, bx, by, width, height, AlphaWriteMode.WhiteAlpha);
                dataIndex += 8;
            }
        }
    }

    private static void DecodeDxn(ReadOnlySpan<byte> data, int width, int height, byte[] destination, CrnFormat format)
    {
        int blockCountX = (width + 3) >> 2;
        int blockCountY = (height + 3) >> 2;
        int dataIndex = 0;

        Span<byte> first = stackalloc byte[16];
        Span<byte> second = stackalloc byte[16];

        for (int by = 0; by < blockCountY; by++)
        {
            for (int bx = 0; bx < blockCountX; bx++)
            {
                DecodeAlphaValues(data.Slice(dataIndex, 8), first);
                dataIndex += 8;
                DecodeAlphaValues(data.Slice(dataIndex, 8), second);
                dataIndex += 8;

                WriteDxnBlock(destination, first, second, bx, by, width, height, format);
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

    private static void WriteDxt3Block(
        byte[] destination,
        Span<Rgba32> colors,
        uint code,
        ReadOnlySpan<byte> alphaBlock,
        int blockX,
        int blockY,
        int width,
        int height)
    {
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

                int alphaIndex = py * 4 + px;
                int alphaByte = alphaBlock[alphaIndex >> 1];
                int alpha4 = (alphaIndex & 1) == 0 ? alphaByte & 0xF : alphaByte >> 4;

                int destIndex = ((y * width) + x) * 4;
                Rgba32 color = colors[pixelIndex];
                destination[destIndex] = color.R;
                destination[destIndex + 1] = color.G;
                destination[destIndex + 2] = color.B;
                destination[destIndex + 3] = (byte)((alpha4 << 4) | alpha4);
            }
        }
    }

    private static void DecodeAlphaBlock(
        ReadOnlySpan<byte> block,
        Span<byte> table,
        byte[] destination,
        int blockX,
        int blockY,
        int width,
        int height,
        AlphaWriteMode mode)
    {
        DecodeAlphaValues(block, table);
        for (int py = 0; py < 4; py++)
        {
            for (int px = 0; px < 4; px++)
            {
                int x = (blockX * 4) + px;
                int y = (blockY * 4) + py;
                if (x >= width || y >= height)
                    continue;

                byte value = table[(py * 4) + px];
                int destIndex = ((y * width) + x) * 4;
                if (mode == AlphaWriteMode.WhiteAlpha)
                {
                    destination[destIndex] = 255;
                    destination[destIndex + 1] = 255;
                    destination[destIndex + 2] = 255;
                    destination[destIndex + 3] = value;
                }
            }
        }
    }

    private static void DecodeAlphaValues(ReadOnlySpan<byte> block, Span<byte> values)
    {
        Span<byte> table = stackalloc byte[8];
        BuildAlphaTable(block[0], block[1], table);

        ulong alphaBits = 0;
        for (int i = 0; i < 6; i++)
            alphaBits |= (ulong)block[2 + i] << (8 * i);

        for (int i = 0; i < 16; i++)
        {
            values[i] = table[(int)(alphaBits & 0x7)];
            alphaBits >>= 3;
        }
    }

    private static void WriteDxnBlock(
        byte[] destination,
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        int blockX,
        int blockY,
        int width,
        int height,
        CrnFormat format)
    {
        for (int py = 0; py < 4; py++)
        {
            for (int px = 0; px < 4; px++)
            {
                int x = (blockX * 4) + px;
                int y = (blockY * 4) + py;
                if (x >= width || y >= height)
                    continue;

                int sourceIndex = (py * 4) + px;
                byte r = format == CrnFormat.DxnXy ? first[sourceIndex] : second[sourceIndex];
                byte g = format == CrnFormat.DxnXy ? second[sourceIndex] : first[sourceIndex];
                int destIndex = ((y * width) + x) * 4;
                destination[destIndex] = r;
                destination[destIndex + 1] = g;
                destination[destIndex + 2] = RegenZ(r, g);
                destination[destIndex + 3] = 255;
            }
        }
    }

    private static void ApplyDxt5DerivativeConversion(byte[] rgba, CrnFormat format)
    {
        if (format == CrnFormat.Dxt5)
            return;

        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i];
            byte g = rgba[i + 1];
            byte b = rgba[i + 2];
            byte a = rgba[i + 3];

            switch (format)
            {
                case CrnFormat.Dxt5CcxY:
                    FromYcc(rgba, i, cbPacked: r, crPacked: g, yPacked: a);
                    break;
                case CrnFormat.Dxt5XGxR:
                    rgba[i] = a;
                    rgba[i + 1] = g;
                    rgba[i + 2] = RegenZ(a, g);
                    rgba[i + 3] = 255;
                    break;
                case CrnFormat.Dxt5XGbr:
                    rgba[i] = a;
                    rgba[i + 1] = g;
                    rgba[i + 2] = b;
                    rgba[i + 3] = 255;
                    break;
                case CrnFormat.Dxt5Agbr:
                    rgba[i] = a;
                    rgba[i + 1] = g;
                    rgba[i + 2] = b;
                    rgba[i + 3] = r;
                    break;
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

    private static void FromYcc(byte[] rgba, int offset, byte cbPacked, byte crPacked, byte yPacked)
    {
        const int cbBias = 123;
        const int crBias = 125;
        const int rCr = 91881;
        const int bCb = 116130;
        const int gCr = -46802;
        const int gCb = -22554;

        int y = yPacked;
        int cb = cbPacked - cbBias;
        int cr = crPacked - crBias;

        rgba[offset] = ClampToByte(y + ((rCr * cr + 32768) >> 16));
        rgba[offset + 1] = ClampToByte(y + (((gCr * cr) + (gCb * cb) + 32768) >> 16));
        rgba[offset + 2] = ClampToByte(y + ((bCb * cb + 32768) >> 16));
        rgba[offset + 3] = 255;
    }

    private static byte RegenZ(byte x, byte y)
    {
        float vx = Math.Clamp((x - 128.0f) / 127.0f, -1.0f, 1.0f);
        float vy = Math.Clamp((y - 128.0f) / 127.0f, -1.0f, 1.0f);
        float vz = MathF.Sqrt(Math.Clamp(1.0f - (vx * vx) - (vy * vy), 0.0f, 1.0f));
        vz = (vz * 127.0f) + 128.0f;
        vz += vz < 128.0f ? -0.5f : 0.5f;
        return ClampToByte((int)vz);
    }

    private static byte ClampToByte(int value)
    {
        return (byte)Math.Clamp(value, 0, 255);
    }

    private enum AlphaWriteMode
    {
        WhiteAlpha,
    }
}
