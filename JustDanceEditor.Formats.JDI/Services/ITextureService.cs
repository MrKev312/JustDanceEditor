using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.JDI.Services;

public interface ITextureService
{
    /// <summary>
    /// Convert a texture file to an Image, or return null if it cannot be decoded.
    /// </summary>
    Image<Bgra32>? ConvertToImage(Stream stream);

    /// <summary>
    /// Convert or transcode a texture stream to the target output path (e.g., PNG, WEBP).
    /// </summary>
    Task ConvertTextureAsync(Stream inputStream, string outputPath, CancellationToken cancellationToken = default);
}