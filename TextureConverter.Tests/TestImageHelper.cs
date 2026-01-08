using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TextureConverter.Tests;

/// <summary>
/// Helper class for generating test images and patterns.
/// </summary>
public static class TestImageHelper
{
    /// <summary>
    /// Test pattern types for synthetic image generation.
    /// </summary>
    public enum TestPattern
    {
        Gradient,      // X gradient with Y variation
        Checkerboard,  // Alternating 2x2 blocks
        Numbered,      // Each pixel colored by position
        SingleColor,   // Solid color
        Striped        // Horizontal stripes
    }

    /// <summary>
    /// Creates a test image with the default gradient pattern.
    /// </summary>
    public static Image<Bgra32> CreateTestImage(int width = 64, int height = 64)
    {
        return CreateTestImage(width, height, TestPattern.Gradient);
    }

    /// <summary>
    /// Creates a test image with the specified pattern.
    /// </summary>
    public static Image<Bgra32> CreateTestImage(int width, int height, TestPattern pattern)
    {
        Image<Bgra32> image = new(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Bgra32 color = pattern switch
                    {
                        TestPattern.Gradient => new Bgra32(
                            (byte)(x * 255 / width),
                            (byte)(y * 255 / height),
                            (byte)((x + y) * 255 / (width + height)),
                            255),
                        TestPattern.Checkerboard => (((x >> 1) ^ (y >> 1)) & 1) == 0
                            ? new Bgra32(255, 255, 255, 255)
                            : new Bgra32(0, 0, 0, 255),
                        TestPattern.Numbered => new Bgra32(
                            (byte)(x % 256),
                            (byte)(y % 256),
                            (byte)((x + y) % 256),
                            255),
                        TestPattern.SingleColor => new Bgra32(128, 64, 32, 255),
                        TestPattern.Striped => y / 4 % 2 == 0
                            ? new Bgra32(255, 0, 0, 255)
                            : new Bgra32(0, 0, 255, 255),
                        _ => new Bgra32(255, 255, 255, 255)
                    };
                    row[x] = color;
                }
            }
        });
        return image;
    }

    /// <summary>
    /// Extracts pixel data from an image for comparison.
    /// </summary>
    public static Bgra32[] ExtractPixels(Image<Bgra32> image)
    {
        Bgra32[] pixels = new Bgra32[image.Width * image.Height];
        image.ProcessPixelRows(accessor =>
        {
            int idx = 0;
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    pixels[idx++] = row[x];
                }
            }
        });
        return pixels;
    }

    /// <summary>
    /// Compares two pixel arrays with optional tolerance.
    /// </summary>
    public static void AssertPixelsClose(Bgra32[] expected, Bgra32[] actual,
        int toleranceR = 0, int toleranceG = 0, int toleranceB = 0, int toleranceA = 0,
        double maxErrorRate = 0.0, string message = "")
    {
        if (expected.Length != actual.Length)
        {
            throw new Xunit.Sdk.XunitException($"{message} Array length mismatch: {expected.Length} vs {actual.Length}");
        }

        int errorCount = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            int rDiff = Math.Abs(expected[i].R - actual[i].R);
            int gDiff = Math.Abs(expected[i].G - actual[i].G);
            int bDiff = Math.Abs(expected[i].B - actual[i].B);
            int aDiff = Math.Abs(expected[i].A - actual[i].A);

            if (rDiff > toleranceR || gDiff > toleranceG || bDiff > toleranceB || aDiff > toleranceA)
            {
                errorCount++;
            }
        }

        if (maxErrorRate > 0)
        {
            double errorRate = (double)errorCount / expected.Length;
            if (errorRate > maxErrorRate)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{message} Error rate {errorRate:P} exceeds {maxErrorRate:P} threshold");
            }
        }
        else
        {
            if (errorCount > 0)
            {
                throw new Xunit.Sdk.XunitException($"{message} {errorCount} pixels differ");
            }
        }
    }

    /// <summary>
    /// Verifies that all pixels in two images match exactly.
    /// </summary>
    public static void AssertPixelsEqual(Image<Bgra32> expected, Image<Bgra32> actual, string message = "")
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new Xunit.Sdk.XunitException(
                $"{message} Image dimensions mismatch: {expected.Width}x{expected.Height} vs {actual.Width}x{actual.Height}");
        }

        Bgra32[] expectedPixels = ExtractPixels(expected);
        Bgra32[] actualPixels = ExtractPixels(actual);

        for (int i = 0; i < expectedPixels.Length; i++)
        {
            if (!expectedPixels[i].Equals(actualPixels[i]))
            {
                throw new Xunit.Sdk.XunitException(
                    $"{message} Pixel mismatch at offset {i}: expected {expectedPixels[i]}, got {actualPixels[i]}");
            }
        }
    }

    /// <summary>
    /// Verifies that all pixels in two images match within acceptable tolerance.
    /// </summary>
    public static void AssertPixelsEqual(Image<Bgra32> expected, Image<Bgra32> actual,
        int tolerance = 0, string message = "")
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new Xunit.Sdk.XunitException(
                $"{message} Image dimensions mismatch: {expected.Width}x{expected.Height} vs {actual.Width}x{actual.Height}");
        }

        Bgra32[] expectedPixels = ExtractPixels(expected);
        Bgra32[] actualPixels = ExtractPixels(actual);

        AssertPixelsClose(expectedPixels, actualPixels,
            toleranceR: tolerance, toleranceG: tolerance, toleranceB: tolerance, toleranceA: tolerance,
            message: message);
    }
}