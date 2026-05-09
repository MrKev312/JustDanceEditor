using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace TextureConverter.Formats;

public static class ImageSharpConfiguration
{
    public static void RegisterCustomFormats()
    {
        ImageFormatManager manager = Configuration.Default.ImageFormatsManager;

        manager.SetDecoder(DdsFormat.Instance, new DdsDecoder());
        manager.SetDecoder(SsdFormat.Instance, new SsdDecoder());
        manager.SetDecoder(XtxFormat.Instance, new XtxDecoder());
        manager.SetDecoder(GtxFormat.Instance, new GtxDecoder());
        manager.SetDecoder(Xbox360Format.Instance, new Xbox360Decoder());

        manager.AddImageFormatDetector(new GameTextureDetector());
    }
}
