using SixLabors.ImageSharp.Formats.Webp;

namespace JustDanceEditor.Formats.JDI.Utilities;

public static class WebpSettings
{
    public static readonly WebpEncoder LosslessWebpEncoder = new()
    {
        FileFormat = WebpFileFormatType.Lossless,
        Quality = 100
    };
}
