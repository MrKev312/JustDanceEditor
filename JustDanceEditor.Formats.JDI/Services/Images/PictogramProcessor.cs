using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.JDI.Services.Images;

/// <summary>
/// Provides pictogram image processing utilities.
/// </summary>
public static class PictogramProcessor
{
    /// <summary>
    /// Standard pictogram width.
    /// </summary>
    public const int PictogramWidth = 512;

    /// <summary>
    /// Standard pictogram height for solo choreographies.
    /// </summary>
    public const int SoloPictogramHeight = 512;

    /// <summary>
    /// Standard pictogram height for duo/trio/quad choreographies.
    /// </summary>
    public const int MultiPictogramHeight = 354;

    /// <summary>
    /// Resizes a pictogram to the standard dimensions based on coach count.
    /// </summary>
    /// <param name="pictogram">The pictogram image to resize.</param>
    /// <param name="coachCount">Number of coaches in the choreography.</param>
    /// <returns>The resized pictogram (mutates the original).</returns>
    public static Image<Bgra32> ResizeToStandard(Image<Bgra32> pictogram, int coachCount)
    {
        ArgumentNullException.ThrowIfNull(pictogram);

        int targetHeight = coachCount > 1 ? MultiPictogramHeight : SoloPictogramHeight;

        if (pictogram.Width != PictogramWidth || pictogram.Height != targetHeight)
        {
            pictogram.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(PictogramWidth, targetHeight),
                Mode = ResizeMode.Stretch
            }));
        }

        return pictogram;
    }

    /// <summary>
    /// Splits a montage image into individual pictograms.
    /// </summary>
    /// <param name="montage">The montage image containing multiple pictograms.</param>
    /// <param name="pictogramNames">Ordered list of pictogram names corresponding to grid positions.</param>
    /// <param name="coachCount">Number of coaches (affects grid layout).</param>
    /// <returns>Dictionary mapping pictogram names to their extracted images.</returns>
    public static Dictionary<string, Image<Bgra32>> SplitMontage(
        Image<Bgra32> montage,
        IReadOnlyList<string> pictogramNames,
        int coachCount)
    {
        ArgumentNullException.ThrowIfNull(montage);
        ArgumentNullException.ThrowIfNull(pictogramNames);

        Dictionary<string, Image<Bgra32>> result = new(StringComparer.OrdinalIgnoreCase);

        if (pictogramNames.Count == 0)
            return result;

        // Determine grid layout based on coach count
        int columns = coachCount == 1 ? 8 : 4;
        int rows = Math.Max(1, (int)Math.Ceiling(pictogramNames.Count / (double)columns));

        int montageWidth = montage.Width;
        int montageHeight = montage.Height;

        if (montageWidth < columns || montageHeight < rows)
            return result;

        int cellWidth = montageWidth / columns;
        int cellHeight = montageHeight / rows;

        if (cellWidth == 0 || cellHeight == 0)
            return result;

        int targetHeight = coachCount > 1 ? MultiPictogramHeight : SoloPictogramHeight;

        for (int i = 0; i < pictogramNames.Count; i++)
        {
            int rowIndex = i / columns;
            int colIndex = i % columns;

            Rectangle cropRectangle = new(colIndex * cellWidth, rowIndex * cellHeight, cellWidth, cellHeight);
            Image<Bgra32> pictoPart = montage.Clone(x => x.Crop(cropRectangle));

            // Resize to standard dimensions
            if (pictoPart.Width != PictogramWidth || pictoPart.Height != targetHeight)
            {
                pictoPart.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(PictogramWidth, targetHeight),
                    Mode = ResizeMode.Stretch
                }));
            }

            result[pictogramNames[i]] = pictoPart;
        }

        return result;
    }

    /// <summary>
    /// Creates a pictogram atlas from individual pictograms.
    /// </summary>
    /// <param name="pictograms">Dictionary of pictogram ID to image.</param>
    /// <param name="atlasSize">Size of the atlas (default 2048x2048).</param>
    /// <param name="pictosPerAtlas">Number of pictograms per atlas (default 16).</param>
    /// <returns>List of atlas images.</returns>
    public static List<Image<Bgra32>> CreateAtlases(
        IReadOnlyDictionary<string, Image<Bgra32>> pictograms,
        int atlasSize = 2048,
        int pictosPerAtlas = 16)
    {
        ArgumentNullException.ThrowIfNull(pictograms);

        List<Image<Bgra32>> atlases = [];
        List<string> pictoKeys = [.. pictograms.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)];

        int pictosPerRow = (int)Math.Sqrt(pictosPerAtlas);
        int cellSize = atlasSize / pictosPerRow;

        for (int atlasIndex = 0; atlasIndex < Math.Ceiling(pictoKeys.Count / (double)pictosPerAtlas); atlasIndex++)
        {
            Image<Bgra32> atlas = new(atlasSize, atlasSize);

            int startIndex = atlasIndex * pictosPerAtlas;
            int endIndex = Math.Min(startIndex + pictosPerAtlas, pictoKeys.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                int localIndex = i - startIndex;
                int row = localIndex / pictosPerRow;
                int col = localIndex % pictosPerRow;

                Image<Bgra32> picto = pictograms[pictoKeys[i]];
                using Image<Bgra32> resized = picto.Clone();
                resized.Mutate(x => x.Resize(cellSize, cellSize));

                atlas.Mutate(x => x.DrawImage(resized, new Point(col * cellSize, row * cellSize), 1f));
            }

            atlases.Add(atlas);
        }

        return atlases;
    }
}