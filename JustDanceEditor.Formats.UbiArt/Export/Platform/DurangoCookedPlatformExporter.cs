using JustDanceEditor.Audio;
using JustDanceEditor.Formats.UbiArt.Import;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Text;

using TextureConverter.TextureType;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class DurangoCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Durango;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "durango");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        byte[] serialized = UbiArtEngineContentSerializer.Serialize(content);
        byte[] dataToWrite;

        if (relativePath.EndsWith(".sgs", StringComparison.OrdinalIgnoreCase))
        {
            dataToWrite = new byte[1 + serialized.Length + 1];
            dataToWrite[0] = (byte)'S';
            Array.Copy(serialized, 0, dataToWrite, 1, serialized.Length);
            dataToWrite[^1] = 0;
        }
        else
        {
            dataToWrite = new byte[serialized.Length + 1];
            Array.Copy(serialized, dataToWrite, serialized.Length);
            dataToWrite[^1] = 0;
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

        bool hasAlpha = HasTransparency(image);
        bool isPicto = relativePath.Contains("/pictos/") || relativePath.Contains("\\pictos\\");

        DDS.DDSFormat format = hasAlpha ? DDS.DDSFormat.DXT5 : DDS.DDSFormat.DXT1;

        int newWidth = (image.Width + 3) & ~3;
        int newHeight = (image.Height + 3) & ~3;
        if (newWidth != image.Width || newHeight != image.Height)
            image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

        using MemoryStream ddsStream = new();
        DDS.ConvertToFile(image, format, ddsStream);
        byte[] ddsData = ddsStream.ToArray();

        await using FileStream fs = File.Create(fullPath);
        using BinaryWriter writer = new(fs);

        WriteTexWrapperHeader(writer, (ushort)image.Width, (ushort)image.Height, (uint)ddsData.Length, isPicto);
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
                using WaveStream waveStream = Path.GetExtension(sourcePath).Equals(".opus", StringComparison.OrdinalIgnoreCase)
                    ? new OpusWaveStream(sourcePath)
                    : new AudioFileReader(sourcePath);

                using FileStream output = File.Create(destPath);
                RakiAudioEncoder.EncodeToRakiPcm(waveStream, output, platform: "Dura", type: "pcm ");
            });
        }
        catch (Exception)
        {
            File.Copy(sourcePath, destPath, true);
        }
    }

    private static void WriteTexWrapperHeader(BinaryWriter writer, ushort width, ushort height, uint innerDataSize, bool isPicto)
    {
        writer.Write([0, 0, 0, 9]);
        writer.Write(Encoding.ASCII.GetBytes("TEX\0"));
        WriteBigEndian32(writer, 44);

        uint widthInfo = ((uint)width << 8) | 0x0080;
        WriteBigEndian32(writer, widthInfo);
        WriteBigEndian16(writer, width);
        WriteBigEndian16(writer, height);
        WriteBigEndian32(writer, 0x00012000);
        WriteBigEndian32(writer, widthInfo);
        WriteBigEndian32(writer, 0);
        WriteBigEndian32(writer, innerDataSize);
        WriteBigEndian32(writer, (uint)(width * height * 4));

        if (isPicto)
            writer.Write([0x02, 0x02, 0xCC, 0xCC]);
        else
            writer.Write([0x00, 0x00, 0xCC, 0xCC]);
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
