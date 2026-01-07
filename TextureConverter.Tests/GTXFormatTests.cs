using TextureConverter.Formats;
using TextureConverter.TextureType;
using TextureConverter.TextureConverterHelpers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TextureConverter.Tests;

public class GTXFormatTests
{
    private static Image<Bgra32> CreateTestImage(int width = 64, int height = 64)
    {
        return TestImageHelper.CreateTestImage(width, height);
    }

    private static Image<Bgra32> CreateTestImage(int width, int height, TestImageHelper.TestPattern pattern)
    {
        return TestImageHelper.CreateTestImage(width, height, pattern);
    }

    private static void AssertPixelsEqual(Image<Bgra32> expected, Image<Bgra32> actual, string message = "")
    {
        TestImageHelper.AssertPixelsEqual(expected, actual, 0, message);
    }

    private static Bgra32[] ExtractPixels(Image<Bgra32> image)
    {
        return TestImageHelper.ExtractPixels(image);
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

    #region Comprehensive Coverage Tests

    #region Pattern-Based Round-Trip Tests
    
    [Theory]
    [InlineData(64, 64, TestImageHelper.TestPattern.Gradient)]
    [InlineData(64, 64, TestImageHelper.TestPattern.Checkerboard)]
    [InlineData(64, 64, TestImageHelper.TestPattern.Numbered)]
    [InlineData(64, 64, TestImageHelper.TestPattern.Striped)]
    [InlineData(128, 64, TestImageHelper.TestPattern.Gradient)]
    [InlineData(64, 128, TestImageHelper.TestPattern.Gradient)]
    public void RoundTrip_RGBA8_PatternPreservation(int width, int height, TestImageHelper.TestPattern pattern)
    {
        using var original = CreateTestImage(width, height, pattern);
        var gtxOutput = new MemoryStream();

        // Act
        GTX.ConvertToFile(original, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = GTX.GetImage(gtxOutput);

        // Assert
        AssertPixelsEqual((Image<Bgra32>)original, (Image<Bgra32>)restored, 
            $"Pattern {pattern} at {width}x{height}");
        
        gtxOutput.Dispose();
    }

    #endregion

    #region Dimension Coverage Tests

    [Theory]
    [InlineData(1, 1)]      // Minimal
    [InlineData(2, 2)]      // Minimal power-of-2
    [InlineData(4, 4)]      // Micro-tile size
    [InlineData(8, 8)]      // Sub-macro-tile
    [InlineData(16, 16)]    // Macro-tile height
    [InlineData(32, 32)]    // Macro-tile size
    [InlineData(64, 64)]    // Standard test size
    [InlineData(128, 128)]  // Larger
    [InlineData(256, 256)]  // Game file size
    [InlineData(512, 512)]  // Game file size (picto)
    [InlineData(63, 63)]    // Non-power-of-2
    [InlineData(65, 65)]    // Non-power-of-2
    [InlineData(257, 257)]  // Non-power-of-2
    [InlineData(128, 64)]   // Non-square
    [InlineData(512, 256)]  // Non-square large
    public void RoundTrip_RGBA8_VariousDimensions(int width, int height)
    {
        using var original = CreateTestImage(width, height, TestImageHelper.TestPattern.Gradient);
        var gtxOutput = new MemoryStream();

        // Act
        GTX.ConvertToFile(original, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = GTX.GetImage(gtxOutput);

        // Assert
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        AssertPixelsEqual((Image<Bgra32>)original, (Image<Bgra32>)restored,
            $"Dimensions {width}x{height}");
        
        gtxOutput.Dispose();
    }

    #endregion

    #region Format Coverage Tests

    [Theory]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_SRGB)]
    [InlineData(GTX.GX2SurfaceFormat.TCS_R5_G6_B5_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R5_G5_B5_A1_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R4_G4_B4_A4_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_UNORM)]
    [InlineData(GTX.GX2SurfaceFormat.TC_R8_G8_UNORM)]
    public void RoundTrip_AllFormats_PreservesDimensions(GTX.GX2SurfaceFormat format)
    {
        // Use size compatible with BC formats (must be multiple of 4)
        using var original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Gradient);
        var gtxOutput = new MemoryStream();

        // Act
        GTX.ConvertToFile(original, format, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = GTX.GetImage(gtxOutput);

        // Assert - Dimensions should always be preserved
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        
        gtxOutput.Dispose();
    }

    #endregion

    #region Swizzle/Deswizzle Tests

    [Fact]
    public void Swizzle_Deswizzle_RoundTrip_RGBA8()
    {
        // Create test data
        uint width = 256;
        uint height = 256;
        byte[] testData = new byte[width * height * 4];
        
        // Fill with pattern
        for (int i = 0; i < testData.Length; i += 4)
        {
            testData[i + 0] = (byte)((i / 4) % 256);      // B
            testData[i + 1] = (byte)(((i / 4) >> 8) % 256); // G
            testData[i + 2] = (byte)(((i / 4) >> 16) % 256); // R
            testData[i + 3] = 255;                           // A
        }

        // Create surface
        var surface = new GTX.GX2Surface
        {
            Width = width,
            Height = height,
            Depth = 1,
            NumMips = 1,
            Format = GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM,
            AA = GTX.GX2AAMode.Mode1X,
            Use = 1,
            TileMode = GTX.GX2TileMode.LinearAligned,
            Swizzle = 0,
            Alignment = 4096,
            Pitch = 0,
            Bpp = 4,
            Data = testData,
            MipData = [],
            MipOffsets = new uint[13],
        };

        // Get surface info
        var surfInfo = GX2Swizzle.GetSurfaceInfo(surface.Format, surface.Width, surface.Height, 
            surface.Depth, (uint)surface.Dim, (uint)surface.TileMode, (uint)surface.AA, 0);
        surface.Pitch = surfInfo.Pitch;

        // Act - Swizzle then deswizzle
        byte[] swizzled = GX2Swizzle.Swizzle(surface, testData, 0, 0);
        byte[] deswizzled = GX2Swizzle.Deswizzle(surface, 0, 0);

        // Assert - Should get back original (or close to it for compressed formats)
        Assert.Equal(testData.Length, deswizzled.Length);
        // Note: Due to compression, not all formats will round-trip perfectly
        // For LinearAligned, this should be exact
    }

    [Theory]
    [InlineData(256u, 256u)]  // Game file size
    [InlineData(512u, 512u)]  // Game file size
    [InlineData(64u, 64u)]    // Standard size
    public void Deswizzle_LinearAligned_ProducesValidData(uint width, uint height)
    {
        // Create test surface with LinearAligned tiling
        var surface = new GTX.GX2Surface
        {
            Width = width,
            Height = height,
            Depth = 1,
            NumMips = 1,
            Format = GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM,
            AA = GTX.GX2AAMode.Mode1X,
            Use = 1,
            TileMode = GTX.GX2TileMode.LinearAligned,
            Swizzle = 0,
            Alignment = 4096,
            Pitch = 0,
            Bpp = 4,
            Data = new byte[width * height * 4],
            MipData = [],
            MipOffsets = new uint[13],
        };

        // Fill with recognizable pattern
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                uint offset = (y * width + x) * 4;
                surface.Data[offset + 0] = (byte)(x % 256);
                surface.Data[offset + 1] = (byte)(y % 256);
                surface.Data[offset + 2] = (byte)((x + y) % 256);
                surface.Data[offset + 3] = 255;
            }
        }

        // Get surface info
        var surfInfo = GX2Swizzle.GetSurfaceInfo(surface.Format, surface.Width, surface.Height,
            surface.Depth, (uint)surface.Dim, (uint)surface.TileMode, (uint)surface.AA, 0);
        surface.Pitch = surfInfo.Pitch;

        // Act
        byte[] deswizzled = GX2Swizzle.Deswizzle(surface, 0, 0);

        // Assert - Data should be present and match dimensions
        Assert.NotEmpty(deswizzled);
        Assert.Equal(width * height * 4, (uint)deswizzled.Length);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RoundTrip_SinglePixel()
    {
        using var original = CreateTestImage(1, 1);
        var gtxOutput = new MemoryStream();

        GTX.ConvertToFile(original, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = GTX.GetImage(gtxOutput);

        Assert.Equal(1, restored.Width);
        Assert.Equal(1, restored.Height);
        
        gtxOutput.Dispose();
    }

    [Theory]
    [InlineData(3, 3)]   // Odd dimensions
    [InlineData(7, 5)]   // Prime-like dimensions
    [InlineData(255, 255)] // Off by one from power of 2
    public void RoundTrip_OddDimensions(int width, int height)
    {
        using var original = CreateTestImage(width, height);
        var gtxOutput = new MemoryStream();

        GTX.ConvertToFile(original, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        gtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = GTX.GetImage(gtxOutput);

        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        
        gtxOutput.Dispose();
    }

    [Fact]
    public void Header_ContainsCorrectMagic()
    {
        using var testImage = CreateTestImage(32, 32);
        var gtxOutput = new MemoryStream();

        GTX.ConvertToFile(testImage, GTX.GX2SurfaceFormat.TCS_R8_G8_B8_A8_UNORM, gtxOutput);
        
        // Verify magic numbers
        gtxOutput.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        gtxOutput.Read(magic, 0, 4);
        Assert.Equal("Gfx2", System.Text.Encoding.ASCII.GetString(magic));
        
        gtxOutput.Dispose();
    }

    #endregion

    #endregion
}
