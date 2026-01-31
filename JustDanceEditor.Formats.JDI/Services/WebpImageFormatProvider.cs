using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.JDI.Services;

/// <summary>
/// Default image format provider using lossless WEBP.
/// </summary>
public sealed class WebpImageFormatProvider : IImageFormatProvider
{
    /// <summary>
    /// Singleton instance using lossless WEBP encoding.
    /// </summary>
    public static WebpImageFormatProvider Lossless { get; } = new(lossless: true);

    /// <summary>
    /// Singleton instance using lossy WEBP encoding with high quality.
    /// </summary>
    public static WebpImageFormatProvider Lossy { get; } = new(lossless: false);

    private readonly WebpEncoder _encoder;

    public WebpImageFormatProvider(bool lossless = true, int quality = 100)
    {
        _encoder = new WebpEncoder
        {
            FileFormat = lossless ? WebpFileFormatType.Lossless : WebpFileFormatType.Lossy,
            Quality = quality
        };
    }

    /// <inheritdoc />
    public string FileExtension => "webp";

    /// <inheritdoc />
    public ImageEncoder Encoder => _encoder;

    /// <inheritdoc />
    public void Save(Image<Bgra32> image, Stream stream)
    {
        image.Save(stream, _encoder);
    }

    /// <inheritdoc />
    public void Save(Image<Bgra32> image, string path)
    {
        image.Save(path, _encoder);
    }

    /// <inheritdoc />
    public Task SaveAsync(Image<Bgra32> image, Stream stream, CancellationToken cancellationToken = default)
    {
        return image.SaveAsync(stream, _encoder, cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveAsync(Image<Bgra32> image, string path, CancellationToken cancellationToken = default)
    {
        return image.SaveAsync(path, _encoder, cancellationToken);
    }
}