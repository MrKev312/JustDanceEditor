using JustDanceEditor.Audio;
using JustDanceEditor.Formats.UbiArt.Import;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using TextureConverter.TextureType;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class WiiUCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.WiiU;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "wiiu");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
        // Text file logic is shared with NX (Standard UbiArt behavior)
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        byte[] serialized = UbiArtEngineContentSerializer.Serialize(content);
        byte[] dataToWrite;

        // Check if this is an SGS file (Scene Graph Settings) which requires 'S' prefix
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
        // Binary export is standard byte writing
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, UbiArtEngineContentSerializer.Serialize(data));
    }

    public async Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        // Convert Image to GTX (Wii U Texture)
        using MemoryStream gtxStream = new();
        // T_BC3_UNORM is equivalent to DXT5, which is standard for UbiArt transparency
        GTX.ConvertToFile(image, GTX.GX2SurfaceFormat.T_BC3_UNORM, gtxStream);
        byte[] gtxData = gtxStream.ToArray();

        using FileStream fs = File.Create(fullPath);
        using BinaryWriter writer = new(fs);

        // Wii U is Big Endian, so the wrapper must be written as BE
        WriteTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, (uint)gtxData.Length);
        writer.Write(gtxData);
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        try
        {
            await Task.Run(() =>
            {
                using WaveStream waveStream = Path.GetExtension(sourcePath) == ".opus"
                    ? new OpusWaveStream(sourcePath)
                    : new AudioFileReader(sourcePath);

                using FileStream output = File.Create(destPath);

                // Official Wii U song and AMB audio both use Cafe DSP ADPCM.
                RakiAudioEncoder.EncodeToRakiCafeAdpcm(waveStream, output);
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
        // Wii U TEX Wrapper (Big Endian)
        WriteBigEndian32(writer, 9); // Magic (0x00000009)
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        WriteBigEndian32(writer, 44); // Header Size (0x2C)

        // Width/Height are 32-bit BE in Wii U UbiArt headers
        WriteBigEndian32(writer, width);
        WriteBigEndian32(writer, height);

        WriteBigEndian32(writer, 1); // Unknown (Resolution Factor?)
        WriteBigEndian32(writer, 0x00000009); // Format Flags (Different from NX)

        WriteBigEndian32(writer, 0); // Unknown
        WriteBigEndian32(writer, 0); // Unknown
        WriteBigEndian32(writer, 0); // Unknown

        WriteBigEndian32(writer, textureSize); // Data Size
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}
