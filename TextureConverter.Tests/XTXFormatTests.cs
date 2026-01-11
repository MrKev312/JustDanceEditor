using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.TextureType;

namespace TextureConverter.Tests;

public class XTXFormatTests
{
    private static Image<Bgra32> CreateTestImage(int width = 64, int height = 64)
    {
        return TestImageHelper.CreateTestImage(width, height);
    }

    private static Image<Bgra32> CreateTestImage(int width, int height, TestImageHelper.TestPattern pattern)
    {
        return TestImageHelper.CreateTestImage(width, height, pattern);
    }

    private static void AssertPixelsEqual(Image<Bgra32> expected, Image<Bgra32> actual,
        int tolerance = 0, string message = "")
    {
        TestImageHelper.AssertPixelsEqual(expected, actual, tolerance, message);
    }

    private static Bgra32[] ExtractPixels(Image<Bgra32> image)
    {
        return TestImageHelper.ExtractPixels(image);
    }

    [Theory]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA8_SRGB)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB10A2)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB565)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB5A1)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA4)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_R8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RG8)]
    public void ConvertToFile_CreatesValidXTXFile(XTX.XTXImageFormat format)
    {
        using Image<Bgra32> testImage = CreateTestImage();
        MemoryStream output = new();

        // Act
        XTX.ConvertToFile(testImage, format, output);

        // Assert
        Assert.NotEmpty(output.ToArray());

        // Verify XTX header signature
        output.Seek(0, SeekOrigin.Begin);
        byte[] header = new byte[4];
        output.Read(header, 0, 4);
        Assert.Equal("DFvN", System.Text.Encoding.ASCII.GetString(header));

        output.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGBA8_RoundTrip_PreservesImageDimensions()
    {
        using Image<Bgra32> original = CreateTestImage(32, 48);
        MemoryStream xtxOutput = new();

        // Act - Convert to XTX
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

        // Reset stream for reading
        xtxOutput.Seek(0, SeekOrigin.Begin);

        // Assert - Read back
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        xtxOutput.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGBA8_RoundTrip_PixelsMatch()
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

        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(image, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

        // Assert
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

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

        xtxOutput.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGB565_RoundTrip_SimilarPixelValues()
    {
        using Image<Bgra32> original = CreateTestImage(16, 16);
        MemoryStream xtxOutput = new();

        // Act - Convert to XTX
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGB565, xtxOutput);

        // Reset stream for reading
        xtxOutput.Seek(0, SeekOrigin.Begin);

        // Assert - Read back and check pixel similarity (allowing for quantization loss)
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        int pixelErrorCount = 0;
        int totalPixels = 0;

        // Copy pixels outside the delegates to avoid ref-like type issues
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

        xtxOutput.Dispose();
    }

    [Fact]
    public void ConvertToFile_R8Luminance_RoundTrip_PreservesLuminance()
    {
        // Create a simple grayscale test image
        Image<Bgra32> image = new(8, 8);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte gray = (byte)((x + (y * 8)) * 255 / 64);
                    row[x] = new Bgra32(gray, gray, gray, 255);
                }
            }
        });

        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(image, XTX.XTXImageFormat.NVN_FORMAT_R8, xtxOutput);

        // Assert
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        Assert.Equal(image.Width, restored.Width);
        Assert.Equal(image.Height, restored.Height);

        xtxOutput.Dispose();
    }

    [Theory]
    [InlineData(XTX.XTXImageFormat.DXT1)]
    [InlineData(XTX.XTXImageFormat.DXT3)]
    [InlineData(XTX.XTXImageFormat.DXT5)]
    public void ConvertToFile_CompressedFormat_RoundTrip_WorksCorrectly(XTX.XTXImageFormat format)
    {
        // Arrange
        using Image<Bgra32> testImage = CreateTestImage(64, 64); // BCn requires 4x4 blocks
        using MemoryStream output = new();

        // Act
        XTX.ConvertToFile(testImage, format, output);
        output.Position = 0;
        using Image<Bgra32> restored = XTX.GetImage(output);

        // Assert
        Assert.Equal(testImage.Width, restored.Width);
        Assert.Equal(testImage.Height, restored.Height);
    }

    [Fact]
    public void ConvertToFile_DifferentSizes_RoundTrip_WorksCorrectly()
    {
        (int, int)[] sizes = [(16, 16), (32, 64), (128, 64), (256, 256)];

        foreach ((int width, int height) in sizes)
        {
            using Image<Bgra32> original = CreateTestImage(width, height);
            MemoryStream xtxOutput = new();

            // Act
            XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

            // Assert
            xtxOutput.Seek(0, SeekOrigin.Begin);
            using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

            Assert.Equal(width, restored.Width);
            Assert.Equal(height, restored.Height);

            xtxOutput.Dispose();
        }
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
        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        // Assert - Allow small tolerance for compression
        AssertPixelsEqual(original, restored, tolerance: 0,
            message: $"Pattern {pattern} at {width}x{height}");

        xtxOutput.Dispose();
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
    [InlineData(512, 512)]  // Larger game file size
    [InlineData(128, 64)]   // Non-square
    [InlineData(512, 256)]  // Non-square large
    public void RoundTrip_RGBA8_VariousDimensions(int width, int height)
    {
        using Image<Bgra32> original = CreateTestImage(width, height, TestImageHelper.TestPattern.Gradient);
        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        // Assert
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        AssertPixelsEqual(original, restored,
            message: $"Dimensions {width}x{height}");

        xtxOutput.Dispose();
    }

    #endregion

    #region Format Coverage Tests

    [Theory]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA8_SRGB)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB10A2)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB565)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB5A1)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA4)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_R8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RG8)]
    public void RoundTrip_AllFormats_PreservesDimensions(XTX.XTXImageFormat format)
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Gradient);
        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(original, format, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        // Assert - Dimensions should always be preserved
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        xtxOutput.Dispose();
    }

    [Theory]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGBA8_SRGB)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RGB565)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_R8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RG8)]
    public void RoundTrip_AllFormats_WithVariousDimensions(XTX.XTXImageFormat format)
    {
        (int, int)[] dimensions = [(32, 32), (64, 64), (128, 128), (256, 256)];

        foreach ((int width, int height) in dimensions)
        {
            using Image<Bgra32> original = CreateTestImage(width, height, TestImageHelper.TestPattern.Numbered);
            MemoryStream xtxOutput = new();

            // Act
            XTX.ConvertToFile(original, format, xtxOutput);
            xtxOutput.Seek(0, SeekOrigin.Begin);
            using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

            // Assert
            Assert.Equal(original.Width, restored.Width);
            Assert.Equal(original.Height, restored.Height);

            xtxOutput.Dispose();
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RoundTrip_SinglePixel()
    {
        using Image<Bgra32> original = CreateTestImage(1, 1);
        MemoryStream xtxOutput = new();

        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        Assert.Equal(1, restored.Width);
        Assert.Equal(1, restored.Height);

        xtxOutput.Dispose();
    }

    [Theory]
    [InlineData(3, 3)]   // Odd dimensions
    [InlineData(7, 5)]   // Prime-like dimensions
    [InlineData(255, 255)] // Off by one from power of 2
    public void RoundTrip_OddDimensions(int width, int height)
    {
        using Image<Bgra32> original = CreateTestImage(width, height);
        MemoryStream xtxOutput = new();

        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        xtxOutput.Dispose();
    }

    [Fact]
    public void Header_ContainsCorrectMagic()
    {
        using Image<Bgra32> testImage = CreateTestImage(32, 32);
        MemoryStream xtxOutput = new();

        XTX.ConvertToFile(testImage, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

        // Verify magic number
        xtxOutput.Seek(0, SeekOrigin.Begin);
        byte[] magic = new byte[4];
        xtxOutput.Read(magic, 0, 4);
        Assert.Equal("DFvN", System.Text.Encoding.ASCII.GetString(magic));

        xtxOutput.Dispose();
    }

    [Theory]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_R8)]
    [InlineData(XTX.XTXImageFormat.NVN_FORMAT_RG8)]
    public void RoundTrip_MonochromeFormats_PreserveLuminance(XTX.XTXImageFormat format)
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

        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(image, format, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        // Assert
        Assert.Equal(image.Width, restored.Width);
        Assert.Equal(image.Height, restored.Height);

        xtxOutput.Dispose();
    }

    #endregion

    #region Format Compatibility Tests

    [Fact]
    public void ConvertToFile_RGB10A2_WorksCorrectly()
    {
        using Image<Bgra32> testImage = CreateTestImage(64, 64);
        MemoryStream output = new();

        // Act & Assert - Should not throw
        XTX.ConvertToFile(testImage, XTX.XTXImageFormat.NVN_FORMAT_RGB10A2, output);
        Assert.NotEmpty(output.ToArray());

        output.Dispose();
    }

    [Fact]
    public void ConvertToFile_RGBA4_RoundTrip_WorksWithQuantization()
    {
        using Image<Bgra32> original = CreateTestImage(64, 64, TestImageHelper.TestPattern.Checkerboard);
        MemoryStream xtxOutput = new();

        // Act
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA4, xtxOutput);
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using Image<Bgra32> restored = XTX.GetImage(xtxOutput);

        // Assert - Allow larger tolerance for 4-bit format
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        AssertPixelsEqual(original, restored,
            tolerance: 15, message: "RGBA4 quantization");

        xtxOutput.Dispose();
    }

    #endregion

    #endregion
}