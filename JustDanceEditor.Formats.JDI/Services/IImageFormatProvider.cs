using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.JDI.Services;

/// <summary>
/// Provides the image format/encoder configuration for storing images.
/// This abstraction allows swapping between formats (WEBP, PNG, etc.) in the future.
/// </summary>
public interface IImageFormatProvider
{
    /// <summary>
    /// Gets the file extension (without dot) for the current image format.
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Gets the encoder to use when saving images.
    /// </summary>
    ImageEncoder Encoder { get; }

    /// <summary>
    /// Saves an image to a stream using the configured format.
    /// </summary>
    void Save(Image<Bgra32> image, Stream stream);

    /// <summary>
    /// Saves an image to a file path using the configured format.
    /// </summary>
    void Save(Image<Bgra32> image, string path);

    /// <summary>
    /// Asynchronously saves an image to a stream using the configured format.
    /// </summary>
    Task SaveAsync(Image<Bgra32> image, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously saves an image to a file path using the configured format.
    /// </summary>
    Task SaveAsync(Image<Bgra32> image, string path, CancellationToken cancellationToken = default);
}