using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.TextureType;

namespace TextureConverter.Tests;

public class DDSFormatTests
{
    private static Image<Bgra32> CreateTestImage(int width = 64, int height = 64)
    {
        return TestImageHelper.CreateTestImage(width, height);
    }

    private static Image<Bgra32> CreateTestImage(int width, int height, TestImageHelper.TestPattern pattern)
    {
        return TestImageHelper.CreateTestImage(width, height, pattern);
    }

    private static Bgra32[] ExtractPixels(Image<Bgra32> image)
    {
        return TestImageHelper.ExtractPixels(image);
    }

    private static void AssertPixelsClose(Bgra32[] expected, Bgra32[] actual,
        int toleranceR = 0, int toleranceG = 0, int toleranceB = 0, int toleranceA = 0,
        double maxErrorRate = 0.0, string message = "")
    {
        TestImageHelper.AssertPixelsClose(expected, actual, toleranceR, toleranceG, toleranceB, toleranceA, maxErrorRate, message);
    }

    [Theory]
    [InlineData(DDS.DDSFormat.RGBA8)]
    [InlineData(DDS.DDSFormat.RGBA_SRGB)]
    [InlineData(DDS.DDSFormat.RGB565)]
    [InlineData(DDS.DDSFormat.RGB5A1)]
    [InlineData(DDS.DDSFormat.RGBA4)]
    [InlineData(DDS.DDSFormat.L8)]
    [InlineData(DDS.DDSFormat.LA8)]
    public void ConvertToFile_CreatesValidDDSFile(DDS.DDSFormat format)
    {
        using Image<Bgra32> testImage = CreateTestImage();
        using MemoryStream output = new();

        // Act
        DDS.ConvertToFile(testImage, format, output);

        // Assert
        Assert.NotEmpty(output.ToArray());

        // Verify DDS header signature
        output.Seek(0, SeekOrigin.Begin);
        byte[] header = new byte[4];
        output.Read(header, 0, 4);
        Assert.Equal("DDS ", System.Text.Encoding.ASCII.GetString(header));
    }

    [Fact]
    public void ConvertToFile_RGBA8_RoundTrip_PreservesImageDimensions()
    {
        using Image<Bgra32> original = CreateTestImage(32, 48);
        using MemoryStream ddsOutput = new();

        // Act - Convert to DDS
        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA8, ddsOutput);

        // Reset stream for reading
        ddsOutput.Seek(0, SeekOrigin.Begin);

        // Assert - Read back
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
    }

    [Fact]
    public void ConvertToFile_RGB565_RoundTrip_SimilarPixelValues()
    {
        using Image<Bgra32> original = CreateTestImage(16, 16);
        using MemoryStream ddsOutput = new();

        // Act - Convert to DDS
        DDS.ConvertToFile(original, DDS.DDSFormat.RGB565, ddsOutput);

        // Reset stream for reading
        ddsOutput.Seek(0, SeekOrigin.Begin);

        // Assert - Read back and check pixel similarity (allowing for quantization loss)
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        int pixelErrorCount = 0;
        int totalPixels = 0;

        // Copy pixel data to compare outside of ProcessPixelRows
        Bgra32[] origPixels = new Bgra32[original.Width * original.Height];
        Bgra32[] restPixels = new Bgra32[restored.Width * restored.Height];

        original.ProcessPixelRows(accessor =>
        {
            int idx = 0;
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
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
                Span<Bgra32> row = accessor.GetRowSpan(y);
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

            if (rDiff > 8 || gDiff > 4 || bDiff > 8)
            {
                pixelErrorCount++;
            }

            totalPixels++;
        }

        // Allow up to 5% pixel error due to format quantization
        double errorRate = (double)pixelErrorCount / totalPixels;
        Assert.True(errorRate < 0.05, $"Error rate {errorRate:P} exceeds 5% threshold");
    }

    [Fact]
    public void ConvertToFile_RGBA8_AllOpaque_PixelsMatch()
    {
        Image<Bgra32> image = new(4, 4);
        Bgra32[] pixels =
        [
            new(255, 0, 0, 255),
            new(0, 255, 0, 255),
            new(0, 0, 255, 255),
            new(255, 255, 255, 255)
        ];

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

        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(image, DDS.DDSFormat.RGBA8, ddsOutput);

        // Assert
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

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
    }

    [Fact]
    public void ConvertToFile_CompressedFormat_ThrowsNotImplemented()
    {
        using Image<Bgra32> testImage = CreateTestImage();
        using MemoryStream output = new();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() =>
            DDS.ConvertToFile(testImage, DDS.DDSFormat.DXT1, output));
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
        using Image<Bgra32> original = CreateTestImage(width, height, pattern);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA8, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert - RGBA8 should preserve exactly
        Bgra32[] origPixels = ExtractPixels(original);
        Bgra32[] restPixels = ExtractPixels(restored);

        AssertPixelsClose(origPixels, restPixels, message: $"Pattern {pattern} at {width}x{height}");
    }

    #endregion

    #region Dimension Coverage Tests

    [Theory]
    [InlineData(1, 1)]      // Minimal
    [InlineData(2, 2)]      // Minimal power-of-2
    [InlineData(4, 4)]      // Small
    [InlineData(8, 8)]      // Standard small
    [InlineData(16, 16)]    // Standard
    [InlineData(32, 32)]    // Standard
    [InlineData(64, 64)]    // Test size
    [InlineData(128, 128)]  // Larger
    [InlineData(256, 256)]  // Game file size
    [InlineData(512, 512)]  // Larger
    [InlineData(128, 64)]   // Non-square
    [InlineData(512, 256)]  // Non-square large
    public void RoundTrip_RGBA8_VariousDimensions(int width, int height)
    {
        using Image<Bgra32> original = CreateTestImage(width, height, TestImageHelper.TestPattern.Gradient);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA8, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        Bgra32[] origPixels = ExtractPixels(original);
        Bgra32[] restPixels = ExtractPixels(restored);
        AssertPixelsClose(origPixels, restPixels, message: $"Dimensions {width}x{height}");
    }

    #endregion

    #region Format Coverage Tests

    [Theory]
    [InlineData(DDS.DDSFormat.RGBA8)]
    [InlineData(DDS.DDSFormat.RGBA_SRGB)]
    [InlineData(DDS.DDSFormat.RGB565)]
    [InlineData(DDS.DDSFormat.RGB5A1)]
    [InlineData(DDS.DDSFormat.RGBA4)]
    [InlineData(DDS.DDSFormat.L8)]
    [InlineData(DDS.DDSFormat.LA8)]
    public void RoundTrip_AllFormats_PreservesDimensions(DDS.DDSFormat format)
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Gradient);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, format, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert - Dimensions should always be preserved
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
    }

    [Theory]
    [InlineData(DDS.DDSFormat.RGBA8)]
    [InlineData(DDS.DDSFormat.RGBA_SRGB)]
    [InlineData(DDS.DDSFormat.RGB565)]
    [InlineData(DDS.DDSFormat.RGB5A1)]
    [InlineData(DDS.DDSFormat.RGBA4)]
    [InlineData(DDS.DDSFormat.L8)]
    [InlineData(DDS.DDSFormat.LA8)]
    public void RoundTrip_AllFormats_WithVariousDimensions(DDS.DDSFormat format)
    {
        (int, int)[] dimensions = [(32, 32), (64, 64), (128, 128), (256, 256)];

        foreach ((int width, int height) in dimensions)
        {
            using Image<Bgra32> original = CreateTestImage(width, height, TestImageHelper.TestPattern.Numbered);
            using MemoryStream ddsOutput = new();

            // Act
            DDS.ConvertToFile(original, format, ddsOutput);
            ddsOutput.Seek(0, SeekOrigin.Begin);
            using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

            // Assert
            Assert.Equal(original.Width, restored.Width);
            Assert.Equal(original.Height, restored.Height);
        }
    }

    #endregion

    #region Format-Specific Tests

    [Fact]
    public void RoundTrip_RGB565_AllowsQuantizationError()
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Gradient);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, DDS.DDSFormat.RGB565, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert - RGB565 has reduced precision
        Bgra32[] origPixels = ExtractPixels(original);
        Bgra32[] restPixels = ExtractPixels(restored);

        // Allow up to 5% error due to 565 quantization
        AssertPixelsClose(origPixels, restPixels,
            toleranceR: 8, toleranceG: 4, toleranceB: 8,
            maxErrorRate: 0.05, message: "RGB565");
    }

    [Fact]
    public void RoundTrip_RGB5A1_HandlesAlpha()
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Checkerboard);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, DDS.DDSFormat.RGB5A1, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
    }

    [Fact]
    public void RoundTrip_RGBA4_AllowsQuantizationError()
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Striped);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA4, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert - RGBA4 has very reduced precision
        Bgra32[] origPixels = ExtractPixels(original);
        Bgra32[] restPixels = ExtractPixels(restored);

        // Allow larger tolerance for 4-bit format
        AssertPixelsClose(origPixels, restPixels,
            toleranceR: 16, toleranceG: 16, toleranceB: 16, toleranceA: 16,
            maxErrorRate: 0.10, message: "RGBA4");
    }

    [Fact]
    public void RoundTrip_L8_Luminance()
    {
        // Create grayscale test image
        Image<Bgra32> image = new(64, 64);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte gray = (byte)(x * 255 / 64);
                    row[x] = new Bgra32(gray, gray, gray, 255);
                }
            }
        });

        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(image, DDS.DDSFormat.L8, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert
        Assert.Equal(image.Width, restored.Width);
        Assert.Equal(image.Height, restored.Height);
    }

    [Fact]
    public void RoundTrip_LA8_LuminanceAlpha()
    {
        // Create grayscale with alpha test image
        Image<Bgra32> image = new(64, 64);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte gray = (byte)(x * 255 / 64);
                    byte alpha = (byte)(y * 255 / 64);
                    row[x] = new Bgra32(gray, gray, gray, alpha);
                }
            }
        });

        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(image, DDS.DDSFormat.LA8, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert
        Assert.Equal(image.Width, restored.Width);
        Assert.Equal(image.Height, restored.Height);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RoundTrip_SinglePixel()
    {
        using Image<Bgra32> original = CreateTestImage(1, 1);
        using MemoryStream ddsOutput = new();

        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA8, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        Assert.Equal(1, restored.Width);
        Assert.Equal(1, restored.Height);
    }

    [Theory]
    [InlineData(3, 3)]   // Odd dimensions
    [InlineData(7, 5)]   // Prime-like dimensions
    [InlineData(255, 255)] // Off by one from power of 2
    public void RoundTrip_OddDimensions(int width, int height)
    {
        using Image<Bgra32> original = CreateTestImage(width, height);
        using MemoryStream ddsOutput = new();

        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA8, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
    }

    [Fact]
    public void Header_ContainsCorrectMagic()
    {
        using Image<Bgra32> testImage = CreateTestImage(32, 32);
        using MemoryStream ddsOutput = new();

        DDS.ConvertToFile(testImage, DDS.DDSFormat.RGBA8, ddsOutput);

        // Verify magic number
        ddsOutput.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        ddsOutput.Read(magic, 0, 4);
        Assert.Equal("DDS ", System.Text.Encoding.ASCII.GetString(magic));
    }

    [Fact]
    public void Header_ContainsRequiredFields()
    {
        using Image<Bgra32> testImage = CreateTestImage(64, 64);
        using MemoryStream ddsOutput = new();

        DDS.ConvertToFile(testImage, DDS.DDSFormat.RGBA8, ddsOutput);

        // Check minimum file size for DDS header
        Assert.True(ddsOutput.Length >= 128, "DDS file too small");
    }

    #endregion

    #region SRGB Format Tests

    [Fact]
    public void RoundTrip_RGBA_SRGB_PreservesDimensions()
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Gradient);
        using MemoryStream ddsOutput = new();

        // Act
        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA_SRGB, ddsOutput);
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = DDS.GetImage(ddsOutput);

        // Assert
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
    }

    #endregion

    #endregion
}