using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.Texture.PlayStation;
using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Raki;
using KevInc.UbiArt.Texture;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.UbiArt.Export.Platform;

public sealed class Ps3CookedPlatformExporter : IPlatformExporter
{
    public UbiArtPlatform Platform => UbiArtPlatform.Cell;

    public string GetPlatformRootFolder(string mapName) => Path.Combine("cache", "itf_cooked", "ps3");

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

        bool isPicto = relativePath.Contains("/pictos/") || relativePath.Contains("\\pictos\\");
        PlayStation3TextureFormat format = UbiArtPlatformExportRules.ShouldUseAlphaTexture(relativePath, image)
            ? PlayStation3TextureFormat.DXT5
            : PlayStation3TextureFormat.DXT1;

        int newWidth = (image.Width + 3) & ~3;
        int newHeight = (image.Height + 3) & ~3;
        if (newWidth != image.Width || newHeight != image.Height)
            image.Mutate(ctx => ctx.Resize(newWidth, newHeight));

        using FileStream fs = File.Create(fullPath);
        UbiArtTextureEncoder.EncodePlayStation3(image, format, fs, isPicto);
        return Task.CompletedTask;
    }

    public async Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null)
    {
        string destPath = context.IO.Combine(context.OutputFolder, relativePath + ".ckd");
        context.IO.CreateDirectory(Path.GetDirectoryName(destPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destPath}'."));

        string tempMp3 = context.IO.Combine(context.IO.GetTempPath(), $"jdi_ps3_{Guid.NewGuid():N}.mp3");
        try
        {
            IConversion conversion = FFmpeg.Conversions.New();
            conversion.SetOverwriteOutput(true);
            conversion.AddParameter($"-i \"{sourcePath}\" -ar 48000 -ac 2 -codec:a libmp3lame -b:a 192k");
            conversion.SetOutput(tempMp3);
            await conversion.Start();

            await using FileStream mp3Source = File.OpenRead(tempMp3);
            await using FileStream output = File.Create(destPath);
            RakiPlayStation3Mp3AudioEncoder.WrapMp3(mp3Source, output, version: GetRakiVersion(context.EngineVersion));
        }
        finally
        {
            if (context.IO.FileExists(tempMp3))
                context.IO.DeleteFile(tempMp3);
        }
    }

    private static bool ShouldWriteAsTextResource(byte[] data)
    {
        byte first = data.FirstOrDefault(b => b != 0 && !char.IsWhiteSpace((char)b));
        return first is (byte)'{' or (byte)'[' or (byte)'<';
    }

    private static uint GetRakiVersion(UbiArtEngineVersion engineVersion)
        => engineVersion == UbiArtEngineVersion.JD2014 ? 8U : 9U;

}
