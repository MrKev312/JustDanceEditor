using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public sealed class SsdFormat : IImageFormat
{
    public static readonly SsdFormat Instance = new();

    private SsdFormat() { }

    public string Name => "SSD";
    public string DefaultMimeType => "image/vnd.ms-dds-be";
    public IEnumerable<string> FileExtensions => new[] { ".ssd", ".tex" };
    public IEnumerable<string> MimeTypes => new[] { "image/vnd.ms-dds-be" };
}