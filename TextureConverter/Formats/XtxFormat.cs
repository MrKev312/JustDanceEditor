using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public sealed class XtxFormat : IImageFormat
{
    public static readonly XtxFormat Instance = new();

    private XtxFormat() { }

    public string Name => "XTX";
    public string DefaultMimeType => "image/xtx";
    public IEnumerable<string> FileExtensions => new[] { ".xtx" };
    public IEnumerable<string> MimeTypes => new[] { "image/xtx" };
}