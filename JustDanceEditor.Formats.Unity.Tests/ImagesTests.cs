using JustDanceEditor.Formats.Unity.Images;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Collections.Generic;
using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.Unity.Tests;

public class UnityMenuArtSourceTests
{
    [Fact]
    public void UnityMenuArtSource_CanBeInstantiatedWithAllValues()
    {
        // Arrange
        string coverPath = "/path/to/cover.png";
        string titleLogoPath = "/path/to/title.png";
        string coachesPath = "/path/to/coaches.png";
        List<string> coachImages = ["/path/coach1.png", "/path/coach2.png"];

        // Act
        UnityMenuArtSource source = new(coverPath, titleLogoPath, coachesPath, coachImages);

        // Assert
        Assert.Equal(coverPath, source.CoverPath);
        Assert.Equal(titleLogoPath, source.SongTitleLogoPath);
        Assert.Equal(coachesPath, source.CoachesBackgroundPath);
        Assert.Equal(2, source.CoachImagePaths.Count);
    }

    [Fact]
    public void UnityMenuArtSource_CanBeInstantiatedWithNullValues()
    {
        // Act
        UnityMenuArtSource source = new(null, null, null, []);

        // Assert
        Assert.Null(source.CoverPath);
        Assert.Null(source.SongTitleLogoPath);
        Assert.Null(source.CoachesBackgroundPath);
        Assert.Empty(source.CoachImagePaths);
    }

    [Fact]
    public void UnityMenuArtSource_IsRecord_SupportsEquality()
    {
        // Arrange
        UnityMenuArtSource source1 = new("/path/cover.png", null, null, []);
        UnityMenuArtSource source2 = new("/path/cover.png", null, null, []);
        UnityMenuArtSource source3 = new("/path/other.png", null, null, []);

        // Act & Assert
        Assert.Equal(source1, source2);
        Assert.NotEqual(source1, source3);
    }
}

public class ImageLoaderTests
{
    [Fact]
    public void TryLoadImage_WithNullPath_ReturnsNull()
    {
        // Act
        Image<Rgba32>? result = ImageLoader.TryLoadImage(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryLoadImage_WithEmptyPath_ReturnsNull()
    {
        // Act
        Image<Rgba32>? result = ImageLoader.TryLoadImage(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryLoadImage_WithNonexistentFile_ReturnsNull()
    {
        // Act
        Image<Rgba32>? result = ImageLoader.TryLoadImage("/path/to/nonexistent/image.png");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryLoadImage_WithValidPngFile_ReturnsImage()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string imagePath = Path.Combine(tempDir, "test.png");

        try
        {
            using (Image<Rgba32> image = new(100, 100))
            {
                image.SaveAsPng(imagePath);
            }

            // Act
            Image<Rgba32>? result = ImageLoader.TryLoadImage(imagePath);

            // Assert
            using Image<Rgba32> loadedImage = result ?? throw new System.InvalidOperationException("Expected the image to load successfully.");
            Assert.Equal(100, loadedImage.Width);
            Assert.Equal(100, loadedImage.Height);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryLoadImage_WithInvalidFile_ThrowsUnknownImageFormatException()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        string invalidPath = Path.Combine(tempDir, "invalid.png");

        try
        {
            File.WriteAllText(invalidPath, "not an image");

            // Act & Assert - ImageLoader.TryLoadImage throws on invalid format
            Assert.Throws<UnknownImageFormatException>(() =>
                ImageLoader.TryLoadImage(invalidPath));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryLoadImage_WithWhitespaceOnlyPath_ReturnsNull()
    {
        // Act
        Image<Rgba32>? result = ImageLoader.TryLoadImage("   ");

        // Assert
        Assert.Null(result);
    }
}