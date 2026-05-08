using CrnLib;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Runtime.InteropServices;

using TextureConverter.TextureConverterHelpers;

namespace TextureConverter.Tests;

public class CrnLibTests
{
    [Theory]
    [InlineData(CrnFormat.Dxt1, TextureFormat.DXT1Crunched, 8)]
    [InlineData(CrnFormat.Dxt5, TextureFormat.DXT5Crunched, 16)]
    public void ManagedCrunch_EncodesAndDecodesTopLevelBlocks(CrnFormat crnFormat, TextureFormat textureFormat, int bytesPerBlock)
    {
        const int width = 13;
        const int height = 9;
        byte[] rgba = CreateRgbaGradient(width, height);

        byte[] crn = CrunchTexture.EncodeUnityCrunch(rgba, width, height, crnFormat, mipCount: 2);
        CrnTextureInfo info = CrunchTexture.GetTextureInfo(crn);
        byte[] blocks = CrunchTexture.DecodeUnityCrunch(crn);

        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(2, info.Levels);
        Assert.Equal(crnFormat, info.Format);
        Assert.Equal(ExpectedBlockBytes(width, height, bytesPerBlock), blocks.Length);

        using Image<Rgba32> decoded = TextureDecoder.Decode(crn, textureFormat, width, height);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Theory]
    [InlineData(CrnFormat.Dxt1)]
    [InlineData(CrnFormat.Dxt5)]
    public void ManagedCrunch_ImageApiEncodesAndDecodesImages(CrnFormat crnFormat)
    {
        const int width = 17;
        const int height = 10;
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(CreateRgbaGradient(width, height), width, height);

        byte[] crn = CrunchTexture.Encode(image, crnFormat, new CrnEncodeOptions
        {
            MipCount = 2,
            Quality = CrnCompressionQuality.BestQuality,
        });

        using Image<Rgba32> decoded = CrunchTexture.Decode(crn);
        using Image<Rgba32> decodedMip = CrunchTexture.DecodeUnityCrunchImage(crn, level: 1);

        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.Equal(8, decodedMip.Width);
        Assert.Equal(5, decodedMip.Height);
    }

    [Theory]
    [InlineData(CrnFormat.Dxt1, 8)]
    [InlineData(CrnFormat.Dxt5, 16)]
    public void ManagedCrunch_DecodesRequestedMipLevel(CrnFormat crnFormat, int bytesPerBlock)
    {
        const int width = 16;
        const int height = 16;
        byte[] rgba = CreateRgbaGradient(width, height);

        byte[] crn = CrunchTexture.EncodeUnityCrunch(rgba, width, height, crnFormat, mipCount: 3);
        byte[] mip = CrunchTexture.DecodeUnityCrunch(crn, level: 2);

        Assert.Equal(ExpectedBlockBytes(4, 4, bytesPerBlock), mip.Length);
    }

    [Theory]
    [InlineData(TextureFormat.DXT1Crunched, 8)]
    [InlineData(TextureFormat.DXT5Crunched, 16)]
    public void TextureEncoderDecoder_UsesManagedCrunch(TextureFormat textureFormat, int bytesPerBlock)
    {
        const int width = 8;
        const int height = 8;
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(CreateRgbaGradient(width, height), width, height);

        byte[] crn = TextureEncoderDecoder.Encode(image, width, height, textureFormat, mips: 1);
        byte[] blocks = TextureEncoderDecoder.DecodeCrunch(crn);
        using Image<Rgba32> decoded = TextureDecoder.Decode(crn, textureFormat, width, height);
        CrnTextureInfo info = CrunchTexture.GetTextureInfo(crn);

        Assert.Equal(ToCrnFormat(textureFormat), info.Format);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
        Assert.Equal(1, info.Levels);
        Assert.Equal(1U, info.UserData0);
        Assert.Equal(ExpectedBlockBytes(width, height, bytesPerBlock), blocks.Length);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Theory]
    [InlineData(TextureFormat.DXT1Crunched)]
    [InlineData(TextureFormat.DXT5Crunched)]
    public void TextureImportExport_ImportsCrunchedTextureThroughManagedCrunch(TextureFormat textureFormat)
    {
        const int sourceSize = 16;
        int mips = 3;
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(CreateRgbaGradient(sourceSize, sourceSize), sourceSize, sourceSize);

        byte[] crn = TextureImportExport.Import(image, textureFormat, out int width, out int height, ref mips);
        CrnTextureInfo info = CrunchTexture.GetTextureInfo(crn);
        using Image<Rgba32> decoded = TextureDecoder.Decode(crn, textureFormat, width, height);

        Assert.Equal(sourceSize, width);
        Assert.Equal(sourceSize, height);
        Assert.Equal(3, mips);
        Assert.Equal(3, info.Levels);
        Assert.Equal(ToCrnFormat(textureFormat), info.Format);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Theory]
    [InlineData(TextureFormat.DXT1Crunched)]
    [InlineData(TextureFormat.DXT5Crunched)]
    public void TextureImportExport_ClampsCrunchMipsForNonSquareOrNonPowerOfTwoImages(TextureFormat textureFormat)
    {
        const int width = 13;
        const int height = 9;
        int mips = 4;
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(CreateRgbaGradient(width, height), width, height);

        byte[] crn = TextureImportExport.Import(image, textureFormat, out int importedWidth, out int importedHeight, ref mips);
        CrnTextureInfo info = CrunchTexture.GetTextureInfo(crn);

        Assert.Equal(width, importedWidth);
        Assert.Equal(height, importedHeight);
        Assert.Equal(1, mips);
        Assert.Equal(1, info.Levels);
    }

    [Theory]
    [InlineData(TextureFormat.DXT1Crunched, 8)]
    [InlineData(TextureFormat.DXT5Crunched, 16)]
    public void NativeCrunchPayload_DecodesWithManagedCrunch_WhenNativeLibraryIsAvailable(TextureFormat textureFormat, int bytesPerBlock)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string nativePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TextureConverter",
            "DLLs",
            "TexToolWrap.dll"));

        if (!File.Exists(nativePath))
            return;

        IntPtr library = IntPtr.Zero;
        IntPtr nativeOutput = IntPtr.Zero;
        GCHandle handle = default;
        try
        {
            library = NativeLibrary.Load(nativePath);
            IntPtr export = NativeLibrary.GetExport(library, "EncodeByCrunchUnity");
            NativeEncode encode = Marshal.GetDelegateForFunctionPointer<NativeEncode>(export);

            const int width = 8;
            const int height = 8;
            byte[] rgba = CreateRgbaGradient(width, height);
            handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
            nativeOutput = encode(out uint size, handle.AddrOfPinnedObject(), (int)textureFormat, 5, width, height, 1, 1);

            Assert.NotEqual(IntPtr.Zero, nativeOutput);
            Assert.True(size > 0);

            byte[] nativeCrn = new byte[size];
            Marshal.Copy(nativeOutput, nativeCrn, 0, (int)size);

            byte[] blocks = CrunchTexture.DecodeUnityCrunch(nativeCrn);
            using Image<Rgba32> decoded = TextureDecoder.Decode(nativeCrn, textureFormat, width, height);
            Assert.Equal(ExpectedBlockBytes(width, height, bytesPerBlock), blocks.Length);
            Assert.Equal(width, decoded.Width);
            Assert.Equal(height, decoded.Height);
        }
        catch (DllNotFoundException)
        {
            return;
        }
        catch (EntryPointNotFoundException)
        {
            return;
        }
        catch (BadImageFormatException)
        {
            return;
        }
        finally
        {
            if (nativeOutput != IntPtr.Zero)
                Marshal.FreeCoTaskMem(nativeOutput);

            if (handle.IsAllocated)
                handle.Free();

            if (library != IntPtr.Zero)
                NativeLibrary.Free(library);
        }
    }

    [Theory]
    [InlineData(CrnFormat.Dxt1, 8)]
    [InlineData(CrnFormat.Dxt5, 16)]
    public void ManagedCrunchPayload_DecodesWithNativeCrunch_WhenNativeLibraryIsAvailable(CrnFormat crnFormat, int bytesPerBlock)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string nativePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TextureConverter",
            "DLLs",
            "TexToolWrap.dll"));

        if (!File.Exists(nativePath))
            return;

        IntPtr library = IntPtr.Zero;
        IntPtr nativeOutput = IntPtr.Zero;
        GCHandle handle = default;
        try
        {
            library = NativeLibrary.Load(nativePath);
            IntPtr export = NativeLibrary.GetExport(library, "DecodeByCrunchUnity");
            NativeDecode decode = Marshal.GetDelegateForFunctionPointer<NativeDecode>(export);

            const int width = 8;
            const int height = 8;
            TextureFormat textureFormat = crnFormat == CrnFormat.Dxt1 ? TextureFormat.DXT1Crunched : TextureFormat.DXT5Crunched;
            using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(CreateRgbaGradient(width, height), width, height);
            byte[] managedCrn = TextureEncoderDecoder.Encode(image, width, height, textureFormat);
            handle = GCHandle.Alloc(managedCrn, GCHandleType.Pinned);
            nativeOutput = decode(out uint size, handle.AddrOfPinnedObject(), (uint)managedCrn.Length);

            Assert.NotEqual(IntPtr.Zero, nativeOutput);
            Assert.Equal(ExpectedBlockBytes(width, height, bytesPerBlock), (int)size);
        }
        catch (DllNotFoundException)
        {
            return;
        }
        catch (EntryPointNotFoundException)
        {
            return;
        }
        catch (BadImageFormatException)
        {
            return;
        }
        finally
        {
            if (nativeOutput != IntPtr.Zero)
                Marshal.FreeCoTaskMem(nativeOutput);

            if (handle.IsAllocated)
                handle.Free();

            if (library != IntPtr.Zero)
                NativeLibrary.Free(library);
        }
    }

    private static int ExpectedBlockBytes(int width, int height, int bytesPerBlock)
    {
        return ((width + 3) >> 2) * ((height + 3) >> 2) * bytesPerBlock;
    }

    private static CrnFormat ToCrnFormat(TextureFormat textureFormat)
    {
        return textureFormat switch
        {
            TextureFormat.DXT1Crunched => CrnFormat.Dxt1,
            TextureFormat.DXT5Crunched => CrnFormat.Dxt5,
            _ => throw new ArgumentOutOfRangeException(nameof(textureFormat)),
        };
    }

    private static byte[] CreateRgbaGradient(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * 4;
                rgba[offset] = (byte)(x * 255 / Math.Max(1, width - 1));
                rgba[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                rgba[offset + 2] = (byte)((x + y) * 255 / Math.Max(1, width + height - 2));
                rgba[offset + 3] = (byte)(64 + (x * 191 / Math.Max(1, width - 1)));
            }
        }

        return rgba;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NativeEncode(out uint returnLength, IntPtr data, int mode, int level, uint width, uint height, uint ver, int mips);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr NativeDecode(out uint returnLength, IntPtr data, uint byteSize);
}
