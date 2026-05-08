using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using CrnLib;

namespace TextureConverter.TextureConverterHelpers;

public class TextureEncoderDecoder
{
    public static byte[] Encode(Image<Rgba32> image, int width, int height, TextureFormat format, int quality = 5, int mips = 1)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (format is not TextureFormat.DXT1Crunched and not TextureFormat.DXT5Crunched)
            // Wrong format, throw exception
            throw new Exception($"Unsupported format: {format}");

        if (image.Width != width || image.Height != height)
            throw new ArgumentException("The supplied dimensions do not match the ImageSharp image.", nameof(image));

        return CrunchTexture.EncodeUnityCrunch(image, ToCrnFormat(format), new CrnEncodeOptions
        {
            MipCount = mips,
            Quality = MapQuality(quality),
            UserData0 = 1,
        });
    }

    public static byte[] DecodeCrunch(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Crunch payload cannot be null or empty.", nameof(data));

        return CrunchTexture.DecodeUnityCrunch(data);
    }

    public static Image<Rgba32> DecodeCrunchImage(byte[] data)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("Crunch payload cannot be null or empty.", nameof(data));

        return CrunchTexture.DecodeUnityCrunchImage(data);
    }

    private static CrnFormat ToCrnFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.DXT1Crunched => CrnFormat.Dxt1,
            TextureFormat.DXT5Crunched => CrnFormat.Dxt5,
            _ => throw new Exception($"Unsupported format: {format}"),
        };
    }

    private static CrnCompressionQuality MapQuality(int quality)
    {
        return quality < 0 ? CrnCompressionQuality.Fast : CrnCompressionQuality.BestQuality;
    }
}
