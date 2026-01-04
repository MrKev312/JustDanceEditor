using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public sealed class GtxFormat : IImageFormat
{
    public static readonly GtxFormat Instance = new();

    private GtxFormat() { }

    public string Name => "GTX";
    public string DefaultMimeType => "image/gtx";
    public IEnumerable<string> FileExtensions => new[] { ".gtx", ".tex" };
    public IEnumerable<string> MimeTypes => new[] { "image/gtx" };
}