using JustDanceEditor.Audio;
using JustDanceEditor.Formats.UbiArt.Import;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Text;

using TextureConverter.TextureType;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class WiiCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Wii;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "wii");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, byte[] content)
    {
        // Text file logic is shared with NX (Standard UbiArt behavior)
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, content);
    }

    public async Task WriteBinaryFileAsync(ExportContext context, string relativePath, byte[] data)
    {
        // Binary export is standard byte writing
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, data);
    }

    public async Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // If the file ends in _cover_albumbkg.tga, it should be 64x64
        if (relativePath.EndsWith("_cover_albumbkg.tga", StringComparison.OrdinalIgnoreCase))
            image.Mutate(x => x.Resize(64, 64));

        // First resize everything to 256x256 max
        if (image.Width > 256 || image.Height > 256)
        {
            int newWidth = Math.Min(image.Width, 256);
            int newHeight = Math.Min(image.Height, 256);
            image.Mutate(x => x.Resize(newWidth, newHeight));
        }

        // Convert Image to SSD (Wii Texture format)
        using MemoryStream ssdStream = new();
        SSD.ConvertToFile(image, ssdStream);
        byte[] ssdData = ssdStream.ToArray();

        using FileStream fs = File.Create(fullPath);
        using BinaryWriter writer = new(fs);

        // Let's write the TEX wrapper here.
        WriteTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, (uint)ssdData.Length);
        writer.Write(ssdData);
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath)!);

        try
        {
            await Task.Run(() =>
            {
                using WaveStream waveStream = Path.GetExtension(sourcePath) == ".opus"
                    ? new OpusWaveStream(sourcePath)
                    : new AudioFileReader(sourcePath);

                using FileStream output = File.Create(destPath);

                // Ambient sounds usually use PCM on Wii as well, or ADPCM.
                // Standard Wii songs use DSP ADPCM (Raki container).
                if (Path.GetFileName(destPath).StartsWith("amb_", StringComparison.OrdinalIgnoreCase))
                {
                    // "Wii " signature for Wii PCM
                    //RakiAudioEncoder.EncodeToRakiPcm(waveStream, output, platform: "Wii ", type: "pcm ");
                    // Nvm we also use ADPCM for ambient sounds on Wii, meaning this whole if else can be removed.
                    RakiAudioEncoder.EncodeToRakiCafeAdpcm(waveStream, output, true);
                }
                else
                {
                    // Use the DSP ADPCM encoder for Wii songs (same as GC/Wii/WiiU DSP)
                    RakiAudioEncoder.EncodeToRakiCafeAdpcm(waveStream, output);
                }
            });
        }
        catch (Exception)
        {
            // Fallback
            File.Copy(sourcePath, destPath, true);
        }
    }

    private static void WriteTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint textureSize)
    {
        // Wii TEX Wrapper (Big Endian)
        // Total size = 0x2C (44 bytes)

        // 0x00: Magic
        WriteBigEndian32(writer, 9); // 0x00000009

        // 0x04: "TEX\0"
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));

        // 0x08: Offset to data start (0x2C)
        WriteBigEndian32(writer, 44);

        // 0x0C: Width Info? (Often 0 or width related)
        // On Wii, this is often just 0, or mirrors the texture format flags
        WriteBigEndian32(writer, 0);

        // 0x10: Width (32-bit BE)
        WriteBigEndian32(writer, width);

        // 0x14: Height (32-bit BE)
        WriteBigEndian32(writer, height);

        // 0x18: Resolution/Mips factor? (1)
        WriteBigEndian32(writer, 1);

        // 0x1C: Format Flags
        // 0x00000009 seems standard for Wii/WiiU
        WriteBigEndian32(writer, 0x00000009);

        // 0x20: Compressed Size / Data Size
        WriteBigEndian32(writer, textureSize);

        // 0x24: Uncompressed Estimate (Width * Height * BPP)
        // CMPR is 4 bits per pixel -> W * H / 2
        uint uncompressedSize = (uint)(width * height / 2);
        WriteBigEndian32(writer, uncompressedSize);

        // 0x28: Unknown / Padding
        WriteBigEndian32(writer, 0);
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}