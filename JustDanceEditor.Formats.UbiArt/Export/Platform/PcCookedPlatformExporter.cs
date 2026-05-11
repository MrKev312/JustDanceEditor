using KevInc.Audio.NAudio;
using KevInc.Raki.NAudio;
using JustDanceEditor.Formats.UbiArt.Import;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Text;

using KevInc.Texture.ImageSharp;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class PcCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.PC;

    // PC cooked assets usually reside in cache/itf_cooked/pc
    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "pc");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        byte[] serialized = UbiArtEngineContentSerializer.Serialize(content);
        byte[] dataToWrite;

        // Add 'S' prefix for SGS files
        if (relativePath.EndsWith(".sgs", StringComparison.OrdinalIgnoreCase))
        {
            dataToWrite = new byte[1 + serialized.Length + 1];
            dataToWrite[0] = (byte)'S';
            Array.Copy(serialized, 0, dataToWrite, 1, serialized.Length);
            dataToWrite[^1] = 0; // Null terminator
        }
        else
        {
            dataToWrite = new byte[serialized.Length + 1];
            Array.Copy(serialized, dataToWrite, serialized.Length);
            dataToWrite[^1] = 0; // Null terminator
        }

        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await using FileStream fs = File.Create(fullPath);
        await fs.WriteAsync(dataToWrite);
    }

    public async Task WriteBinaryFileAsync(ExportContext context, string relativePath, object data)
    {
        // Binary files (Actors) are generated as BigEndian by the ContentGenerator, so we write them directly
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, UbiArtEngineContentSerializer.Serialize(data));
    }

    public async Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        // Determine format based on transparency usage
        // DXT5 (BC3) for transparency, DXT1 (BC1) for opaque
        bool hasAlpha = HasTransparency(image);
        // Does the path end in a picto folder?
        bool isPicto = relativePath.Contains("/pictos/") || relativePath.Contains("\\pictos\\");

        DDS.DDSFormat format = hasAlpha ? DDS.DDSFormat.DXT5 : DDS.DDSFormat.DXT1;

        // If one of the sides is not a multiple of 4, we need to resize to the next multiple of 4
        int newWidth = (image.Width + 3) & ~3;
        int newHeight = (image.Height + 3) & ~3;
        if (newWidth != image.Width || newHeight != image.Height)
            image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

        using MemoryStream ddsStream = new();
        DDS.ConvertToFile(image, format, ddsStream);
        byte[] ddsData = ddsStream.ToArray();

        using FileStream fs = File.Create(fullPath);
        using BinaryWriter writer = new(fs);

        // PC .ckd Textures use a Big-Endian TEX wrapper around standard Little-Endian DDS data
        WriteTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, (uint)ddsData.Length, isPicto);

        // Write the inner DDS data
        writer.Write(ddsData);
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        try
        {
            await Task.Run(() =>
            {
                // Convert input to WaveStream
                using WaveStream waveStream = Path.GetExtension(sourcePath) == ".opus"
                    ? new OpusWaveStream(sourcePath)
                    : new AudioFileReader(sourcePath);

                using FileStream output = File.Create(destPath);

                // Encode to RAKI container with PCM payload
                // "Win " platform signature is used for PC, which RakiPcmAudioEncoder handles as Little-Endian
                RakiPcmAudioEncoder.Encode(waveStream, output, platform: "Win ", type: "pcm ");
            });
        }
        catch (Exception)
        {
            // Fallback
            File.Copy(sourcePath, destPath, true);
        }
    }

    private static void WriteTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint innerDataSize, bool isPicto)
    {
        // 0x00: Magic 00 00 00 09
        writer.Write([0, 0, 0, 9]);

        // 0x04: Signature "TEX\0"
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));

        // 0x08: Offset to data (Standard is 0x2C / 44 bytes)
        WriteBigEndian32(writer, 44);

        // 0x0C: Width Info (Width << 8 | 0x80)
        uint widthInfo = ((uint)width << 8) | 0x0080;
        WriteBigEndian32(writer, widthInfo);

        // 0x10: Dimensions
        WriteBigEndian16(writer, width);
        WriteBigEndian16(writer, height);

        // 0x14: Format Flags (0x00012000 is standard for PC CKD)
        WriteBigEndian32(writer, 0x00012000);

        // 0x18: Width Info repeated
        WriteBigEndian32(writer, widthInfo);

        // 0x1C: Padding (Always 0)
        WriteBigEndian32(writer, 0);

        // --- Metadata Section (The 12 bytes gap filling 0x20 to 0x2C) ---

        // 0x20: Compressed Size (The size of the DDS data)
        WriteBigEndian32(writer, innerDataSize);

        // 0x24: Uncompressed Size (Estimate: Width * Height * 4 bytes per pixel)
        // This is strictly not required to be perfect, but standard for UbiArt headers.
        uint uncompressedSize = (uint)(width * height * 4);
        WriteBigEndian32(writer, uncompressedSize);

        // 0x28: Texture Signature (00 00 CC CC)
        // CC CC indicates a standard cooked texture. 
        if (isPicto)
            writer.Write([0x02, 0x02, 0xCC, 0xCC]); // Picto textures use 02 02 CC CC
        else
            writer.Write([0x00, 0x00, 0xCC, 0xCC]);

        // We have now written exactly 44 bytes (0x2C).
        // The DDS header must start IMMEDIATELY after this.
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteBigEndian16(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static bool HasTransparency(Image<Bgra32> image)
    {
        bool hasAlpha = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                if (hasAlpha)
                    break;
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A < 255)
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }
        });
        return hasAlpha;
    }
}

