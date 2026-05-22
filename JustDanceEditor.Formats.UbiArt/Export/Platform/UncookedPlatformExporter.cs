using KevInc.UbiArt.FileSystem;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class UncookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Uncooked;

    public string GetPlatformRootFolder(string mapName) => ""; // Root is purely relative in Uncooked

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, UbiArtEngineContentSerializer.Serialize(content));
    }

    public async Task WriteBinaryFileAsync(ExportContext context, string relativePath, object data)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, UbiArtEngineContentSerializer.Serialize(data));
    }

    public async Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext == ".png")
        {
            await image.SaveAsync(fullPath, new PngEncoder());
        }
        else
        {
            await image.SaveAsync(fullPath, new TgaEncoder { BitsPerPixel = TgaBitsPerPixel.Pixel32 });
        }
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        if (Path.GetExtension(sourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, true);
        }
        else
        {
            IConversion conversion = FFmpeg.Conversions.New();
            conversion.SetOverwriteOutput(true);
            conversion.AddParameter($"-i \"{sourcePath}\" -ar 48000 -ac 2 -sample_fmt s16");
            conversion.SetOutput(destPath);
            await conversion.Start();
        }
    }
}
