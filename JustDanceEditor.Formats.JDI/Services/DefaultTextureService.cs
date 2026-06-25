using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.JDI.Services;

public sealed class DefaultTextureService : ITextureService
{
    public Image<Bgra32>? ConvertToImage(Stream stream)
    {
        try
        {
            return Image.Load<Bgra32>(stream);
        }
        catch
        {
            return null;
        }
    }

    public Task ConvertTextureAsync(Stream inputStream, string outputPath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using Image<Bgra32>? image = ConvertToImage(inputStream) ?? throw new InvalidOperationException("Failed to convert texture");
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));

            if (ext == ".webp")
                image.Save(outputPath, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
            else
                image.Save(outputPath, new PngEncoder());
        }, cancellationToken);
    }
}