using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.JDI.Services.Images;

/// <summary>
/// Defines how banner colors are applied when generating map backgrounds.
/// </summary>
public enum BannerColorMode
{
    /// <summary>
    /// Use only primary colors (songcolor_1a and songcolor_1b).
    /// Recommended for pre-JD2019 songs.
    /// </summary>
    Main,

    /// <summary>
    /// Use only alternate colors (songcolor_2a and songcolor_2b).
    /// </summary>
    Alternate,

    /// <summary>
    /// Use gradient blending: top is pure 1b, middle blends 1a with white, bottom blends 1b with white.
    /// Recommended for JD2019+ songs.
    /// </summary>
    Gradient
}

/// <summary>
/// Provides cover image generation and composition utilities.
/// </summary>
public static class CoverComposer
{
    /// <summary>
    /// Standard cover dimensions (16:9 aspect ratio).
    /// </summary>
    public const int CoverWidth = 640;
    public const int CoverHeight = 360;

    /// <summary>
    /// Full resolution background dimensions.
    /// </summary>
    public const int BackgroundWidth = 2048;
    public const int BackgroundHeight = 1024;

    /// <summary>
    /// Album coach composite dimensions.
    /// </summary>
    public const int AlbumCoachSize = 1024;

    /// <summary>
    /// Banner dimensions.
    /// </summary>
    public const int BannerWidth = 1024;
    public const int BannerHeight = 512;

    /// <summary>
    /// Generates a cover image from a background and album coach composite.
    /// </summary>
    /// <param name="background">The background image (ideally 2048x1024).</param>
    /// <param name="albumCoach">The album coach composite image (ideally 1024x1024). Can be null.</param>
    /// <param name="targetWidth">Target cover width (default 640).</param>
    /// <param name="targetHeight">Target cover height (default 360).</param>
    /// <returns>The composed cover image.</returns>
    public static Image<Bgra32> ComposeCover(
        Image<Bgra32> background,
        Image<Bgra32>? albumCoach = null,
        int targetWidth = CoverWidth,
        int targetHeight = CoverHeight)
    {
        ArgumentNullException.ThrowIfNull(background);

        // Clone background to avoid modifying original
        Image<Bgra32> result = background.Clone();

        // Ensure background is at standard size
        if (result.Width != BackgroundWidth || result.Height != BackgroundHeight)
        {
            result.Mutate(x => x.Resize(BackgroundWidth, BackgroundHeight));
        }

        // Draw album coach on the right side if provided
        if (albumCoach != null)
        {
            using Image<Bgra32> coachResized = albumCoach.Clone();
            coachResized.Mutate(x => x.Resize(AlbumCoachSize, AlbumCoachSize));
            result.Mutate(x => x.DrawImage(coachResized, new Point(512, 0), 1f));
        }

        // Resize to 720x360, then crop to 640x360 (centered)
        result.Mutate(x => x.Resize(720, 360));
        result.Mutate(x => x.Crop(new Rectangle(40, 0, 640, 360)));

        // Final resize to target dimensions
        if (targetWidth != CoverWidth || targetHeight != CoverHeight)
        {
            result.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Stretch
            }));
        }

        return result;
    }

    /// <summary>
    /// Creates a square cover by squashing the standard cover.
    /// </summary>
    /// <param name="cover">The standard cover image.</param>
    /// <param name="size">Target square size (default 512).</param>
    /// <returns>The square cover image.</returns>
    public static Image<Bgra32> CreateSquareCover(Image<Bgra32> cover, int size = 512)
    {
        ArgumentNullException.ThrowIfNull(cover);

        Image<Bgra32> result = cover.Clone();
        result.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Stretch
        }));

        return result;
    }

    /// <summary>
    /// Creates a placeholder image with a descriptive message.
    /// </summary>
    /// <param name="assetName">Name of the missing asset to display.</param>
    /// <param name="width">Width of the placeholder image.</param>
    /// <param name="height">Height of the placeholder image.</param>
    /// <returns>A purple placeholder image with "Missing: {assetName}" text.</returns>
    public static Image<Bgra32> CreatePlaceholder(string assetName, int width, int height)
    {
        Image<Bgra32> placeholder = new(width, height);
        placeholder.Mutate(x => x.BackgroundColor(Color.Magenta));

        string message = $"Missing: {assetName}";

        try
        {
            // Scale font size based on image dimensions
            float fontSize = Math.Min(width, height) / 8f;
            fontSize = Math.Max(fontSize, 12f); // Minimum readable size
            Font font = SystemFonts.CreateFont("Segoe UI", fontSize, FontStyle.Regular);

            // Center the text
            FontRectangle textBounds = TextMeasurer.MeasureSize(message, new TextOptions(font));
            float x = (width - textBounds.Width) / 2;
            float y = (height - textBounds.Height) / 2;

            placeholder.Mutate(ctx => ctx.DrawText(message, font, Color.FloralWhite, new PointF(x, y)));
        }
        catch
        {
            // Font may not be available on all systems - that's okay
        }

        return placeholder;
    }

    /// <summary>
    /// Creates a placeholder background image when none is available.
    /// </summary>
    /// <param name="message">Message to display on the placeholder.</param>
    /// <returns>A placeholder background image.</returns>
    public static Image<Bgra32> CreatePlaceholderBackground(string message = "Background not found")
    {
        return CreatePlaceholder(message, BackgroundWidth, BackgroundHeight);
    }

    /// <summary>
    /// Generates a map background from the coaches background.
    /// </summary>
    /// <param name="coachesBackground">The coaches background image.</param>
    /// <returns>Map background resized to 2048x1024.</returns>
    public static Image<Bgra32> GenerateMapBackground(Image<Bgra32> coachesBackground)
    {
        ArgumentNullException.ThrowIfNull(coachesBackground);

        Image<Bgra32> result = coachesBackground.Clone();
        result.Mutate(x => x.Resize(BackgroundWidth, BackgroundHeight));
        return result;
    }

    /// <summary>
    /// Generates a banner from the coaches background.
    /// </summary>
    /// <param name="coachesBackground">The coaches background image.</param>
    /// <returns>Banner resized to 1024x512.</returns>
    public static Image<Bgra32> GenerateBanner(Image<Bgra32> coachesBackground)
    {
        ArgumentNullException.ThrowIfNull(coachesBackground);

        // 1. Resize and convert to Grayscale to get baseline luminance
        Image<Bgra32> result = coachesBackground.Clone();
        result.Mutate(x => x
            .Resize(BannerWidth, BannerHeight)
            .Grayscale());

        // 2. Find Min/Max for Range Expansion (Normalization)
        byte min = 255;
        byte max = 0;
        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                foreach (ref Bgra32 p in row)
                {
                    if (p.R < min)
                        min = p.R;
                    if (p.R > max)
                        max = p.R;
                }
            }
        });

        float range = (max - min) < 1 ? 1 : (max - min);

        // 3. Map values to channels
        result.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    // Calculate the expanded grayscale value (0 to 255)
                    float normalized = (row[x].R - min) / range * 255f;
                    byte gray = (byte)Math.Clamp(normalized, 0, 255);

                    // Red: Standard B&W version (kept as is)
                    byte r = row[x].R;

                    // Green: Purely black for most of the image, ramps up at the 
                    // end to create the highlights
                    byte g = (byte)Math.Clamp((gray - 160) * 2.7, 0, 255);

                    // Blue: A darkened version that is used for most of the image,
                    // ramps down to black for the highlights
                    byte b = (byte)Math.Clamp((100 - gray) * 2.5, 0, 255);

                    row[x] = new Bgra32(r, g, b, row[x].A);
                }
            }
        });

        return result;
    }

    /// <summary>
    /// Generates a map background from a banner image using song colors.
    /// This processes the banner's blue and green channels to create a colored background.
    /// </summary>
    /// <param name="banner">The banner image to process.</param>
    /// <param name="songColor1A">Primary color A (hex format like "#FFFFFFFF" or "#RRGGBBAA").</param>
    /// <param name="songColor1B">Primary color B (hex format like "#FFFFFFFF" or "#RRGGBBAA").</param>
    /// <param name="songColor2A">Alternate color A (hex format like "#FFFFFFFF" or "#RRGGBBAA").</param>
    /// <param name="songColor2B">Alternate color B (hex format like "#FFFFFFFF" or "#RRGGBBAA").</param>
    /// <param name="colorMode">The color mode to use for blending.</param>
    /// <returns>A map background image sized to 2048x1024.</returns>
    public static Image<Bgra32> GenerateMapBackgroundFromBanner(
        Image<Bgra32> banner,
        string songColor1A,
        string songColor1B,
        string songColor2A,
        string songColor2B,
        BannerColorMode colorMode = BannerColorMode.Main)
    {
        ArgumentNullException.ThrowIfNull(banner);
        ArgumentException.ThrowIfNullOrWhiteSpace(songColor1A);
        ArgumentException.ThrowIfNullOrWhiteSpace(songColor1B);
        ArgumentException.ThrowIfNullOrWhiteSpace(songColor2A);
        ArgumentException.ThrowIfNullOrWhiteSpace(songColor2B);

        int width = banner.Width;
        int height = banner.Height;

        // Parse all color values
        Bgra32 color1A = ParseHexColor(songColor1A, new Bgra32(255, 255, 255, 255));
        Bgra32 color1B = ParseHexColor(songColor1B, new Bgra32(255, 255, 255, 255));
        Bgra32 color2A = ParseHexColor(songColor2A, new Bgra32(255, 255, 255, 255));
        Bgra32 color2B = ParseHexColor(songColor2B, new Bgra32(255, 255, 255, 255));

        // Process banner pixels
        Image<Bgra32> resultImage = new(width, height);
        for (int y = 0; y < height; y++)
        {
            // Determine which color pair to use based on mode and y position
            (Bgra32 colorA, Bgra32 colorB) = colorMode switch
            {
                BannerColorMode.Main => (color1A, color1B),
                BannerColorMode.Alternate => (color2A, color2B),
                BannerColorMode.Gradient => GetGradientColors(y, height, color1A, color1B, color2A, color2B),
                _ => (color1A, color1B)
            };

            for (int x = 0; x < width; x++)
            {
                Bgra32 pixel = banner[x, y];
                
                // Use blue channel as weight between colorA and colorB
                float weight = pixel.B / 255f;
                Bgra32 newColor = GetWeightedAverage(colorA, colorB, weight);
                
                // Add green channel to brighten highlights
                if (colorMode != BannerColorMode.Gradient)
                    newColor = AddGreenChannel(newColor, pixel.G);
                
                resultImage[x, y] = newColor;
            }
        }

        // Resize to standard map background dimensions
        resultImage.Mutate(x => x.Resize(BackgroundWidth, BackgroundHeight));
        
        return resultImage;
    }

    /// <summary>
    /// Determines which color pair to use for gradient mode based on vertical position.
    /// Transitions from 1b/1b to 1a/white, then to 1a/1a.
    /// </summary>
    private static (Bgra32 colorA, Bgra32 colorB) GetGradientColors(
        int y,
        int height,
        Bgra32 color1A,
        Bgra32 color1B,
        Bgra32 color2A,
        Bgra32 color2B)
    {
        float position = (float)y / height;
        Bgra32 white = new(255, 255, 255, 255);

        Bgra32 resA, resB;

        // Base gradient logic: 1b,1b -> 1a,white -> 1a,1a
        if (position < 0.5f)
        {
            // Transition from 1b,1b to 1a,white
            float blend = position / 0.5f; // 0 to 1
            resA = BlendColors(color1B, color1A, blend);
            resB = BlendColors(color1B, white, blend);
        }
        else
        {
            // Transition from 1a,white to 1a,1a
            float blend = (position - 0.5f) / 0.5f; // 0 to 1
            resA = color1A;
            resB = BlendColors(white, color1A, blend);
        }

        // Overlay: softened, lower-intensity fade to 1b — wider vertical spread and gentler peak
        // - Spread: expanded vertically so effect is less localized
        // - Intensity: peak reduced (was 40%) and eased with smoothstep for gentler edges
        if (position > 0.60f && position < 0.95f)
        {
            const float center = 0.825f;
            const float halfWidth = 0.175f; // affects ~0.60 -> 0.95

            float dist = Math.Abs(position - center);
            float t = Math.Clamp(1.0f - (dist / halfWidth), 0f, 1f); // 0..1
            // smoothstep to produce a gentler falloff
            float smooth = t * t * (3f - 2f * t);

            const float maxBlend = 0.20f; // reduce peak from 40% -> 20%
            float blendAmount = smooth * maxBlend;

            resA = BlendColors(resA, color1B, blendAmount);
            resB = BlendColors(resB, color1B, blendAmount);
        }

        return (resA, resB);
    }

    /// <summary>
    /// Blends two colors using linear interpolation.
    /// </summary>
    /// <param name="from">Starting color (blend = 0).</param>
    /// <param name="to">Target color (blend = 1).</param>
    /// <param name="blend">Blend factor from 0 to 1.</param>
    /// <returns>The blended color.</returns>
    private static Bgra32 BlendColors(Bgra32 from, Bgra32 to, float blend)
    {
        return new Bgra32(
            (byte)(from.R + (to.R - from.R) * blend),
            (byte)(from.G + (to.G - from.G) * blend),
            (byte)(from.B + (to.B - from.B) * blend),
            (byte)(from.A + (to.A - from.A) * blend)
        );
    }

    /// <summary>
    /// Generates an album background from the coaches background.
    /// Crops the center square and resizes to 256x256.
    /// </summary>
    /// <param name="coachesBackground">The coaches background image.</param>
    /// <param name="size">Target size (default 256x256).</param>
    /// <returns>Album background image.</returns>
    public static Image<Bgra32> GenerateAlbumBackground(Image<Bgra32> coachesBackground, int size = 256)
    {
        ArgumentNullException.ThrowIfNull(coachesBackground);

        // Crop the middle square
        int cropSize = Math.Min(coachesBackground.Width, coachesBackground.Height);
        int cropX = (coachesBackground.Width - cropSize) / 2;
        int cropY = (coachesBackground.Height - cropSize) / 2;

        Image<Bgra32> result = coachesBackground.Clone(x => x
            .Crop(new Rectangle(cropX, cropY, cropSize, cropSize))
            .Resize(size, size));

        return result;
    }
    /// <summary>
    /// Composes an album coach image from individual coach images using collision-aware positioning.
    /// Coaches are arranged in rows with the outer edges touching the canvas sides,
    /// scaled up until they collide or reach height limits.
    /// </summary>
    /// <param name="coachImages">Individual coach images in order (1-based index order).</param>
    /// <param name="size">Target size for the composite (default 1024x1024).</param>
    /// <returns>The composed album coach image.</returns>
    public static Image<Bgra32> ComposeAlbumCoach(IReadOnlyList<Image<Bgra32>> coachImages, int size = AlbumCoachSize)
    {
        ArgumentNullException.ThrowIfNull(coachImages);

        Image<Bgra32> canvas = new(size, size);

        if (coachImages.Count == 0)
            return canvas;

        // Normalize all coaches to 1024x1024 for consistent processing
        List<Image<Bgra32>> normalizedCoaches = [];
        try
        {
            foreach (Image<Bgra32> coach in coachImages)
            {
                Image<Bgra32> normalized = coach.Clone();
                normalized.Mutate(x => x.Resize(1024, 1024));
                normalizedCoaches.Add(normalized);
            }

            // Calculate visible bounding boxes for horizontal fitting
            Dictionary<Image<Bgra32>, (int MinX, int MaxX)> bounds = [];
            foreach (Image<Bgra32> coach in normalizedCoaches)
            {
                bounds[coach] = GetVisibleXRange(coach);
            }

            // Define rows based on coach count
            List<Image<Bgra32>> backRow = [];
            List<Image<Bgra32>> frontRow = [];

            int count = normalizedCoaches.Count;
            if (count == 1)
            {
                // Single coach centered
                float scale = (float)size / normalizedCoaches[0].Height;
                canvas.Mutate(ctx => DrawRow(ctx, normalizedCoaches, scale, isFrontRow: false, bounds, size));
                return canvas;
            }
            else if (count == 2)
            {
                // 2 side by side
                backRow.Add(normalizedCoaches[0]);
                backRow.Add(normalizedCoaches[1]);
            }
            else if (count == 3)
            {
                // 2 back, 1 front (middle)
                backRow.Add(normalizedCoaches[0]);
                backRow.Add(normalizedCoaches[2]);
                frontRow.Add(normalizedCoaches[1]);
            }
            else // >= 4
            {
                // 2 back, 2 front
                backRow.Add(normalizedCoaches[0]);
                backRow.Add(normalizedCoaches[3]);
                frontRow.Add(normalizedCoaches[1]);
                frontRow.Add(normalizedCoaches[2]);
            }

            // Calculate maximum valid scale
            float maxScaleBack = 100f;
            float maxScaleFront = 100f;

            if (backRow.Count == 2)
                maxScaleBack = GetMaxOverlapScale(backRow[0], backRow[1], size, bounds);

            if (frontRow.Count == 2)
                maxScaleFront = GetMaxOverlapScale(frontRow[0], frontRow[1], size, bounds);

            float finalScale;
            if (count == 2)
                finalScale = maxScaleBack;
            else if (count == 3)
                finalScale = maxScaleBack; // Front is single, governed by back row
            else
                finalScale = Math.Min(maxScaleBack, maxScaleFront);

            // Height limit: images cannot exceed canvas size
            foreach (Image<Bgra32> c in normalizedCoaches)
            {
                float hLimit = (float)size / c.Height;
                if (finalScale > hLimit)
                    finalScale = hLimit;
            }

            // Draw rows
            canvas.Mutate(ctx =>
            {
                DrawRow(ctx, backRow, finalScale, isFrontRow: false, bounds, size);
                DrawRow(ctx, frontRow, finalScale, isFrontRow: true, bounds, size);
            });

            return canvas;
        }
        finally
        {
            foreach (Image<Bgra32> coach in normalizedCoaches)
                coach.Dispose();
        }
    }

    private static void DrawRow(
        IImageProcessingContext ctx,
        List<Image<Bgra32>> row,
        float scale,
        bool isFrontRow,
        Dictionary<Image<Bgra32>, (int MinX, int MaxX)> bounds,
        int canvasSize)
    {
        if (row.Count == 0)
            return;

        // Calculate Y position (bottom-aligned)
        int scaledH = (int)(row[0].Height * scale);
        int yPos = canvasSize - scaledH;

        // Front row is moved down by 1/3 of height (legs cut off, faces visible)
        if (isFrontRow)
            yPos += scaledH / 3;

        if (row.Count == 1)
        {
            // Center single coach
            Image<Bgra32> img = row[0];
            int scaledW = (int)(img.Width * scale);
            int xPos = (canvasSize - scaledW) / 2;

            using Image<Bgra32> clone = img.Clone(x => x.Resize(scaledW, scaledH));
            ctx.DrawImage(clone, new Point(xPos, yPos), 1.0f);
        }
        else if (row.Count == 2)
        {
            Image<Bgra32> leftImg = row[0];
            Image<Bgra32> rightImg = row[1];
            (int minXLeft, int maxXLeft) = bounds[leftImg];
            (int minXRight, int maxXRight) = bounds[rightImg];

            int lW = (int)(leftImg.Width * scale);
            int lH = (int)(leftImg.Height * scale);
            int rW = (int)(rightImg.Width * scale);
            int rH = (int)(rightImg.Height * scale);

            // Left image: visible left edge at canvas 0
            int xLeft = (int)-(minXLeft * scale);

            // Right image: visible right edge at canvas edge
            int xRight = (int)(canvasSize - (maxXRight * scale));

            using Image<Bgra32> cLeft = leftImg.Clone(x => x.Resize(lW, lH));
            ctx.DrawImage(cLeft, new Point(xLeft, yPos), 1.0f);

            using Image<Bgra32> cRight = rightImg.Clone(x => x.Resize(rW, rH));
            ctx.DrawImage(cRight, new Point(xRight, yPos), 1.0f);
        }
    }

    private static float GetMaxOverlapScale(
        Image<Bgra32> imgLeft,
        Image<Bgra32> imgRight,
        int canvasSize,
        Dictionary<Image<Bgra32>, (int MinX, int MaxX)> bounds)
    {
        (int minXLeft, int maxXLeft) = bounds[imgLeft];
        (int minXRight, int maxXRight) = bounds[imgRight];

        float visWidthLeft = maxXLeft - minXLeft;
        float visWidthRight = maxXRight - minXRight;

        // Start where visible bounding boxes exactly touch
        float startScale = canvasSize / (visWidthLeft + visWidthRight);
        if (startScale < 0.1f)
            startScale = 0.1f;

        float currentScale = startScale;
        const float step = 0.02f;
        float maxSafeScale = currentScale;

        while (true)
        {
            float nextScale = currentScale + step;

            // Height check
            if (imgLeft.Height * nextScale > canvasSize || imgRight.Height * nextScale > canvasSize)
                break;

            // Calculate positions at nextScale
            float dX_L = -(minXLeft * nextScale);
            float dX_R = canvasSize - (maxXRight * nextScale);

            // Check collision
            if (CheckCollision(imgLeft, imgRight, nextScale, dX_L, dX_R))
                break;

            maxSafeScale = nextScale;
            currentScale = nextScale;
        }

        return maxSafeScale;
    }

    private static bool CheckCollision(Image<Bgra32> imgA, Image<Bgra32> imgB, float scale, float xA, float xB)
    {
        float wA = imgA.Width * scale;
        float wB = imgB.Width * scale;

        // Check bounding box intersection
        float intersectStart = Math.Max(xA, xB);
        float intersectEnd = Math.Min(xA + wA, xB + wB);

        if (intersectEnd <= intersectStart)
            return false;

        bool collision = false;
        const byte alphaThreshold = 100;

        imgA.ProcessPixelRows(imgB, (accA, accB) =>
        {
            int startX = Math.Max(0, (int)intersectStart);
            int endX = Math.Min(1024, (int)intersectEnd);

            for (int screenX = startX; screenX < endX; screenX++)
            {
                int srcXA = (int)((screenX - xA) / scale);
                int srcXB = (int)((screenX - xB) / scale);

                if (srcXA < 0 || srcXA >= imgA.Width || srcXB < 0 || srcXB >= imgB.Width)
                    continue;

                // Check vertical scanline (bottom-aligned)
                for (int y = 0; y < imgA.Height; y++)
                {
                    int rowA_Index = y;
                    int rowB_Index = y - (imgA.Height - imgB.Height);

                    if (rowB_Index < 0 || rowB_Index >= imgB.Height)
                        continue;

                    Bgra32 pixelA = accA.GetRowSpan(rowA_Index)[srcXA];
                    Bgra32 pixelB = accB.GetRowSpan(rowB_Index)[srcXB];

                    if (pixelA.A > alphaThreshold && pixelB.A > alphaThreshold)
                    {
                        collision = true;
                        break;
                    }
                }

                if (collision)
                    break;
            }
        });

        return collision;
    }

    private static (int MinX, int MaxX) GetVisibleXRange(Image<Bgra32> img)
    {
        int minX = img.Width;
        int maxX = 0;
        bool found = false;

        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < img.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A > 0)
                    {
                        if (x < minX)
                            minX = x;
                        if (x > maxX)
                            maxX = x;
                        found = true;
                    }
                }
            }
        });

        return found ? (minX, maxX) : (0, img.Width);
    }

    /// <summary>
    /// Parses a hex color string (e.g., "#FFFFFFFF" or "#RRGGBBAA") into a Bgra32 color.
    /// </summary>
    private static Bgra32 ParseHexColor(string hexColor, Bgra32 fallback)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
            return fallback;

        string hex = hexColor.TrimStart('#');
        
        // Support both RRGGBB and RRGGBBAA formats
        if (hex.Length == 6)
            hex += "FF"; // Add full opacity if alpha not provided
        
        if (hex.Length != 8)
            return fallback;

        try
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = Convert.ToByte(hex.Substring(6, 2), 16);
            return new Bgra32(r, g, b, a);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Adjusts the brightness of a color by a given factor.
    /// </summary>
    private static Bgra32 AdjustBrightness(Bgra32 color, float delta)
    {
        static byte Clamp(float value) => (byte)Math.Clamp(value, 0, 255);
        float factor = 1 + delta;
        return new Bgra32(
            Clamp(color.R * factor),
            Clamp(color.G * factor),
            Clamp(color.B * factor),
            color.A);
    }

    /// <summary>
    /// Calculates a weighted average between two colors.
    /// </summary>
    private static Bgra32 GetWeightedAverage(Bgra32 colorA, Bgra32 colorB, float weight)
    {
        return new(
            (byte)((colorA.R * weight) + (colorB.R * (1 - weight))),
            (byte)((colorA.G * weight) + (colorB.G * (1 - weight))),
            (byte)((colorA.B * weight) + (colorB.B * (1 - weight))),
            (byte)((colorA.A * weight) + (colorB.A * (1 - weight)))
        );
    }

    /// <summary>
    /// Adds a green channel value to a color, clamping to valid byte range.
    /// </summary>
    private static Bgra32 AddGreenChannel(Bgra32 color, byte greenValue)
    {
        return new(
            (byte)Math.Min(color.R + greenValue, byte.MaxValue),
            (byte)Math.Min(color.G + greenValue, byte.MaxValue),
            (byte)Math.Min(color.B + greenValue, byte.MaxValue),
            color.A);
    }
}