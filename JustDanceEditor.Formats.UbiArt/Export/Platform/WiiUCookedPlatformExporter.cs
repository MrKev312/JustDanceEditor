using KevInc.Texture.Nintendo.ImageSharp;
using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public class WiiUCookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Cafe;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "wiiu");

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

    private static bool ShouldWriteAsTextResource(byte[] data)
    {
        byte first = data.FirstOrDefault(b => b != 0 && !char.IsWhiteSpace((char)b));
        return first is (byte)'{' or (byte)'[' or (byte)'<';
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

        GTX.GX2SurfaceFormat format = UbiArtPlatformExportRules.ShouldUseAlphaTexture(relativePath, image)
            ? GTX.GX2SurfaceFormat.T_BC3_UNORM
            : GTX.GX2SurfaceFormat.T_BC1_UNORM;

        using FileStream fs = File.Create(fullPath);
        UbiArtTextureEncoder.EncodeWiiUGtx(image, format, fs);
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

            RakiCafeDspAdpcmAudioEncoder.Encode(
                waveStream,
                output,
                splitChannels: UbiArtPlatformExportRules.IsAmbAudio(destPath),
                platform: UbiArtPlatformExportRules.GetAdpcmPlatform(Platform),
                version: UbiArtPlatformExportRules.GetAdpcmVersion(Platform, context.EngineVersion));
        });
    }
}
