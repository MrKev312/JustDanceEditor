using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.JDI.Services;

public interface ITextureService
{
    /// <summary>
    /// Convert a texture file to an Image, or return null if it cannot be decoded.
    /// </summary>
    Image<Bgra32>? ConvertToImage(string path);

    /// <summary>
    /// Convert or transcode a texture file to the target output path (e.g., PNG, WEBP).
    /// </summary>
    Task ConvertTextureAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default);
}