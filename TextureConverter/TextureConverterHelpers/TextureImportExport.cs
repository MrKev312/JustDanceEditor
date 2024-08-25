using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TextureConverter.TextureConverterHelpers;

public class TextureImportExport
{
    public static byte[] Import(
        Image<Rgba32> image, TextureFormat format,
        out int width, out int height, ref int mips)
    {
        width = image.Width;
        height = image.Height;

        // can't make mipmaps from this image
        if (mips > 1 && (width != height || !IsPo2(width)))
            mips = 1;

        image.Mutate(i => i.Flip(FlipMode.Vertical));

        byte[] encData = TextureEncoderDecoder.Encode(image, width, height, format, 5, mips);
        return encData;
    }

    static bool IsPo2(int n)
    {
        return n > 0 && (n & (n - 1)) == 0;
    }
}