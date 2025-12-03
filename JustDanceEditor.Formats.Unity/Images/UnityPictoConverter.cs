using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;

namespace JustDanceEditor.Formats.Unity.Images;

public sealed record UnityPictoConversionRequest(
    UnityExportData UnityData,
    IReadOnlyList<string> SourceFiles,
    string PictoTempFolder,
    string PictoAtlasFolder);

public sealed record UnityPictoConversionResult(
    Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> ImageDictionary,
    List<Image<Rgba32>> AtlasImages);

public static class UnityPictoConverter
{
    public static UnityPictoConversionResult Convert(UnityPictoConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        UnityExportData unityData = request.UnityData ?? throw new ArgumentNullException(nameof(request.UnityData));
        ArgumentNullException.ThrowIfNull(request.SourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoTempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoAtlasFolder);

        string[] pictoFiles = [.. request.SourceFiles];
        if (pictoFiles.Length == 0)
        {
            Logger.Log("No pictos found in input folder, skipping picto conversion.", LogLevel.Warning);
            return new UnityPictoConversionResult([], []);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        ResetDirectory(request.PictoTempFolder);

        Logger.Log($"Found {pictoFiles.Length} raw picto source files to process.");
        ProcessAndSaveRawPictoFiles(unityData, pictoFiles, request.PictoTempFolder);

        string[] convertedPngPaths = Directory.GetFiles(request.PictoTempFolder, "*.png");
        Array.Sort(convertedPngPaths);
        if (convertedPngPaths.Length == 0)
        {
            Logger.Log("No pictos were processed into PNGs; skipping atlas creation.", LogLevel.Warning);
            stopwatch.Stop();
            Logger.Log($"Picto conversion finished early after {stopwatch.ElapsedMilliseconds}ms.");
            return new UnityPictoConversionResult([], []);
        }

        Logger.Log($"Successfully processed raw files into {convertedPngPaths.Length} individual PNG pictos.");

        var (imageDict, atlasPics) = BuildPictoAtlases(convertedPngPaths);
        SaveAtlasImagesToDisk(atlasPics, request.PictoAtlasFolder);

        stopwatch.Stop();
        Logger.Log($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms. Generated {atlasPics.Count} atlas(es).");
        return new UnityPictoConversionResult(imageDict, atlasPics);
    }

    private static void ResetDirectory(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
    }

    private static void ProcessAndSaveRawPictoFiles(UnityExportData unityData, string[] pictoFiles, string pictoTempFolder)
    {
        Logger.Log($"Processing {pictoFiles.Length} raw picto files...");
        Parallel.For(0, pictoFiles.Length, i =>
        {
            string rawPictoPath = pictoFiles[i];
            string baseName = Path.GetFileName(rawPictoPath).Split('.')[0];

            using Image<Bgra32> pictoImage = TextureConverter.TextureConverter.ConvertToImage(rawPictoPath);

            if (baseName.Equals("montage", StringComparison.OrdinalIgnoreCase))
            {
                SplitAndSaveMontageParts(pictoImage, unityData, pictoTempFolder);
            }
            else
            {
                ResizeAndSaveIndividualPicto(pictoImage, baseName, unityData, pictoTempFolder);
            }
        });
        Logger.Log("Finished processing raw picto files into temporary PNGs.");
    }

    private static void ResizeAndSaveIndividualPicto(Image<Bgra32> pictoImage, string name, UnityExportData unityData, string pictoTempFolder)
    {
        int coachCount = unityData.Metadata.CoachCount;
        int targetWidth = 512;
        int targetHeight = coachCount > 1 ? 354 : 512;

        if (pictoImage.Width != targetWidth || pictoImage.Height != targetHeight)
        {
            pictoImage.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Stretch
            }));
        }

        pictoImage.Save(Path.Combine(pictoTempFolder, name + ".png"));
    }

    private static void SplitAndSaveMontageParts(Image<Bgra32> montageImage, UnityExportData unityData, string pictoTempFolder)
    {
        List<string> pictoNamesFromClips = unityData.PictogramClips
            .Select(clip => Path.GetFileNameWithoutExtension(clip.PictoPath))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int pictoCount = pictoNamesFromClips.Count;
        if (pictoCount == 0)
        {
            Logger.Log("Montage processing skipped; no pictogram names found in song clips.", LogLevel.Warning);
            return;
        }

        int columns = unityData.Metadata.CoachCount == 1 ? 8 : 4;
        int rows = Math.Max(1, (int)Math.Ceiling(pictoCount / (double)columns));

        int montageWidth = montageImage.Width;
        int montageHeight = montageImage.Height;

        if (montageWidth < columns || montageHeight < rows)
        {
            Logger.Log("Montage dimensions too small for expected pictogram grid.", LogLevel.Error);
            return;
        }

        int cellWidth = montageWidth / columns;
        int cellHeight = montageHeight / rows;
        if (cellWidth == 0 || cellHeight == 0)
        {
            Logger.Log("Calculated montage cell dimensions are zero; cannot split montage.", LogLevel.Error);
            return;
        }

        for (int i = 0; i < pictoCount; i++)
        {
            int rowIndex = i / columns;
            int colIndex = i % columns;

            Rectangle cropRectangle = new(colIndex * cellWidth, rowIndex * cellHeight, cellWidth, cellHeight);
            using Image<Bgra32> pictoPart = montageImage.Clone(x => x.Crop(cropRectangle));
            ResizeAndSaveIndividualPicto(pictoPart, pictoNamesFromClips[i], unityData, pictoTempFolder);
        }

        Logger.Log("Finished splitting montage into individual pictos.");
    }

    private static (Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> ImageDictionary, List<Image<Rgba32>> AtlasImages)
        BuildPictoAtlases(string[] convertedPictoPngPaths)
    {
        Logger.Log($"Creating atlasses from {convertedPictoPngPaths.Length} PNG pictos...");

        Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> imageDict = new(StringComparer.OrdinalIgnoreCase);
        List<Image<Rgba32>> atlasPics = new();
        Image<Rgba32>? currentAtlasImage = null;

        const int imagesPerAtlas = 16;
        const int atlasDimension = 2048;
        const int pictoCellDimension = 512;

        for (int i = 0; i < convertedPictoPngPaths.Length; i++)
        {
            int indexInCurrentAtlas = i % imagesPerAtlas;

            if (indexInCurrentAtlas == 0)
                currentAtlasImage = new Image<Rgba32>(atlasDimension, atlasDimension);

            if (currentAtlasImage == null)
                throw new InvalidOperationException("Atlas image was not initialized before drawing.");

            string pictoFilePath = convertedPictoPngPaths[i];
            string pictoName = Path.GetFileNameWithoutExtension(pictoFilePath);
            using Image<Rgba32> imageToDraw = Image.Load<Rgba32>(pictoFilePath);

            int gridX = indexInCurrentAtlas % 4;
            int gridY = indexInCurrentAtlas / 4;

            int drawX = gridX * pictoCellDimension;
            int drawY = atlasDimension - (gridY * pictoCellDimension) - pictoCellDimension;
            if (imageToDraw.Height < pictoCellDimension)
                drawY += pictoCellDimension - imageToDraw.Height;

            currentAtlasImage.Mutate(x => x.DrawImage(imageToDraw, new Point(drawX, drawY), 1f));
            int atlasSheetIndex = i / imagesPerAtlas;
            imageDict[pictoName] = (atlasSheetIndex, (imageToDraw.Width, imageToDraw.Height));

            if (indexInCurrentAtlas == imagesPerAtlas - 1 || i == convertedPictoPngPaths.Length - 1)
            {
                atlasPics.Add(currentAtlasImage);
                currentAtlasImage = null;
            }
        }

        Logger.Log($"Finished creating {atlasPics.Count} atlas image(s) in memory.");
        return (imageDict, atlasPics);
    }

    private static void SaveAtlasImagesToDisk(List<Image<Rgba32>> atlasImages, string pictoAtlasTempFolder)
    {
        if (atlasImages.Count == 0)
        {
            Logger.Log("No atlas images to save.", LogLevel.Info);
            return;
        }

        if (Directory.Exists(pictoAtlasTempFolder))
            Directory.Delete(pictoAtlasTempFolder, true);

        Directory.CreateDirectory(pictoAtlasTempFolder);
        for (int i = 0; i < atlasImages.Count; i++)
        {
            string atlasPath = Path.Combine(pictoAtlasTempFolder, $"atlas_{i}.png");
            atlasImages[i].Save(atlasPath);
        }

        Logger.Log($"All atlas images saved successfully to {pictoAtlasTempFolder}.");
    }
}
