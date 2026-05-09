using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public sealed class Xbox360Format : IImageFormat
{
    public static readonly Xbox360Format Instance = new();

    private Xbox360Format() { }

    public string Name => "Xbox360";
    public string DefaultMimeType => "image/x-xbox360-texture";
    public IEnumerable<string> FileExtensions => [".ckd", ".x360tex", ".360tex"];
    public IEnumerable<string> MimeTypes => [DefaultMimeType];
}
