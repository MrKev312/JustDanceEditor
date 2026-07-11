using KevInc.Texture.ImageSharp;
using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class OrbisCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Orbis;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "orbis");

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

    public Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        bool isPicto = relativePath.Contains("/pictos/") || relativePath.Contains("\\pictos\\");
        DDS.DDSFormat format = UbiArtPlatformExportRules.ShouldUseAlphaTexture(relativePath, image)
            ? DDS.DDSFormat.DXT5
            : DDS.DDSFormat.DXT1;

        int newWidth = (image.Width + 3) & ~3;
        int newHeight = (image.Height + 3) & ~3;
        if (newWidth != image.Width || newHeight != image.Height)
            image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

        uint samplerFlags = Path.GetFileName(relativePath).Equals("montage.png", StringComparison.OrdinalIgnoreCase)
            ? 0x02020000U
            : 0U;

        using FileStream fs = File.Create(fullPath);
        UbiArtTextureEncoder.EncodePcDds(image, format, fs, isPicto, samplerFlags);
        return Task.CompletedTask;
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, UbiArtAudioExportSource source)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        await Task.Run(() =>
        {
            using WaveStream waveStream = source.OpenWaveStream();
            using FileStream output = File.Create(destPath);
            RakiPcmAudioEncoder.Encode(waveStream, output, platform: "Orbi", type: "pcm ");
        });
    }
}