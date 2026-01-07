using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

/// <summary>
/// Image format for WiiU GTX textures.
/// </summary>
public sealed class GtxFormat : IImageFormat
{
    public static readonly GtxFormat Instance = new();

    private GtxFormat() { }

    public string Name => "GTX";
    public string DefaultMimeType => "image/gtx";
    public IEnumerable<string> FileExtensions => [".gtx"];
    public IEnumerable<string> MimeTypes => ["image/gtx"];
}
