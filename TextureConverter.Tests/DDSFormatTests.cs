using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using TextureConverter.Formats;
using TextureConverter.TextureType;

namespace TextureConverter.Tests;

public class DDSFormatTests
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
    [InlineData(DDS.DDSFormat.RGBA8)]
    [InlineData(DDS.DDSFormat.RGBA_SRGB)]
    [InlineData(DDS.DDSFormat.RGB565)]
    [InlineData(DDS.DDSFormat.RGB5A1)]
    [InlineData(DDS.DDSFormat.RGBA4)]
    [InlineData(DDS.DDSFormat.L8)]
    [InlineData(DDS.DDSFormat.LA8)]
    public void ConvertToFile_CreatesValidDDSFile(DDS.DDSFormat format)
    {
        using var testImage = CreateTestImage();
        using var output = new MemoryStream();

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
        using var original = CreateTestImage(32, 48);
        using var ddsOutput = new MemoryStream();

        // Act - Convert to DDS
        DDS.ConvertToFile(original, DDS.DDSFormat.RGBA8, ddsOutput);

        // Reset stream for reading
        ddsOutput.Seek(0, SeekOrigin.Begin);
        
        // Assert - Read back
        using var restored = DDS.GetImage(ddsOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);
    }

    [Fact]
    public void ConvertToFile_RGB565_RoundTrip_SimilarPixelValues()
    {
        using var original = CreateTestImage(16, 16);
        using var ddsOutput = new MemoryStream();

        // Act - Convert to DDS
        DDS.ConvertToFile(original, DDS.DDSFormat.RGB565, ddsOutput);

        // Reset stream for reading
        ddsOutput.Seek(0, SeekOrigin.Begin);
        
        // Assert - Read back and check pixel similarity (allowing for quantization loss)
        using var restored = DDS.GetImage(ddsOutput);
        Assert.Equal(original.Width, restored.Width);
        Assert.Equal(original.Height, restored.Height);

        int pixelErrorCount = 0;
        int totalPixels = 0;

        // Copy pixel data to compare outside of ProcessPixelRows
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
    }

    [Fact]
    public void ConvertToFile_RGBA8_AllOpaque_PixelsMatch()
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

        using var ddsOutput = new MemoryStream();

        // Act
        DDS.ConvertToFile(image, DDS.DDSFormat.RGBA8, ddsOutput);

        // Assert
        ddsOutput.Seek(0, SeekOrigin.Begin);
        using var restored = DDS.GetImage(ddsOutput);

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
        using var testImage = CreateTestImage();
        using var output = new MemoryStream();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => 
            DDS.ConvertToFile(testImage, DDS.DDSFormat.DXT1, output));
    }
}
