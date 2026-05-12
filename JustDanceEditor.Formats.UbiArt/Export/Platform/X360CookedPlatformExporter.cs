using KevInc.Texture.ImageSharp;
using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Texture;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class X360CookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.X360;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "x360");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        byte[] serialized = UbiArtEngineContentSerializer.Serialize(content);

        if (!ShouldWriteAsTextResource(serialized))
        {
            context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
            await File.WriteAllBytesAsync(fullPath, serialized);
            return;
        }

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

    public Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
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

        using FileStream fs = File.Create(fullPath);
        UbiArtTextureEncoder.EncodePcDds(image, format, fs, isPicto);
        return Task.CompletedTask;
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        if (!IsX360Xma2Raki(sourcePath))
            throw new NotSupportedException("X360 export requires real XMA2 audio. FFmpeg can verify/decode XMA2, but this project does not yet have a cross-platform XMA2 encoder for WAV/Opus input.");

        await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: true));
    }

    private static bool ShouldWriteAsTextResource(byte[] data)
    {
        byte first = data.FirstOrDefault(b => b != 0 && !char.IsWhiteSpace((char)b));
        return first is (byte)'{' or (byte)'[' or (byte)'<';
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

    private static bool IsX360Xma2Raki(string path)
    {
        try
        {
            byte[] header = new byte[20];
            using FileStream fs = File.OpenRead(path);
            int bytesRead = fs.Read(header, 0, header.Length);

            return bytesRead >= 16 && IsX360Xma2RakiAt(header, 0)
                || bytesRead >= 20 && IsX360Xma2RakiAt(header, 4);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsX360Xma2RakiAt(byte[] header, int offset)
    {
        return HasAscii(header, offset, "RAKI")
            && HasAscii(header, offset + 8, "X360")
            && HasAscii(header, offset + 12, "xma2");
    }

    private static bool HasAscii(byte[] data, int offset, string value)
    {
        if (offset < 0 || offset + value.Length > data.Length)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            if (data[offset + i] != value[i])
                return false;
        }

        return true;
    }
}
