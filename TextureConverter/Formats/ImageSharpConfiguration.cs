using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public static class ImageSharpConfiguration
{
    public static void RegisterCustomFormats()
    {
        ImageFormatManager manager = Configuration.Default.ImageFormatsManager;

        manager.SetDecoder(DdsFormat.Instance, new DdsDecoder());
        manager.SetDecoder(XtxFormat.Instance, new XtxDecoder());
        manager.SetDecoder(GtxFormat.Instance, new GtxDecoder());

        manager.AddImageFormatDetector(new GameTextureDetector());
    }
}