using JustDanceEditor.Audio;
using JustDanceEditor.Formats.UbiArt.Import;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text;

using TextureConverter.TextureType;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class NxCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.NX;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "nx");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
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
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, UbiArtEngineContentSerializer.Serialize(data));
    }

    public async Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        using MemoryStream xtxStream = new();
        XTX.ConvertToFile(image, XTX.XTXImageFormat.DXT5, xtxStream);
        byte[] xtxData = xtxStream.ToArray();

        using FileStream fs = File.Create(fullPath);
        using BinaryWriter writer = new(fs);

        WriteTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, (uint)xtxData.Length);
        writer.Write(xtxData);
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

                if (Path.GetFileName(destPath).StartsWith("amb_", StringComparison.OrdinalIgnoreCase))
                {
                    RakiAudioEncoder.EncodeToRakiPcm(waveStream, output, platform: "Nx  ", type: "pcm ");
                }
                else
                {
                    RakiAudioEncoder.EncodeToRakiNxOpus(waveStream, output, markers);
                }
            });
        }
        catch (Exception)
        {
            File.Copy(sourcePath, destPath, true);
        }
    }

    private static void WriteTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint xtxDataSize)
    {
        writer.Write([0, 0, 0, 9]); // Magic
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        WriteBigEndian32(writer, 44); // Offset
        uint widthInfo = ((uint)width << 8) | 0x0080;
        writer.Write(widthInfo);
        writer.Write(width);
        writer.Write(height);
        writer.Write(0x00012000); // Format
        writer.Write(widthInfo);
        writer.Write((uint)0);
        writer.Write(0x4E4E0004); // NN Marker
        uint crc = (uint)(((width * height) ^ 0xA3E908) & 0xFFFFFFFF);
        writer.Write(crc);
        writer.Write((uint)0);
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}
