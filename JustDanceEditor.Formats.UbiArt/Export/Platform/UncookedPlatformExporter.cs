using JustDanceEditor.Formats.UbiArt.Import;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class UncookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Uncooked;

    public string GetPlatformRootFolder(string mapName) => ""; // Root is purely relative in Uncooked

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, byte[] content)
    {
        // No .ckd, no trailing null
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, content);
    }

    public async Task WriteBinaryFileAsync(ExportContext context, string relativePath, byte[] data)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, data);
    }

    public async Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        // If extension is .tga, save TGA. If .png, save PNG.
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        string ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext == ".png")
        {
            await image.SaveAsync(fullPath, new PngEncoder());
        }
        else
        {
            // Default to TGA for Uncooked UbiArt textures usually
            await image.SaveAsync(fullPath, new TgaEncoder { BitsPerPixel = TgaBitsPerPixel.Pixel32 });
        }
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        // Standard WAV copy or conversion (no RAKI)
        string destPath = context.IO.Combine(context.OutputFolder, relativePath);
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        if (Path.GetExtension(sourcePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, true);
        }
        else
        {
            // Convert to WAV
            using MediaFoundationReader reader = new(sourcePath);
            WaveFileWriter.CreateWaveFile(destPath, reader);
        }
    }
}