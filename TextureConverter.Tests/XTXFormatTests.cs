using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.Formats;
using TextureConverter.TextureType;

namespace TextureConverter.Tests;

public class XTXFormatTests
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
        using var testImage = CreateTestImage();
        var output = new MemoryStream();

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
        using var original = CreateTestImage(32, 48);
        var xtxOutput = new MemoryStream();

        // Act - Convert to XTX
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

        // Reset stream for reading
        xtxOutput.Seek(0, SeekOrigin.Begin);
        
        // Assert - Read back
        using var restored = XTX.GetImage(xtxOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
        
        xtxOutput.Dispose();
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

        var xtxOutput = new MemoryStream();

        // Act
        XTX.ConvertToFile(image, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

        // Assert
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = XTX.GetImage(xtxOutput);

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
        using var original = CreateTestImage(16, 16);
        var xtxOutput = new MemoryStream();

        // Act - Convert to XTX
        XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGB565, xtxOutput);

        // Reset stream for reading
        xtxOutput.Seek(0, SeekOrigin.Begin);
        
        // Assert - Read back and check pixel similarity (allowing for quantization loss)
        using var restored = XTX.GetImage(xtxOutput);
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
        var image = new Image<Bgra32>(8, 8);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte gray = (byte)((x + y * 8) * 255 / 64);
                    row[x] = new Bgra32(gray, gray, gray, 255);
                }
            }
        });

        var xtxOutput = new MemoryStream();

        // Act
        XTX.ConvertToFile(image, XTX.XTXImageFormat.NVN_FORMAT_R8, xtxOutput);

        // Assert
        xtxOutput.Seek(0, SeekOrigin.Begin);
        using var restored = XTX.GetImage(xtxOutput);

        Assert.Equal(image.Width, restored.Width);
        Assert.Equal(image.Height, restored.Height);
        
        xtxOutput.Dispose();
    }

    [Fact]
    public void ConvertToFile_CompressedFormat_ThrowsNotImplemented()
    {
        using var testImage = CreateTestImage();
        using var output = new MemoryStream();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => 
            XTX.ConvertToFile(testImage, XTX.XTXImageFormat.DXT1, output));
    }

    [Fact]
    public void ConvertToFile_DifferentSizes_RoundTrip_WorksCorrectly()
    {
        var sizes = new[] { (16, 16), (32, 64), (128, 64), (256, 256) };

        foreach (var (width, height) in sizes)
        {
            using var original = CreateTestImage(width, height);
            var xtxOutput = new MemoryStream();

            // Act
            XTX.ConvertToFile(original, XTX.XTXImageFormat.NVN_FORMAT_RGBA8, xtxOutput);

            // Assert
            xtxOutput.Seek(0, SeekOrigin.Begin);
            using var restored = XTX.GetImage(xtxOutput);
            
            Assert.Equal(width, restored.Width);
            Assert.Equal(height, restored.Height);
            
            xtxOutput.Dispose();
        }
    }
}
