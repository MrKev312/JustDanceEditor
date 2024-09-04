using SixLabors.ImageSharp.PixelFormats;

using System.Runtime.InteropServices;

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
            throw new Exception($"Unsupported format: {format}");

        byte[] dest;

        // Pin the byte array to avoid GC moving it
        GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

        try
        {
            IntPtr dataInPtr = dataHandle.AddrOfPinnedObject(); // Get the pointer to the pinned array

            // Call the PInvoke method
            IntPtr dataOutPtr = PInvoke.EncodeByCrunchUnitySafe(out uint size, dataInPtr, (int)format, quality, (uint)width, (uint)height, 1, mips);

            dest = new byte[size];
            Marshal.Copy(dataOutPtr, dest, 0, (int)size); // Copy the unmanaged memory to the managed array

            // Free the unmanaged memory
            Marshal.FreeCoTaskMem(dataOutPtr);
        }
        finally
        {
            // Always free the handle to avoid memory leaks
            if (dataHandle.IsAllocated)
                dataHandle.Free();
        }

        return dest;
    }
}
