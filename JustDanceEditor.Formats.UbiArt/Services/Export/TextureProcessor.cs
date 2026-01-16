using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

public partial class TextureProcessor : ITextureProcessor
{
    public IEnumerable<ProcessedTexture> ProcessAssets(IntermediateSongPackage package, string materializedRoot, IFileSystem io)
    {
        string mapNameLower = package.Metadata.MapName.ToLowerInvariant();
        List<ProcessedTexture> results = [];

        // --- 1. Load Source Images ---

        // Coach Images
        string coachesDir = io.Combine(materializedRoot, "assets", "coaches");
        Dictionary<int, Image<Bgra32>> coachImages = [];
        Image<Bgra32>? coachBackground = null;

        if (io.DirectoryExists(coachesDir))
        {
            foreach (string file in io.GetFiles(coachesDir))
            {
                string fName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

                // Ignore logos immediately
                if (fName.Contains("logo") || fName.Contains("title"))
                    continue;

                if (fName == "coachesbackground")
                {
                    coachBackground = Image.Load<Bgra32>(file);
                    continue;
                }

                // Match "coach_1", "coach_01", etc.
                Match match = CoachRegex().Match(fName);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int id))
                {
                    // Resize source coach to standard 1024x1024 immediately to normalize processing
                    Image<Bgra32> img = Image.Load<Bgra32>(file);
                    img.Mutate(x => x.Resize(1024, 1024));
                    coachImages[id] = img;
                }
            }
        }

        // Cover Image
        string coverDir = io.Combine(materializedRoot, "assets", "coverAssets");
        Image<Bgra32>? genericCover = null;

        if (io.DirectoryExists(coverDir))
        {
            // Look for "cover.webp/png/jpg"
            string? coverFile = io.GetFiles(coverDir)
                .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("cover", StringComparison.OrdinalIgnoreCase));

            if (coverFile != null)
            {
                genericCover = Image.Load<Bgra32>(coverFile);
            }
        }

        // --- 2. Generate Derived Assets ---

        // A. Coach Textures (1024x1024)
        foreach (KeyValuePair<int, Image<Bgra32>> kvp in coachImages)
        {
            // Already resized on load, clone to avoid disposal issues if reused
            results.Add(new ProcessedTexture($"{mapNameLower}_coach_{kvp.Key}", kvp.Value.Clone()));
        }

        // B. Cover Generic (512x512)
        if (genericCover != null)
        {
            Image<Bgra32> cover512 = genericCover.Clone(x => x.Resize(512, 512));
            results.Add(new ProcessedTexture($"{mapNameLower}_cover_generic", cover512));

            // C. Cover Online / Kids / AlbumBkg (256x256) - Generated from Generic
            Image<Bgra32> cover256 = genericCover.Clone(x => x.Resize(256, 256));
            results.Add(new ProcessedTexture($"{mapNameLower}_cover_online", cover256.Clone()));
            results.Add(new ProcessedTexture($"{mapNameLower}_cover_online_kids", cover256.Clone()));
            results.Add(new ProcessedTexture($"{mapNameLower}_cover_albumbkg", cover256.Clone()));

            // Clean up intermediate
            cover256.Dispose();
        }

        // D. Map Background (2048x1024) & Banner (1024x512)
        if (coachBackground != null)
        {
            Image<Bgra32> mapBkg = coachBackground.Clone(x => x.Resize(2048, 1024));
            results.Add(new ProcessedTexture($"{mapNameLower}_map_bkg", mapBkg));

            Image<Bgra32> banner = coachBackground.Clone(x => x.Resize(1024, 512));
            results.Add(new ProcessedTexture($"{mapNameLower}_banner_bkg", banner));
        }

        // E. Album Coach (1024x1024 Composite)
        if (coachImages.Count > 0)
        {
            // Convert dictionary to list sorted by ID
            List<Image<Bgra32>> sortedCoaches = [.. coachImages.OrderBy(x => x.Key).Select(x => x.Value)];
            Image<Bgra32> albumCoach = GenerateAlbumCoach(sortedCoaches);
            results.Add(new ProcessedTexture($"{mapNameLower}_cover_albumcoach", albumCoach));
        }

        // Cleanup Sources
        genericCover?.Dispose();
        coachBackground?.Dispose();
        foreach (Image<Bgra32> img in coachImages.Values)
            img.Dispose();

        return results;
    }

    public Image<Bgra32> GenerateAlbumCoach(List<Image<Bgra32>> coaches)
    {
        const int CanvasSize = 1024;
        Image<Bgra32> canvas = new(CanvasSize, CanvasSize);
        int count = coaches.Count;

        if (count == 0)
            return canvas;

        // 1. Pre-calculate Visible Bounding Boxes (MinX, MaxX) for horizontal fitting
        // We assume the vertical content usually spans most of the height or we align bottom.
        Dictionary<Image<Bgra32>, (int MinX, int MaxX)> bounds = [];
        foreach (Image<Bgra32> coach in coaches)
        {
            bounds[coach] = GetVisibleXRange(coach);
        }

        // 2. Define Rows
        List<Image<Bgra32>> backRow = [];
        List<Image<Bgra32>> frontRow = [];

        if (count == 1)
        {
            // Special case: Single image centered
            Image<Bgra32> img = coaches[0];
            // Scale to fit height 1024
            float s = 1024f / img.Height;
            // Or keep 1.0 if it's already 1024? User prompt implies filling space.
            // Let's assume max height 1024.
            canvas.Mutate(ctx => DrawRow(ctx, [img], s, false, bounds));
            return canvas;
        }
        else if (count == 2)
        {
            // 2 Side by Side (Treat as one back row for scaling logic)
            backRow.Add(coaches[0]);
            backRow.Add(coaches[1]);
        }
        else if (count == 3)
        {
            // 2 Back, 1 Front
            backRow.Add(coaches[0]);
            backRow.Add(coaches[2]);
            frontRow.Add(coaches[1]);
        }
        else // >= 4
        {
            // 2 Back, 2 Front
            backRow.Add(coaches[0]);
            backRow.Add(coaches[3]);
            frontRow.Add(coaches[1]);
            frontRow.Add(coaches[2]);
        }

        // 3. Calculate Maximum Valid Scale
        // We calculate the max safe scale for the back row and front row independently,
        // then take the smaller one to ensure uniformity.

        float maxScaleBack = 100f; // Start high
        float maxScaleFront = 100f;

        if (backRow.Count == 2)
        {
            maxScaleBack = GetMaxOverlapScale(backRow[0], backRow[1], CanvasSize, bounds);
        }

        if (frontRow.Count == 2)
        {
            maxScaleFront = GetMaxOverlapScale(frontRow[0], frontRow[1], CanvasSize, bounds);
        }

        // If a row has only 1 image (e.g. 3 coaches -> front row is 1), 
        // it shouldn't constrain the scale based on collision.
        // We default to the other row's scale, or a height-based limit.

        float finalScale;
        if (count == 2)
            finalScale = maxScaleBack;
        else if (count == 3)
            finalScale = maxScaleBack; // Front is single, governed by back row sizing
        else
            finalScale = Math.Min(maxScaleBack, maxScaleFront);

        // Hard limit: Images cannot be taller than 1024 pixels relative to canvas
        // (Though they might be pushed down, the prompt says "height is at most 1024")
        // Check all images to ensure none exceed 1024 height at this scale
        foreach (Image<Bgra32> c in coaches)
        {
            float hLimit = 1024f / c.Height;
            if (finalScale > hLimit)
                finalScale = hLimit;
        }

        // 4. Draw
        canvas.Mutate(ctx =>
        {
            // Draw Back Row
            DrawRow(ctx, backRow, finalScale, isFrontRow: false, bounds);

            // Draw Front Row
            DrawRow(ctx, frontRow, finalScale, isFrontRow: true, bounds);
        });

        return canvas;
    }

    private void DrawRow(IImageProcessingContext ctx, List<Image<Bgra32>> row, float scale, bool isFrontRow, Dictionary<Image<Bgra32>, (int MinX, int MaxX)> bounds)
    {
        if (row.Count == 0)
            return;

        int canvasH = 1024;
        int canvasW = 1024;

        // Calculate Y
        // Base: Touch bottom.
        int scaledH = (int)(row[0].Height * scale);
        int yPos = canvasH - scaledH;

        // If front row, move down by 1/3 of height (legs cut off, faces of back visible)
        if (isFrontRow)
        {
            yPos += scaledH / 3;
        }

        if (row.Count == 1)
        {
            // Center
            Image<Bgra32> img = row[0];
            int scaledW = (int)(img.Width * scale);
            int xPos = (canvasW - scaledW) / 2;

            using Image<Bgra32> clone = img.Clone(x => x.Resize(scaledW, scaledH));
            ctx.DrawImage(clone, new Point(xPos, yPos), 1.0f);
        }
        else if (row.Count == 2)
        {
            Image<Bgra32> leftImg = row[0];
            Image<Bgra32> rightImg = row[1];
            (int MinX, int MaxX) = bounds[leftImg];
            (int MinX, int MaxX) bRight = bounds[rightImg];

            int lW = (int)(leftImg.Width * scale);
            int lH = (int)(leftImg.Height * scale);
            int rW = (int)(rightImg.Width * scale);
            int rH = (int)(rightImg.Height * scale);

            // Position Logic: "Outer edge touching the side of the space"
            // Left Image: Visible Left (MinX) must be at Canvas 0.
            // DrawX = 0 - (MinX * scale)
            int xLeft = (int)-(MinX * scale);

            // Right Image: Visible Right (MaxX) must be at Canvas 1024.
            // DrawX + (MaxX * scale) = 1024
            // DrawX = 1024 - (MaxX * scale)
            int xRight = (int)(canvasW - (bRight.MaxX * scale));

            using Image<Bgra32> cLeft = leftImg.Clone(x => x.Resize(lW, lH));
            ctx.DrawImage(cLeft, new Point(xLeft, yPos), 1.0f);

            using Image<Bgra32> cRight = rightImg.Clone(x => x.Resize(rW, rH));
            ctx.DrawImage(cRight, new Point(xRight, yPos), 1.0f);
        }
    }

    private float GetMaxOverlapScale(Image<Bgra32> imgLeft, Image<Bgra32> imgRight, int canvasSize, Dictionary<Image<Bgra32>, (int MinX, int MaxX)> bounds)
    {
        (int MinX, int MaxX) = bounds[imgLeft];
        (int MinX, int MaxX) bRight = bounds[imgRight];

        float visWidthLeft = MaxX - MinX;
        float visWidthRight = bRight.MaxX - bRight.MinX;

        // 1. Calculate the scale where the visible Bounding Boxes exactly touch.
        // Canvas = (VisWidthLeft * s) + (VisWidthRight * s)
        // s = Canvas / (TotalVisWidth)
        float startScale = canvasSize / (visWidthLeft + visWidthRight);

        // Safety: ensure we don't start infinitely small
        if (startScale < 0.1f)
            startScale = 0.1f;

        float currentScale = startScale;
        float step = 0.02f;
        float maxSafeScale = currentScale;

        // Loop until collision or height limit
        while (true)
        {
            // Propose next scale
            float nextScale = currentScale + step;

            // Height check (Must be <= 1024)
            if (imgLeft.Height * nextScale > canvasSize || imgRight.Height * nextScale > canvasSize)
                break;

            // Calculate positions at nextScale
            // Left draw pos: shifts MinX to 0
            float dX_L = -(MinX * nextScale);
            // Right draw pos: shifts MaxX to 1024
            float dX_R = canvasSize - (bRight.MaxX * nextScale);

            // Check Collision
            if (CheckCollision(imgLeft, imgRight, nextScale, dX_L, dX_R))
            {
                // Collision detected, stop.
                break;
            }

            // No collision, accept this scale and continue growing
            maxSafeScale = nextScale;
            currentScale = nextScale;
        }

        return maxSafeScale;
    }

    private bool CheckCollision(Image<Bgra32> imgA, Image<Bgra32> imgB, float scale, float xA, float xB)
    {
        // Screen space rectangles
        float wA = imgA.Width * scale;
        float wB = imgB.Width * scale;

        // Intersection of [xA, xA + wA] and [xB, xB + wB]
        float intersectStart = Math.Max(xA, xB);
        float intersectEnd = Math.Min(xA + wA, xB + wB);

        if (intersectEnd <= intersectStart)
            return false; // No bounding box overlap

        // Check pixels in the intersection strip
        bool collision = false;
        const byte alphaThreshold = 100;

        // Map screen X back to source X
        // SrcX = (ScreenX - DrawX) / Scale

        imgA.ProcessPixelRows(imgB, (accA, accB) =>
        {
            // Iterate relevant columns in screen space
            // Optimization: Step by 1 pixel in screen space
            int startX = (int)intersectStart;
            int endX = (int)intersectEnd;

            // Ensure we are within canvas bounds (0-1024) - strictly the intersection implies valid range if images are on screen
            // But let's be safe
            if (startX < 0)
                startX = 0;
            if (endX > 1024)
                endX = 1024;

            for (int screenX = startX; screenX < endX; screenX++)
            {
                int srcXA = (int)((screenX - xA) / scale);
                int srcXB = (int)((screenX - xB) / scale);

                if (srcXA < 0 || srcXA >= imgA.Width || srcXB < 0 || srcXB >= imgB.Width)
                    continue;

                // Check vertical scanline
                for (int y = 0; y < imgA.Height; y++)
                {
                    // We assume images are bottom-aligned relative to each other (same Y plane).
                    // If heights differ, alignment is bottom.
                    // However, DrawRow aligns them to the bottom of the canvas. 
                    // Since `y` in GetRowSpan is from top, we must align the *bottoms* of the source images.
                    // SrcY_B maps to SrcY_A?
                    // Let's assume for collision check they are drawn at the same Y visually.
                    // If imgA.Height != imgB.Height, the bottom pixels align.

                    int rowA_Index = y;

                    // If A is 1000px and B is 800px.
                    // Bottom of A is at y=999. Bottom of B is at y=799.
                    // If we are at the bottom-most pixel of the composition:
                    // We need pixel 999 from A and pixel 799 from B.
                    // Formula: yB = yA - (HeightA - HeightB).

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

    // Helper to find the "content" bounding box horizontally
    private (int MinX, int MaxX) GetVisibleXRange(Image<Bgra32> img)
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

        if (!found)
            return (0, img.Width); // Fallback for empty image
        return (minX, maxX);
    }

    [GeneratedRegex("coach_(\\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CoachRegex();
}