using JustDanceEditor.Formats.JDI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.UI.Services;

internal sealed class DefaultTextureService : ITextureService
{
    public Image<Bgra32>? ConvertToImage(string path)
    {
        try
        {
            return TextureConverter.TextureConverter.ConvertToImage(path);
        }
        catch
        {
            return null;
        }
    }

    public Task ConvertTextureAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using Image<Bgra32>? image = ConvertToImage(inputPath) ?? throw new InvalidOperationException($"Failed to convert texture: {inputPath}");
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            if (ext == ".png")
            {
                image.Save(outputPath, new PngEncoder());
            }
            else if (ext == ".webp")
            {
                image.Save(outputPath, new WebpEncoder() { FileFormat = WebpFileFormatType.Lossless });
            }
            else
            {
                // Default to PNG
                image.Save(outputPath, new PngEncoder());
            }
        }, cancellationToken);
    }
}