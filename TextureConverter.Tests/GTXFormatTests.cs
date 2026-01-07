using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.Formats;
using TextureConverter.TextureType;

namespace TextureConverter.Tests;

public class GTXFormatTests
{
    /// <summary>
    /// Creates a test image with a gradient pattern for testing.
    /// </summary>
    private static Image<Bgra32> CreateTestImage(int width = 64, int height = 64)
    {
        var image = new Image<Bgra32>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte r = (byte)((x * 255) / width);
                    byte g = (byte)((y * 255) / height);
                    byte b = (byte)(((x + y) * 255) / (width + height));
                    row[x] = new Bgra32(r, g, b, 255);
                }
            }
        });
        return image;
    }

    [Theory]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB)]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R5_G5_B5_A1_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R4_G4_B4_A4_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_G8_UNORM)]
    public void ConvertToFile_CreatesValidGTXFile(GTX.GX2SurfaceFormat format)
    {
        using var testImage = CreateTestImage();
        var output = new MemoryStream();

        // Act
        GTX.ConvertToFile(testImage, format, output);

        // Assert
        Assert.NotEmpty(output.ToArray());
        
        // Verify GTX header signature
        output.Seek(0, SeekOrigin.Begin);
        byte[] header = new byte[4];
        output.Read(header, 0, 4);
        Assert.Equal("Gfx2", System.Text.Encoding.ASCII.GetString(header));
        
        output.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGBA8_RoundTrip_PreservesImageDimensions()
    {
        using var original = CreateTestImage(32, 32);
        var gtxOutput = new MemoryStream();

        // Act - Convert to GTX
        GTX.ConvertToFile(original, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);

        // Reset stream for reading
        gtxOutput.Seek(0, SeekOrigin.Begin);
        
        // Assert - Read back
        using var restored = GTX.GetImage(gtxOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        
        gtxOutput.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGBA8_RoundTrip_PixelsMatch()
    {
        var image = new Image<Bgra32>(4, 4);
        var pixels = new Bgra32[] 
        { 
            new(255, 0, 0, 255),
            new(0, 255, 0, 255),
            new(0, 0, 255, 255),
            new(255, 255, 255, 255)
        };

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length && x < 4; x++)
                {
                    row[x] = pixels[x];
                }
            }
        });

        var gtxOutput = new MemoryStream();

        // Act
        GTX.ConvertToFile(image, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);

        // Assert
        gtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = GTX.GetImage(gtxOutput);

        restored.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < Math.Min(accessor.Height, 1); y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < Math.Min(row.Length, 4); x++)
                {
                    Assert.Equal(pixels[x].R, row[x].R);
                    Assert.Equal(pixels[x].G, row[x].G);
                    Assert.Equal(pixels[x].B, row[x].B);
                    Assert.Equal(pixels[x].A, row[x].A);
                }
            }
        });
        
        gtxOutput.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGB565_RoundTrip_SimilarPixelValues()
    {
        using var original = CreateTestImage(16, 16);
        var gtxOutput = new MemoryStream();

        // Act - Convert to GTX
        GTX.ConvertToFile(original, GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM, gtxOutput);

        // Reset stream for reading
        gtxOutput.Seek(0, SeekOrigin.Begin);
        
        // Assert - Read back and check pixel similarity (allowing for quantization loss)
        using var restored = GTX.GetImage(gtxOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        int pixelErrorCount = 0;
        int totalPixels = 0;

        // Copy pixels outside the delegates to avoid ref-like type issues
        var origPixels = new Bgra32[original.Width * original.Height];
        var restPixels = new Bgra32[restored.Width * restored.Height];

        original.ProcessPixelRows(accessor =>
        {
            int idx = 0;
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    origPixels[idx++] = row[x];
                }
            }
        });

        restored.ProcessPixelRows(accessor =>
        {
            int idx = 0;
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    restPixels[idx++] = row[x];
                }
            }
        });

        // Now compare outside the delegates
        for (int i = 0; i < origPixels.Length && i < restPixels.Length; i++)
        {
            Bgra32 origPixel = origPixels[i];
            Bgra32 restPixel = restPixels[i];

            // RGB565 has reduced precision; allow for quantization error
            int rDiff = Math.Abs(origPixel.R - restPixel.R);
            int gDiff = Math.Abs(origPixel.G - restPixel.G);
            int bDiff = Math.Abs(origPixel.B - restPixel.B);

            if (rDiff > 16 || gDiff > 8 || bDiff > 16)
            {
                pixelErrorCount++;
            }

            totalPixels++;
        }

        // Allow up to 10% pixel error for format conversion
        double errorRate = (double)pixelErrorCount / totalPixels;
        Assert.True(errorRate < 0.10, $"Too many pixel errors: {errorRate:P2}");
        
        gtxOutput.Dispose();
    }

    [Fact]
    public void GTX_LoadFile_ParsesHeaderCorrectly()
    {
        using var testImage = CreateTestImage(32, 32);
        var gtxOutput = new MemoryStream();

        // Create a GTX file
        GTX.ConvertToFile(testImage, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);

        // Load and verify
        var gtx = new GTX();
        gtx.LoadFile(gtxOutput);

        Assert.Equal(7U, gtx.MajorVersion);
        Assert.True(gtx.Textures.Count > 0);
        Assert.Equal(32U, gtx.Textures[0].Width);
        Assert.Equal(32U, gtx.Textures[0].Height);
        Assert.Equal(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtx.Textures[0].Format);

        gtxOutput.Dispose();
    }

    [Fact]
    public void GtxDecoder_Identify_ReturnsCorrectImageInfo()
    {
        using var testImage = CreateTestImage(64, 48);
        var gtxOutput = new MemoryStream();

        GTX.ConvertToFile(testImage, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);

        var decoder = new GtxDecoder();
        var options = new SixLabors.ImageSharp.Formats.DecoderOptions();
        var info = decoder.Identify(options, gtxOutput);

        Assert.Equal(64, info.Size.Width);
        Assert.Equal(48, info.Size.Height);

        gtxOutput.Dispose();
    }

    [Fact]
    public void GtxFormat_HasCorrectProperties()
    {
        var format = GtxFormat.Instance;

        Assert.Equal("GTX", format.Name);
        Assert.Equal("image/gtx", format.DefaultMimeType);
        Assert.Contains(".gtx", format.FileExtensions);
        Assert.Contains("image/gtx", format.MimeTypes);
    }

    [Theory]
    [InlineData(GTX.GX2SurfaceFormat.T_BC1_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.T_BC2_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.T_BC3_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.T_BC4_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.T_BC5_UNORM)]
    public void IsFormatBCN_ReturnsTrueForBCNFormats(GTX.GX2SurfaceFormat format)
    {
        Assert.True(GTX.IsFormatBCN(format));
    }

    [Theory]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_UNORM)]
    public void IsFormatBCN_ReturnsFalseForNonBCNFormats(GTX.GX2SurfaceFormat format)
    {
        Assert.False(GTX.IsFormatBCN(format));
    }

    [Theory]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, 4)]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM, 2)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_UNORM, 1)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_G8_UNORM, 2)]
    [InlineData(GTX.GX2SurfaceFormat.T_BC1_UNORM, 8)]
    [InlineData(GTX.GX2SurfaceFormat.T_BC3_UNORM, 16)]
    public void GetBPP_ReturnsCorrectValue(GTX.GX2SurfaceFormat format, int expectedBpp)
    {
        Assert.Equal(expectedBpp, GTX.GetBPP(format));
    }

    [Fact]
    public void ConvertGX2ToDDSFormat_ConvertsCorrectly()
    {
        Assert.Equal(TextureType.DDS.DDSFormat.RGBA8, GTX.ConvertGX2ToDDSFormat(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM));
        Assert.Equal(TextureType.DDS.DDSFormat.BC1, GTX.ConvertGX2ToDDSFormat(GTX.GX2SurfaceFormat.T_BC1_UNORM));
        Assert.Equal(TextureType.DDS.DDSFormat.BC3, GTX.ConvertGX2ToDDSFormat(GTX.GX2SurfaceFormat.T_BC3_UNORM));
        Assert.Equal(TextureType.DDS.DDSFormat.RGB565, GTX.ConvertGX2ToDDSFormat(GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM));
    }

    [Fact]
    public void ConvertDDSToGX2Format_ConvertsCorrectly()
    {
        Assert.Equal(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, GTX.ConvertDDSToGX2Format(TextureType.DDS.DDSFormat.RGBA8));
        Assert.Equal(GTX.GX2SurfaceFormat.T_BC1_UNORM, GTX.ConvertDDSToGX2Format(TextureType.DDS.DDSFormat.BC1));
        Assert.Equal(GTX.GX2SurfaceFormat.T_BC3_UNORM, GTX.ConvertDDSToGX2Format(TextureType.DDS.DDSFormat.BC3));
        Assert.Equal(GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM, GTX.ConvertDDSToGX2Format(TextureType.DDS.DDSFormat.RGB565));
    }
}
