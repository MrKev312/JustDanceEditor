using KevInc.Texture.Nintendo.ImageSharp;
using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

    public Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image)
    {
        string fullPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{fullPath}'."));

        XTX.XTXImageFormat format = UbiArtPlatformExportRules.ShouldUseAlphaTexture(relativePath, image)
            ? XTX.XTXImageFormat.DXT5
            : XTX.XTXImageFormat.DXT1;

        ushort? wrapperWidth = UbiArtPlatformExportRules.UsesLogicalOnlineCoverDimensions(relativePath) ? (ushort)1024 : null;
        ushort? wrapperHeight = UbiArtPlatformExportRules.UsesLogicalOnlineCoverDimensions(relativePath) ? (ushort)1024 : null;

        using FileStream fs = File.Create(fullPath);
        UbiArtTextureEncoder.EncodeNxXtx(
            image,
            format,
            fs,
            wrapperWidth,
            wrapperHeight,
            UbiArtPlatformExportRules.GetNxSamplerFlags(relativePath));
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

            if (Path.GetFileName(destPath).StartsWith("amb_", StringComparison.OrdinalIgnoreCase))
                RakiPcmAudioEncoder.Encode(waveStream, output, platform: "Nx  ", type: "pcm ");
            else
                RakiNintendoSwitchOpusAudioEncoder.Encode(waveStream, output);
        });
    }
}