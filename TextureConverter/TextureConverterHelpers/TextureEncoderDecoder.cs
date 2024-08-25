using AssetsTools.NET.Texture;

using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TextureConverter.TextureConverterHelpers;

public class TextureEncoderDecoder
{
    public static byte[] Encode(SixLabors.ImageSharp.Image<Rgba32> image, int width, int height, TextureFormat format, int quality = 5, int mips = 1)
    {
        if (format is not TextureFormat.DXT1Crunched and not TextureFormat.DXT5Crunched)
            // Wrong format, throw exception
            throw new Exception($"Unsupported format: {format}");

        using MemoryStream rawDataStream = new();

        byte[] rawRgbaData = new byte[width * height * 4];
        image.CopyPixelDataTo(rawRgbaData);
        byte[] rawEncodedData = EncodeCrunch(rawRgbaData, width, height, format, quality, mips);
        rawDataStream.Write(rawEncodedData);

        return rawDataStream.ToArray();
    }

    private static byte[] EncodeCrunch(byte[] data, int width, int height, TextureFormat format, int quality, int mips)
    {
        if (format is not TextureFormat.DXT1Crunched and not TextureFormat.DXT5Crunched)
            // Wrong format, throw exception
            throw new Exception($"Unsupported format: {format}");

        byte[] dest = [];
        uint size = 0;
        unsafe
        {
            int checkoutId = -1;
            fixed (byte* dataPtr = data)
            {
                // we don't know the size of the output yet
                // write it to unmanaged memory first and copy to managed after we have the size
                // ////////////
                // setting ver to 1 fixes "The texture could not be loaded because it has been
                // encoded with an older version of Crunch" not sure if this breaks older games though
                // todo: determine version ranges
                nint dataIntPtr = (nint)dataPtr;
                size = PInvoke.EncodeByCrunchUnity(dataIntPtr, ref checkoutId, (int)format, quality, (uint)width, (uint)height, 1, mips);
                if (size == 0)
                    return [];
            }

            dest = new byte[size];

            fixed (byte* destPtr = dest)
            {
                nint destIntPtr = (nint)destPtr;
                if (!PInvoke.PickUpAndFree(destIntPtr, size, checkoutId))
                    return [];
            }
        }

        if (size > 0)
        {
            byte[] resizedDest = new byte[size];
            Buffer.BlockCopy(dest, 0, resizedDest, 0, (int)size);
            return resizedDest;
        }
        else
        {
            return [];
        }
    }
}
