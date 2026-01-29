using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

namespace TextureConverter.TextureType;

public class SSD
{
    public static Image<Bgra32> GetImage(Stream data)
    {
        // Use Big Endian for the main header reading
        using EndianBinaryReader reader = new(data, true);

        byte[] magicBytes = reader.ReadBytes(4);
        string magic = Encoding.ASCII.GetString(magicBytes);
        if (magic != " SDD")
            throw new InvalidOperationException("Invalid Big-Endian DDS (SSD) file signature");

        // DDS Header (Big Endian)
        uint size = reader.ReadUInt32();
        uint flags = reader.ReadUInt32();
        uint height = reader.ReadUInt32();
        uint width = reader.ReadUInt32();
        uint pitch = reader.ReadUInt32();
        uint depth = reader.ReadUInt32();
        uint mips = reader.ReadUInt32();

        // Skip Reserved1 (44 bytes)
        reader.BaseStream.Seek(44, SeekOrigin.Current);

        // Pixel Format - NOTE: This section appears to be Little Endian in Wii SSDs
        // We read it as Big Endian here for stream consistency, but the values might look swapped in debugger.
        // Since we ignore most of it for decoding (relying on heuristics), this is fine for reading.
        uint pfSize = reader.ReadUInt32();
        uint pfFlags = reader.ReadUInt32();
        uint fourCC = reader.ReadUInt32();

        // Skip remaining 40 bytes of DDS header (Caps, Reserved2)
        reader.BaseStream.Seek(40, SeekOrigin.Current);

        // Handle possible metadata tags (TTVN, AAPM) before pixel data
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            long currentPos = reader.BaseStream.Position;
            byte[] tag = reader.ReadBytes(4);

            // Heuristic: If tag contains non-ASCII or 0xFF, it's likely raw data or padding
            if (tag.Length < 4 || tag[0] > 0x7F || tag[1] > 0x7F)
            {
                reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
                break;
            }

            string tagName = Encoding.ASCII.GetString(tag);
            if (tagName == "TTVN" || tagName == "AAPM")
            {
                uint tagVersion = reader.ReadUInt32();
                uint tagSize = reader.ReadUInt32();
                long nextPos = currentPos + tagSize;
                if (nextPos > reader.BaseStream.Length)
                {
                    reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
                    break;
                }

                reader.BaseStream.Seek(nextPos, SeekOrigin.Begin);
            }
            else
            {
                // Unknown tag or start of data
                reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
                break;
            }
        }

        byte[] rawData = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));

        // DXT1 is 4 bits per pixel (0.5 bytes)
        int dxt1Size = (int)(width * height / 2);

        // Extract Color Data
        byte[] colorData = new byte[Math.Min(rawData.Length, dxt1Size)];
        Array.Copy(rawData, 0, colorData, 0, colorData.Length);

        // Extract Alpha Data
        byte[]? alphaData = null;
        int remainingBytes = rawData.Length - dxt1Size;

        // If remaining bytes match DXT1 size, assume it's a CMPA (Color+Alpha) texture
        if (remainingBytes >= dxt1Size)
        {
            alphaData = new byte[dxt1Size];
            Array.Copy(rawData, dxt1Size, alphaData, 0, dxt1Size);
        }

        // 1. Decode Color (Wii CMPR -> Linear DXT1)
        byte[] linearDxt1 = DeswizzleCmpr(colorData, (int)width, (int)height);

        BcDecoder decoder = new();
        using Image<Rgba32> decodedImage = decoder.DecodeRawToImageRgba32(linearDxt1, (int)width, (int)height, CompressionFormat.Bc1);

        // 2. Decode Alpha (Wii CMPR -> Linear DXT1 -> Red Channel is Alpha)
        Image<Rgba32>? decodedAlphaImage = null;
        if (alphaData != null)
        {
            byte[] linearAlpha = DeswizzleCmpr(alphaData, (int)width, (int)height);
            decodedAlphaImage = decoder.DecodeRawToImageRgba32(linearAlpha, (int)width, (int)height, CompressionFormat.Bc1);
        }

        // 3. Merge Color and Alpha
        Image<Bgra32> result = new((int)width, (int)height);
        decodedImage.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> destRow = result.DangerousGetPixelRowMemory(y).Span;
                Span<Rgba32> srcRow = accessor.GetRowSpan(y);

                Span<Rgba32> alphaRow = decodedAlphaImage != null
                    ? decodedAlphaImage.DangerousGetPixelRowMemory(y).Span
                    : Span<Rgba32>.Empty;

                for (int x = 0; x < srcRow.Length; x++)
                {
                    byte a = 255;

                    if (!alphaRow.IsEmpty)
                    {
                        // In CMPA, alpha is stored as the Red channel of the second DXT1 texture
                        a = alphaRow[x].R;
                    }
                    else
                    {
                        a = srcRow[x].A;
                    }

                    destRow[x] = new Bgra32(srcRow[x].R, srcRow[x].G, srcRow[x].B, a);
                }
            }
        });

        decodedAlphaImage?.Dispose();
        return result;
    }

    public static void ConvertToFile(Image<Bgra32> image, Stream output)
    {
        uint width = (uint)image.Width;
        uint height = (uint)image.Height;
        uint mipCount = 1;

        // Check for transparency
        bool hasTransparency = CheckTransparency(image);

        // 1. Encode Main Color (DXT1)
        BcEncoder encoder = new()
        {
            OutputOptions = { GenerateMipMaps = false, Quality = CompressionQuality.BestQuality, Format = CompressionFormat.Bc1 }
        };

        // Convert to Rgba32 for encoder
        using Image<Rgba32> rgbaImage = image.CloneAs<Rgba32>();

        // Encode to linear DXT1
        byte[] linearColor = encoder.EncodeToRawBytes(rgbaImage)[0];

        // Swizzle to Wii CMPR layout
        byte[] swizzledColor = SwizzleCmpr(linearColor, (int)width, (int)height);

        // 2. Encode Alpha (DXT1) if needed
        byte[]? swizzledAlpha = null;
        if (hasTransparency)
        {
            // Create a grayscale image where R=G=B = Original Alpha
            using Image<Rgba32> alphaMap = new((int)width, (int)height);
            rgbaImage.ProcessPixelRows(alphaMap, (src, dst) =>
            {
                for (int y = 0; y < src.Height; y++)
                {
                    Span<Rgba32> sRow = src.GetRowSpan(y);
                    Span<Rgba32> dRow = dst.GetRowSpan(y);
                    for (int x = 0; x < sRow.Length; x++)
                    {
                        byte a = sRow[x].A;
                        dRow[x] = new Rgba32(a, a, a, 255);
                    }
                }
            });

            byte[] linearAlpha = encoder.EncodeToRawBytes(alphaMap)[0];
            swizzledAlpha = SwizzleCmpr(linearAlpha, (int)width, (int)height);
        }

        // 3. Write Header
        WriteSsdHeader(output, width, height, mipCount, hasTransparency, swizzledColor.Length + (swizzledAlpha?.Length ?? 0));

        // 4. Write Data
        output.Write(swizzledColor);
        if (swizzledAlpha != null)
        {
            output.Write(swizzledAlpha);
        }
    }

    private static bool CheckTransparency(Image<Bgra32> image)
    {
        bool hasTrans = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A < 255)
                    {
                        hasTrans = true;
                        return; // Break early
                    }
                }

                if (hasTrans)
                    return;
            }
        });
        return hasTrans;
    }

    private static void WriteSsdHeader(Stream output, uint width, uint height, uint mips, bool hasTransparency, int dataSize)
    {
        // SSD file is fundamentally Big Endian
        using EndianBinaryWriter writer = new(output, Encoding.ASCII, true, true);

        // --- Main Header (Big Endian) ---
        writer.Write(Encoding.ASCII.GetBytes(" SDD"));
        writer.Write(124U); // Size

        // Flags: DDSCAPS | HEIGHT | WIDTH | PITCH | PIXELFORMAT = 0x100F
        writer.Write(0x0000100FU);

        writer.Write(height);
        writer.Write(width);
        writer.Write(width * 4); // Pitch: Width * 4
        writer.Write(1U); // Depth
        writer.Write(mips);

        // Reserved1 (11 uints)
        for (int i = 0; i < 11; i++)
            writer.Write(0U);

        // --- Pixel Format (Little Endian!) ---
        // We manually write bytes here to enforce Little Endian order regardless of the writer's mode.
        // Size: 32 (0x20)
        writer.Write((byte)0x20);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        // Flags: DDPF_FOURCC (0x04)
        writer.Write((byte)0x04);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        // FourCC
        // Opaque: "1TXD" (0x31545844) -> Write as is (ASCII bytes)
        // Transparent: "CMPA" (0x41504D43) -> Write as is
        // Note: The "official" Opaque dump shows "04 31 54 58 44" (04 flags, then 1TXD).
        // Standard DXT1 FourCC is 0x31545844.
        if (hasTransparency)
        {
            // CMPA
            writer.Write((byte)0x41);
            writer.Write((byte)0x50);
            writer.Write((byte)0x4D);
            writer.Write((byte)0x43);
        }
        else
        {
            // 1TXD
            writer.Write((byte)0x31);
            writer.Write((byte)0x54);
            writer.Write((byte)0x58);
            writer.Write((byte)0x44);
        }

        // RGBBitCount, RMask, GMask, BMask, AMask (All 0)
        for (int i = 0; i < 5; i++)
        {
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
        }

        // --- Caps (Big Endian) ---
        // Back to normal writer flow
        writer.Write(0x00001000U); // DDSCAPS_TEXTURE
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U);
        writer.Write(0U); // Reserved2
    }

    /// <summary>
    /// Swizzles Linear DXT1 data into Wii CMPR tile order.
    /// Inverse of DeswizzleCmpr.
    /// </summary>
    private static byte[] SwizzleCmpr(byte[] data, int width, int height)
    {
        byte[] output = new byte[data.Length];
        int blocksWide = (width + 3) / 4;
        int blocksHigh = (height + 3) / 4;
        int destOffset = 0; // Writing sequentially to output (Swizzled)

        // Iterate over 8x8 tiles (each tile contains 4 DXT1 blocks)
        for (int ty = 0; ty < height; ty += 8)
        {
            for (int tx = 0; tx < width; tx += 8)
            {
                // Each 8x8 tile has 4 blocks in a Z-pattern:
                // 0 1
                // 2 3
                for (int i = 0; i < 4; i++)
                {
                    int bx = (tx / 4) + (i % 2);
                    int by = (ty / 4) + (i / 2);

                    if (bx < blocksWide && by < blocksHigh)
                    {
                        // Calculate where this block lives in the linear input data
                        int sourceOffset = ((by * blocksWide) + bx) * 8;

                        if (sourceOffset + 8 <= data.Length && destOffset + 8 <= output.Length)
                        {
                            // Swap Color 0 Endianness (Byte 0 <-> Byte 1)
                            output[destOffset + 0] = data[sourceOffset + 1];
                            output[destOffset + 1] = data[sourceOffset + 0];

                            // Swap Color 1 Endianness (Byte 2 <-> Byte 3)
                            output[destOffset + 2] = data[sourceOffset + 3];
                            output[destOffset + 3] = data[sourceOffset + 2];

                            // Swap Indices bits (Same function works both ways)
                            output[destOffset + 4] = SwapBitPairs(data[sourceOffset + 4]);
                            output[destOffset + 5] = SwapBitPairs(data[sourceOffset + 5]);
                            output[destOffset + 6] = SwapBitPairs(data[sourceOffset + 6]);
                            output[destOffset + 7] = SwapBitPairs(data[sourceOffset + 7]);
                        }
                    }

                    destOffset += 8;
                }
            }
        }

        return output;
    }

    /// <summary>
    /// Deswizzles the Color channel (Wii CMPR format).
    /// </summary>
    private static byte[] DeswizzleCmpr(byte[] data, int width, int height)
    {
        byte[] output = new byte[data.Length];
        int blocksWide = (width + 3) / 4;
        int blocksHigh = (height + 3) / 4;
        int sourceOffset = 0;

        for (int ty = 0; ty < height; ty += 8)
        {
            for (int tx = 0; tx < width; tx += 8)
            {
                // 4 sub-blocks per 8x8 tile in Z-order
                for (int i = 0; i < 4; i++)
                {
                    int bx = (tx / 4) + (i % 2);
                    int by = (ty / 4) + (i / 2);

                    if (bx < blocksWide && by < blocksHigh)
                    {
                        int destOffset = ((by * blocksWide) + bx) * 8;
                        if (sourceOffset + 8 <= data.Length && destOffset + 8 <= output.Length)
                        {
                            // Swap endianness of color values (16-bit)
                            output[destOffset + 0] = data[sourceOffset + 1];
                            output[destOffset + 1] = data[sourceOffset + 0];
                            output[destOffset + 2] = data[sourceOffset + 3];
                            output[destOffset + 3] = data[sourceOffset + 2];

                            // Swap bit pairs in index bytes
                            output[destOffset + 4] = SwapBitPairs(data[sourceOffset + 4]);
                            output[destOffset + 5] = SwapBitPairs(data[sourceOffset + 5]);
                            output[destOffset + 6] = SwapBitPairs(data[sourceOffset + 6]);
                            output[destOffset + 7] = SwapBitPairs(data[sourceOffset + 7]);
                        }
                    }

                    sourceOffset += 8;
                }
            }
        }

        return output;
    }

    private static byte SwapBitPairs(byte b)
    {
        // Reverses 2-bit pairs: [3][2][1][0] -> [0][1][2][3]
        return (byte)(((b & 0x03) << 6) | ((b & 0x0C) << 2) | ((b & 0x30) >> 2) | ((b & 0xC0) >> 6));
    }
}