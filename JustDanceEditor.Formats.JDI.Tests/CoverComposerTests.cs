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
        Assert.Throws<ArgumentNullException>(() => CoverComposer.ComposeAlbumCoach(null));
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

    [Fact]
    public void CreateMissingBackground_HasPurpleBackground()
    {
        // Act
        using Image<Bgra32> result = CoverComposer.CreateMissingBackground(100, 100);

        // Assert - corner pixel should be purple
        Bgra32 corner = result[0, 0];
        Assert.Equal(128, corner.R);
        Assert.Equal(0, corner.G);
        Assert.Equal(128, corner.B);
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
        Assert.Throws<ArgumentNullException>(() => CoverComposer.GenerateMapBackground(null));
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
        Assert.Throws<ArgumentNullException>(() => CoverComposer.GenerateBanner(null));
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

    #region GenerateMapBackgroundFromBanner Tests

    [Fact]
    public void GenerateMapBackgroundFromBanner_WithMainMode_GeneratesBackground()
    {
        // Arrange
        using Image<Bgra32> banner = CreateTestBannerImage();

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateMapBackgroundFromBanner(
            banner,
            "#FF5733FF", // Orange 1a
            "#FFB347FF", // Light orange 1b
            "#3498DBFF", // Blue 2a
            "#5DADE2FF", // Light blue 2b
            BannerColorMode.Main);

        // Assert
        Assert.Equal(CoverComposer.BackgroundWidth, result.Width);
        Assert.Equal(CoverComposer.BackgroundHeight, result.Height);
        Assert.True(HasAnyNonZeroPixels(result));
    }

    [Fact]
    public void GenerateMapBackgroundFromBanner_WithAlternateMode_UsesAlternateColors()
    {
        // Arrange
        using Image<Bgra32> banner = CreateSolidGrayscaleBanner(128);

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateMapBackgroundFromBanner(
            banner,
            "#FF0000FF", // Red 1a
            "#FF0000FF", // Red 1b
            "#00FF00FF", // Green 2a
            "#00FF00FF", // Green 2b
            BannerColorMode.Alternate);

        // Assert
        // Should have green tint from 2a/2b colors
        Bgra32 centerPixel = result[result.Width / 2, result.Height / 2];
        Assert.True(centerPixel.G > centerPixel.R, "Should use green (alternate) colors, not red (main) colors");
    }

    [Fact]
    public void GenerateMapbackgroundFromBanner_WithGradientMode_CreatesVerticalGradient()
    {
        // Arrange
        using Image<Bgra32> banner = CreateSolidGrayscaleBanner(128);

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateMapBackgroundFromBanner(
            banner,
            "#FF0000FF", // Red 1a
            "#FF0000FF", // Red 1b
            "#0000FFFF", // Blue 2a (ignored in gradient mode)
            "#0000FFFF", // Blue 2b (ignored in gradient mode)
            BannerColorMode.Gradient);

        // Assert - gradient mode uses only 1a/1b colors throughout
        // Creates gradient: top 1b,1b -> middle 1a,white blend -> bottom 1a,1a
        int topY = result.Height / 6;      // Top third (pure 1b = red)
        int middleY = result.Height / 2;   // Middle third (1a blended)
        int bottomY = result.Height * 5 / 6; // Bottom third (pure 1a = red)

        Bgra32 topPixel = result[result.Width / 2, topY];
        Bgra32 middlePixel = result[result.Width / 2, middleY];
        Bgra32 bottomPixel = result[result.Width / 2, bottomY];

        // All positions should have red as primary color (from 1a/1b)
        // Top should be pure red (1b)
        Assert.True(topPixel.R > topPixel.B, "Top should be red from 1b");
        Assert.True(topPixel.R > topPixel.G, "Top should be pure red (no green added)");

        // Middle should also be red (from 1a), but with some white blend
        Assert.True(middlePixel.R > 0, "Middle should contain red from 1a");

        // Bottom should be red (1a)
        Assert.True(bottomPixel.R > bottomPixel.B, "Bottom should be red from 1a");
    }

    [Fact]
    public void GenerateMapBackgroundFromBanner_WithBlueChannelGradient_CreatesHorizontalBlending()
    {
        // Arrange - create a banner with strong horizontal gradient
        using Image<Bgra32> banner = CreateStrongGradientBanner();

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateMapBackgroundFromBanner(
            banner,
            "#FF0000FF", // Red 1a
            "#00FF00FF", // Green 1b
            "#0000FFFF", // Blue 2a
            "#FFFF00FF", // Yellow 2b
            BannerColorMode.Main);

        // Assert - sample multiple points across the horizontal gradient
        Bgra32 leftPixel = result[100, result.Height / 2];
        Bgra32 middlePixel = result[result.Width / 2, result.Height / 2];
        Bgra32 rightPixel = result[result.Width - 100, result.Height / 2];

        // Calculate brightness to check gradient effect
        int brightnessLeft = leftPixel.R + leftPixel.G + leftPixel.B;
        int brightnessMiddle = middlePixel.R + middlePixel.G + middlePixel.B;
        int brightnessRight = rightPixel.R + rightPixel.G + rightPixel.B;

        // The gradient should create variation in brightness
        bool hasVariation = Math.Abs(brightnessLeft - brightnessRight) > 20 ||
                           Math.Abs(brightnessLeft - brightnessMiddle) > 20 ||
                           Math.Abs(brightnessMiddle - brightnessRight) > 20;

        Assert.True(hasVariation,
            $"Gradient should create brightness variation (L:{brightnessLeft}, M:{brightnessMiddle}, R:{brightnessRight})");
    }

    [Theory]
    [InlineData(BannerColorMode.Main)]
    [InlineData(BannerColorMode.Alternate)]
    [InlineData(BannerColorMode.Gradient)]
    public void GenerateMapBackgroundFromBanner_WithAllModes_ProducesValidOutput(BannerColorMode mode)
    {
        // Arrange
        using Image<Bgra32> banner = CreateTestBannerImage();

        // Act
        using Image<Bgra32> result = CoverComposer.GenerateMapBackgroundFromBanner(
            banner,
            "#FF5733FF",
            "#FFB347FF",
            "#3498DBFF",
            "#5DADE2FF",
            mode);

        // Assert
        Assert.Equal(CoverComposer.BackgroundWidth, result.Width);
        Assert.Equal(CoverComposer.BackgroundHeight, result.Height);
        Assert.True(HasAnyNonZeroPixels(result));
    }

    [Fact]
    public void GenerateMapBackgroundFromBanner_WithNullBanner_ThrowsArgumentNullException()
    {
        // Arrange
        Image<Bgra32>? banner = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CoverComposer.GenerateMapBackgroundFromBanner(
            banner, "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF"));
    }

    [Theory]
    [InlineData(null, "#FF", "#FF", "#FF")]
    [InlineData("#FF", null, "#FF", "#FF")]
    [InlineData("#FF", "#FF", null, "#FF")]
    [InlineData("#FF", "#FF", "#FF", null)]
    public void GenerateMapBackgroundFromBanner_WithNullColors_ThrowsArgumentNullException(
        string? color1a, string? color1b, string? color2a, string? color2b)
    {
        // Arrange
        using Image<Bgra32> banner = CreateTestBannerImage();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CoverComposer.GenerateMapBackgroundFromBanner(
            banner, color1a, color1b, color2a, color2b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateMapBackgroundFromBanner_WithEmptyOrWhitespaceColor_ThrowsArgumentException(string invalidColor)
    {
        // Arrange
        using Image<Bgra32> banner = CreateTestBannerImage();

        // Act & Assert - try with invalid color in each position
        Assert.Throws<ArgumentException>(() => CoverComposer.GenerateMapBackgroundFromBanner(
            banner, invalidColor, "#FFFFFFFF", "#FFFFFFFF", "#FFFFFFFF"));
    }

    private static Image<Bgra32> CreateTestBannerImage()
    {
        Image<Bgra32> banner = new(1024, 512);
        // Create a simple pattern with varying blue and green channels
        for (int y = 0; y < banner.Height; y++)
        {
            for (int x = 0; x < banner.Width; x++)
            {
                byte blue = (byte)(x * 255 / banner.Width);
                byte green = (byte)(y * 255 / banner.Height);
                banner[x, y] = new Bgra32(128, green, blue, 255);
            }
        }

        return banner;
    }

    private static Image<Bgra32> CreateSolidGrayscaleBanner(byte grayValue)
    {
        Image<Bgra32> banner = new(1024, 512);
        Bgra32 pixel = new(grayValue, grayValue, grayValue, 255);
        for (int y = 0; y < banner.Height; y++)
        {
            for (int x = 0; x < banner.Width; x++)
            {
                banner[x, y] = pixel;
            }
        }

        return banner;
    }

    private static Image<Bgra32> CreateStrongGradientBanner()
    {
        Image<Bgra32> banner = new(1024, 512);
        for (int y = 0; y < banner.Height; y++)
        {
            for (int x = 0; x < banner.Width; x++)
            {
                // Create a strong horizontal gradient: left = full blue (weight 1.0), right = no blue (weight 0.0)
                byte blue = (byte)(255 - (x * 255 / banner.Width));
                // Also vary green to add highlights
                byte green = (byte)(x * 100 / banner.Width);
                banner[x, y] = new Bgra32(128, green, blue, 255);
            }
        }

        return banner;
    }

    private static bool HasAnyNonZeroPixels(Image<Bgra32> image)
    {
        bool found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !found; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].R != 0 || row[x].G != 0 || row[x].B != 0)
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
