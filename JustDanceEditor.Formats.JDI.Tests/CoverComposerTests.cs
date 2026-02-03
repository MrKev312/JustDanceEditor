using JustDanceEditor.Formats.JDI.Services.Images;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class CoverComposerTests
{
    #region ComposeAlbumCoach Tests

    [Fact]
    public void ComposeAlbumCoach_WithEmptyList_ReturnsEmptyCanvas()
    {
        // Arrange
        List<Image<Bgra32>> coaches = [];

        // Act
        using Image<Bgra32> result = CoverComposer.ComposeAlbumCoach(coaches);

        // Assert
        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
        // Empty canvas should be transparent/black
        AssertAllPixelsAreTransparent(result);
    }

    [Fact]
    public void ComposeAlbumCoach_WithSingleCoach_CentersCoach()
    {
        // Arrange
        using Image<Bgra32> coach = CreateTestCoachImage(1, Color.Red);
        List<Image<Bgra32>> coaches = [coach];

        // Act
        using Image<Bgra32> result = CoverComposer.ComposeAlbumCoach(coaches);

        // Assert
        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
        // Center pixel should have some color (not fully transparent)
        Bgra32 centerPixel = result[512, 512];
        Assert.True(centerPixel.A > 0, "Center pixel should not be transparent");
    }

    [Fact]
    public void ComposeAlbumCoach_WithTwoCoaches_PlacesSideBySide()
    {
        // Arrange
        using Image<Bgra32> coach1 = CreateTestCoachImage(1, Color.Red);
        using Image<Bgra32> coach2 = CreateTestCoachImage(2, Color.Blue);
        List<Image<Bgra32>> coaches = [coach1, coach2];

        // Act
        using Image<Bgra32> result = CoverComposer.ComposeAlbumCoach(coaches);

        // Assert
        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
        // Left side should have content
        bool hasLeftContent = HasNonTransparentPixels(result, 0, 256);
        // Right side should have content
        bool hasRightContent = HasNonTransparentPixels(result, 768, 1024);
        Assert.True(hasLeftContent, "Left side should have content");
        Assert.True(hasRightContent, "Right side should have content");
    }

    [Fact]
    public void ComposeAlbumCoach_WithThreeCoaches_PlacesTwoBackOneFront()
    {
        // Arrange
        using Image<Bgra32> coach1 = CreateTestCoachImage(1, Color.Red);
        using Image<Bgra32> coach2 = CreateTestCoachImage(2, Color.Green);
        using Image<Bgra32> coach3 = CreateTestCoachImage(3, Color.Blue);
        List<Image<Bgra32>> coaches = [coach1, coach2, coach3];

        // Act
        using Image<Bgra32> result = CoverComposer.ComposeAlbumCoach(coaches);

        // Assert
        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
        // Should have content in multiple areas
        bool hasContent = HasNonTransparentPixels(result, 0, 1024);
        Assert.True(hasContent, "Canvas should have content");
    }

    [Fact]
    public void ComposeAlbumCoach_WithFourCoaches_PlacesTwoBackTwoFront()
    {
        // Arrange
        using Image<Bgra32> coach1 = CreateTestCoachImage(1, Color.Red);
        using Image<Bgra32> coach2 = CreateTestCoachImage(2, Color.Green);
        using Image<Bgra32> coach3 = CreateTestCoachImage(3, Color.Blue);
        using Image<Bgra32> coach4 = CreateTestCoachImage(4, Color.Yellow);
        List<Image<Bgra32>> coaches = [coach1, coach2, coach3, coach4];

        // Act
        using Image<Bgra32> result = CoverComposer.ComposeAlbumCoach(coaches);

        // Assert
        Assert.Equal(1024, result.Width);
        Assert.Equal(1024, result.Height);
        // Should have content spread across the canvas
        bool hasContent = HasNonTransparentPixels(result, 0, 1024);
        Assert.True(hasContent, "Canvas should have content");
    }

    [Fact]
    public void ComposeAlbumCoach_WithCustomSize_ReturnsCorrectSize()
    {
        // Arrange
        using Image<Bgra32> coach = CreateTestCoachImage(1, Color.Red);
        List<Image<Bgra32>> coaches = [coach];
        int customSize = 512;

        // Act
        using Image<Bgra32> result = CoverComposer.ComposeAlbumCoach(coaches, customSize);

        // Assert
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public void ComposeAlbumCoach_WithNullList_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CoverComposer.ComposeAlbumCoach(null!));
    }

    #endregion

    #region CreatePlaceholder Tests

    [Fact]
    public void CreatePlaceholder_CreatesCorrectSize()
    {
        // Act
        using Image<Bgra32> result = CoverComposer.CreatePlaceholder("Test", 640, 360);

        // Assert
        Assert.Equal(640, result.Width);
        Assert.Equal(360, result.Height);
    }

    [Fact]
    public void CreatePlaceholder_HasMagentaBackground()
    {
        // Act
        using Image<Bgra32> result = CoverComposer.CreatePlaceholder("Test", 100, 100);

        // Assert - corner pixel should be magenta
        Bgra32 corner = result[0, 0];
        Assert.Equal(255, corner.R);
        Assert.Equal(0, corner.G);
        Assert.Equal(255, corner.B);
    }

    #endregion

    #region GenerateMapBackground Tests

    [Fact]
    public void GenerateMapBackground_ResizesTo2048x1024()
    {
        // Arrange
        using Image<Bgra32> background = new(800, 600, Color.Blue);

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateMapBackground(background);

        // Assert
        Assert.Equal(2048, result.Width);
        Assert.Equal(1024, result.Height);
    }

    [Fact]
    public void GenerateMapBackground_WithNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CoverComposer.GenerateMapBackground(null!));
    }

    #endregion

    #region GenerateBanner Tests

    [Fact]
    public void GenerateBanner_ResizesTo1024x512()
    {
        // Arrange
        using Image<Bgra32> background = new(800, 600, Color.Green);

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateBanner(background);

        // Assert
        Assert.Equal(1024, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public void GenerateBanner_WithNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CoverComposer.GenerateBanner(null!));
    }

    #endregion

    #region CreateSquareCover Tests

    [Fact]
    public void CreateSquareCover_SquashesToDefaultSize()
    {
        // Arrange
        using Image<Bgra32> cover = new(640, 360, Color.Red);

        // Act
        using Image<Bgra32> result = CoverComposer.CreateSquareCover(cover);

        // Assert
        Assert.Equal(512, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public void CreateSquareCover_SquashesToCustomSize()
    {
        // Arrange
        using Image<Bgra32> cover = new(640, 360, Color.Red);

        // Act
        using Image<Bgra32> result = CoverComposer.CreateSquareCover(cover, 256);

        // Assert
        Assert.Equal(256, result.Width);
        Assert.Equal(256, result.Height);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test coach image with a colored vertical stripe in the center
    /// to simulate a person-shaped image with transparent edges.
    /// </summary>
    private static Image<Bgra32> CreateTestCoachImage(int coachId, Color color)
    {
        Image<Bgra32> image = new(1024, 1024, new Bgra32(0, 0, 0, 0));

        // Draw a vertical stripe in the center to simulate a coach
        int stripeWidth = 200;
        int stripeLeft = (1024 - stripeWidth) / 2;
        int stripeRight = stripeLeft + stripeWidth;

        Bgra32 pixel = new(color.ToPixel<Bgra32>().B, color.ToPixel<Bgra32>().G, color.ToPixel<Bgra32>().R, 255);

        for (int y = 0; y < 1024; y++)
        {
            for (int x = stripeLeft; x < stripeRight; x++)
            {
                image[x, y] = pixel;
            }
        }

        return image;
    }

    private static void AssertAllPixelsAreTransparent(Image<Bgra32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                foreach (Bgra32 pixel in row)
                {
                    Assert.Equal(0, pixel.A);
                }
            }
        });
    }

    private static bool HasNonTransparentPixels(Image<Bgra32> image, int xStart, int xEnd)
    {
        bool found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !found; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = xStart; x < xEnd && x < row.Length; x++)
                {
                    if (row[x].A > 0)
                    {
                        found = true;
                        break;
                    }
                }
            }
        });
        return found;
    }

    #endregion
}