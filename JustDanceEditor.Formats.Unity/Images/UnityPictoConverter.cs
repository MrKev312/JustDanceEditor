using JustDanceEditor.Formats.Unity.Models;
using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;

namespace JustDanceEditor.Formats.Unity.Images;

public sealed record UnityPictoConversionRequest(
    UnityExportData UnityData,
    IReadOnlyList<string> SourceFiles,
    string PictoAtlasFolder);

public sealed record UnityPictoConversionResult(
    Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> ImageDictionary,
    List<Image<Rgba32>> AtlasImages);

public static class UnityPictoConverter
{
    public static UnityPictoConversionResult Convert(UnityPictoConversionRequest request, Microsoft.Extensions.Logging.ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoAtlasFolder);

        string[] pictoFiles = [.. request.SourceFiles];
        if (pictoFiles.Length == 0)
        {
            logger.LogWarning("No pictos found in input folder, skipping picto conversion.");
            return new UnityPictoConversionResult([], []);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        ResetDirectory(request.PictoAtlasFolder);
        Array.Sort(pictoFiles);

        logger.LogInformation("Found {Count} raw picto source files to process.", pictoFiles.Length);

        (Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)>? imageDict, List<Image<Rgba32>>? atlasPics) = BuildPictoAtlases(pictoFiles, request.UnityData.Metadata.CoachCount > 1, logger);
        SaveAtlasImagesToDisk(atlasPics, request.PictoAtlasFolder, logger);

        stopwatch.Stop();
        logger.LogInformation("Finished converting pictos in {ElapsedMs}ms. Generated {AtlasCount} atlas(es).", stopwatch.ElapsedMilliseconds, atlasPics.Count);
        return new UnityPictoConversionResult(imageDict, atlasPics);
    }

    private static void ResetDirectory(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
    }

    private static void ResizePicto(Image<Rgba32> pictoImage, bool multipleCoaches)
    {
        int targetWidth = 512;
        int targetHeight = multipleCoaches ? 354 : 512;

        if (pictoImage.Width != targetWidth || pictoImage.Height != targetHeight)
        {
            pictoImage.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Max
            }));
        }
    }

    private static (Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> ImageDictionary, List<Image<Rgba32>> AtlasImages)
        BuildPictoAtlases(string[] convertedPictoPngPaths, bool multipleCoaches = false, Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        logger?.LogInformation("Creating atlasses from {Count} PNG pictos...", convertedPictoPngPaths.Length);

        Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> imageDict = new(StringComparer.OrdinalIgnoreCase);
        List<Image<Rgba32>> atlasPics = [];
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
            ResizePicto(imageToDraw, multipleCoaches);

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

        logger?.LogInformation("Finished creating {Count} atlas image(s) in memory.", atlasPics.Count);
        return (imageDict, atlasPics);
    }

    private static void SaveAtlasImagesToDisk(List<Image<Rgba32>> atlasImages, string pictoAtlasTempFolder, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (atlasImages.Count == 0)
        {
            logger.LogInformation("No atlas images to save.");
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

        logger.LogInformation("All atlas images saved successfully to {Folder}.", pictoAtlasTempFolder);
    }
}