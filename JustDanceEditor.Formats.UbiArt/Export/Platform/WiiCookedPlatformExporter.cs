using KevInc.Audio.NAudio;
using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class WiiCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Wii;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "wii");

    public async Task WriteEngineResourceAsync(ExportContext context, string relativePath, object content)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));
        await File.WriteAllBytesAsync(fullPath, UbiArtEngineContentSerializer.Serialize(content));
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

        using FileStream fs = File.Create(fullPath);
        UbiArtTextureEncoder.EncodeWiiSsd(image, fs);
        return Task.CompletedTask;
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
                    RakiCafeDspAdpcmAudioEncoder.Encode(waveStream, output, true);
                }
                else
                {
                    RakiCafeDspAdpcmAudioEncoder.Encode(waveStream, output);
                }
            });
        }
        catch (Exception)
        {
            File.Copy(sourcePath, destPath, true);
        }
    }

}

