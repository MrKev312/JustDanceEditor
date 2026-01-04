using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public sealed class DdsFormat : IImageFormat
{
    public static readonly DdsFormat Instance = new();

    private DdsFormat() { }

    public string Name => "DDS";
    public string DefaultMimeType => "image/vnd.ms-dds";
    public IEnumerable<string> FileExtensions => new[] { ".dds", ".tex" };
    public IEnumerable<string> MimeTypes => new[] { "image/vnd.ms-dds" };
}