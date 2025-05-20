using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;

namespace JustDanceEditor.Converter.Converters.Images;

public static class PictoConverter
{
    /// <summary>
    /// Converts raw picto files into PNG images and creates atlas images.
    /// </summary>
    public static (Dictionary<string, (int, (int, int))> ImageDictionary, List<Image<Rgba32>> AtlasPics) ConvertPictos(ConversionContext context)
    {
        // Start timing the entire process
        Stopwatch stopwatch = Stopwatch.StartNew();
        Logger.Log("Starting picto conversion process...");

        // Step 1: Initialize paths and retrieve raw picto files.
        // This function gets necessary folder paths and the list of raw picto files.
        // It also performs initial checks, like ensuring pictos exist, and prepares temporary folders.
        (CookedFile[]? pictoFiles, string pictoTempFolder, string pictoAtlasTempFolder) = PreparePictoConversion(context);

        if (pictoFiles == null || pictoFiles.Length == 0)
        {
            // PreparePictoConversion already logged the warning if no files found.
            stopwatch.Stop(); // Stop timer before early exit
            Logger.Log($"Picto conversion skipped as no pictos were found. Time taken: {stopwatch.ElapsedMilliseconds}ms");
            return ([], []);
        }

        Logger.Log($"Found {pictoFiles.Length} raw picto source files to process.");

        // Step 2: Process raw picto files into standardized PNGs.
        // This involves loading textures from raw files, handling 'montage' files by splitting them
        // into individual pictos based on song data, resizing all individual pictos as needed,
        // and saving them as PNG files in a temporary folder.
        ProcessAndSaveRawPictoFiles(context, pictoFiles, pictoTempFolder);

        // Step 3: Collect paths of all generated PNG pictos.
        // After processing, all individual pictos (either from single files or split montages)
        // are now PNGs in the pictoTempFolder. These are the files to be atlassed.
        string[] convertedPngPaths = Directory.GetFiles(pictoTempFolder, "*.png");
        Array.Sort(convertedPngPaths); // Sort for deterministic order in atlas creation.

        if (convertedPngPaths.Length == 0)
        {
            Logger.Log("No pictos were processed into PNGs (e.g., montage splitting yielded no files, or source files were invalid). Skipping atlas creation.", LogLevel.Warning);
            stopwatch.Stop();
            Logger.Log($"Picto conversion finished early. Time taken: {stopwatch.ElapsedMilliseconds}ms");
            return ([], []);
        }

        Logger.Log($"Successfully processed raw files into {convertedPngPaths.Length} individual PNG pictos.");

        // Step 4: Create atlas images from the processed PNGs.
        // This function takes the individual PNG pictos, arranges them into larger atlas sheets (2048x2048),
        // and creates a dictionary mapping picto names to their atlas index and dimensions.
        (Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> imageDict, List<Image<Rgba32>> atlasPics) =
            BuildPictoAtlases(convertedPngPaths);

        // Step 5: Save the generated atlas images to disk.
        // This will take the in-memory atlas images and write them to the designated temporary atlas folder.
        SaveAtlasImagesToDisk(atlasPics, pictoAtlasTempFolder);

        // Stop timing and log completion
        stopwatch.Stop();
        Logger.Log($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms. Generated {atlasPics.Count} atlas(es).");

        // Convert the named tuple from BuildPictoAtlases to the unnamed tuple required by the public API
        var finalImageDict = imageDict.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.AtlasIndex, (kvp.Value.Dimensions.Width, kvp.Value.Dimensions.Height))
        );
        return (finalImageDict, atlasPics);
    }

    /// <summary>
    /// Prepares for picto conversion: gets paths, retrieves raw picto files, and sets up temporary folders.
    /// </summary>
    private static (CookedFile[]? PictoFiles, string PictoTempFolder, string PictoAtlasTempFolder) PreparePictoConversion(ConversionContext context)
    {
        TempFolders tempFolders = context.FileSystem.TempFolders;
        InputFolders inputFolders = context.FileSystem.InputFolders;

        CookedFile[] pictoFiles = [.. context.FileSystem.GetAllFiles(inputFolders.PictosFolder)];

        if (pictoFiles.Length == 0)
        {
            Logger.Log("No pictos found in input folder, skipping picto conversion.", LogLevel.Warning);
            return (null, tempFolders.PictoFolder, tempFolders.PictoAtlasFolder);
        }

        // Ensure the temporary picto folder is clean and exists
        if (Directory.Exists(tempFolders.PictoFolder))
        {
            Directory.Delete(tempFolders.PictoFolder, true); // Delete contents and folder
        }

        Directory.CreateDirectory(tempFolders.PictoFolder); // Recreate folder

        return (pictoFiles, tempFolders.PictoFolder, tempFolders.PictoAtlasFolder);
    }

    /// <summary>
    /// Processes raw picto files (e.g., .tga.ckd), converts them to ImageSharp images,
    /// splits montage files, resizes, and saves them as PNGs in pictoTempFolder.
    /// </summary>
    private static void ProcessAndSaveRawPictoFiles(ConversionContext context, CookedFile[] pictoFiles, string pictoTempFolder)
    {
        Logger.Log($"Processing {pictoFiles.Length} raw picto files...");
        Parallel.For(0, pictoFiles.Length, i =>
        {
            string rawPictoPath = pictoFiles[i];
            // Extracts "name" from "name.ext.ckd" or "name.tga" etc.
            string baseName = Path.GetFileName(rawPictoPath).Split('.')[0];

            using Image<Bgra32> pictoImage = TextureConverter.TextureConverter.ConvertToImage(rawPictoPath);

            if (baseName.Equals("montage", StringComparison.OrdinalIgnoreCase))
            {
                // This specific file is the montage texture. Split it.
                SplitAndSaveMontageParts(pictoImage, baseName, context, pictoTempFolder);
            }
            else
            {
                // This is a regular picto file. Resize and save it as one PNG.
                ResizeAndSaveIndividualPicto(pictoImage, baseName, context, pictoTempFolder);
            }
        });
        Logger.Log("Finished processing raw picto files into temporary PNGs.");
    }

    /// <summary>
    /// Resizes an individual picto image based on coach count and saves it as a PNG.
    /// The input pictoImage is mutated and saved.
    /// </summary>
    private static void ResizeAndSaveIndividualPicto(Image<Bgra32> pictoImage, string name, ConversionContext context, string pictoTempFolder)
    {
        int targetWidth, targetHeight;
        if (context.SongData.CoachCount > 1)
        {
            targetWidth = 512;
            targetHeight = 354;
        }
        else
        {
            targetWidth = 512;
            targetHeight = 512;
        }

        if (pictoImage.Width != targetWidth || pictoImage.Height != targetHeight)
        {
            pictoImage.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Stretch // Original used simple Resize(w,h), assuming stretch is equivalent.
            }));
        }

        pictoImage.Save(Path.Combine(pictoTempFolder, name + ".png"));
    }

    /// <summary>
    /// Splits a montage image into individual pictos based on song data, resizes them, and saves them as PNGs.
    /// The 'montageBaseName' is typically "montage", used for logging.
    /// </summary>
    private static void SplitAndSaveMontageParts(Image<Bgra32> montageImage, string montageBaseName, ConversionContext context, string pictoTempFolder)
    {
        List<string> pictoNamesFromClips = [];
        foreach (PictogramClip clip in context.SongData.Clips.OfType<PictogramClip>())
        {
            string pictoNameForFile = Path.GetFileNameWithoutExtension(clip.PictoPath);
            if (!pictoNamesFromClips.Contains(pictoNameForFile)) // Ensure uniqueness
                pictoNamesFromClips.Add(pictoNameForFile);
        }

        pictoNamesFromClips.Sort(); // Sort alphabetically for consistent processing order
        int pictoCount = pictoNamesFromClips.Count;

        if (pictoCount == 0)
        {
            Logger.Log($"Montage file '{montageBaseName}' processing: No pictogram names found in song clips. Skipping montage splitting.", LogLevel.Warning);
            return;
        }

        int columns = context.SongData.CoachCount == 1 ? 8 : 4;
        int rows = 1;
        while (columns * rows < pictoCount)
        {
            rows++;
        }

        int montageWidth = montageImage.Width;
        int montageHeight = montageImage.Height;

        if (montageWidth < columns || montageHeight < rows || columns == 0 || rows == 0)
        {
            Logger.Log($"Montage image '{montageBaseName}' dimensions ({montageWidth}x{montageHeight}) or grid ({columns}x{rows}) are invalid for splitting. Required {pictoCount} pictos. Cannot split.", LogLevel.Error);
            return;
        }

        int cellWidth = montageWidth / columns;
        int cellHeight = montageHeight / rows;

        if (cellWidth == 0 || cellHeight == 0)
        {
            Logger.Log($"Calculated cell dimensions for montage '{montageBaseName}' are zero ({cellWidth}x{cellHeight}) from source {montageWidth}x{montageHeight} and grid {columns}x{rows}. Cannot split.", LogLevel.Error);
            return;
        }

        Logger.Log($"Splitting montage '{montageBaseName}' ({montageWidth}x{montageHeight}) into {pictoCount} parts based on a {columns}x{rows} grid. Each cell: {cellWidth}x{cellHeight}.");

        for (int i = 0; i < pictoCount; i++)
        {
            int rowIndexInGrid = i / columns;
            int colIndexInGrid = i % columns;

            Rectangle cropRectangle = new(colIndexInGrid * cellWidth, rowIndexInGrid * cellHeight, cellWidth, cellHeight);

            // Clone the specific part from the montage for processing
            using Image<Bgra32> pictoPart = montageImage.Clone(x => x.Crop(cropRectangle));

            // Resize the extracted part to standard picto dimensions and save it
            ResizeAndSaveIndividualPicto(pictoPart, pictoNamesFromClips[i], context, pictoTempFolder);
        }

        Logger.Log($"Finished splitting montage '{montageBaseName}'.");
    }

    /// <summary>
    /// Builds atlas images from individual PNG pictos and creates a mapping dictionary.
    /// </summary>
    private static (Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> ImageDictionary, List<Image<Rgba32>> AtlasImages)
        BuildPictoAtlases(string[] convertedPictoPngPaths)
    {
        Logger.Log($"Creating atlasses from {convertedPictoPngPaths.Length} PNG pictos...");

        Dictionary<string, (int AtlasIndex, (int Width, int Height) Dimensions)> imageDict = [];
        List<Image<Rgba32>> atlasPics = [];
        Image<Rgba32>? currentAtlasImage = null;

        const int imagesPerAtlas = 16; // 4x4 grid
        const int atlasDimension = 2048; // Atlas is 2048x2048
        const int pictoCellDimension = 512; // Each cell in atlas is conceptually 512x512

        for (int i = 0; i < convertedPictoPngPaths.Length; i++)
        {
            int indexInCurrentAtlas = i % imagesPerAtlas;

            if (indexInCurrentAtlas == 0) // Start of a new atlas sheet
            {
                // The previous atlas (if any) would have been added to atlasPics in the last iteration's check.
                currentAtlasImage = new Image<Rgba32>(atlasDimension, atlasDimension); // Default Rgba32 is transparent black
            }

            // This should ideally not be null if convertedPictoPngPaths is not empty, due to above assignment.
            if (currentAtlasImage == null)
            {
                Logger.Log($"Critical Error: currentAtlasImage is null when processing picto {i}. This indicates a logic flaw.", LogLevel.Error);
                break; // Cannot continue without an atlas to draw on.
            }

            string pictoFilePath = convertedPictoPngPaths[i];
            string pictoName = Path.GetFileNameWithoutExtension(pictoFilePath);

            using Image<Rgba32> imageToDraw = Image.Load<Rgba32>(pictoFilePath);

            // Calculate position in the 4x4 grid of the current atlas.
            // Original logic fills atlas rows from the bottom of the atlas image upwards.
            int grid_x_pos = indexInCurrentAtlas % 4; // Column in 4x4 grid (0-3)
            int grid_y_pos = indexInCurrentAtlas / 4; // Row in 4x4 grid (0-3), relative to atlas filling order

            // Top-left X coordinate for drawing on atlas
            int drawX = grid_x_pos * pictoCellDimension;

            // Top-left Y coordinate for drawing on atlas (ImageSharp Y increases downwards).
            // The original formula `y_coord = 2048 - (grid_y_pos * 512) - 512` calculates the Y for the top of a cell
            // when cells are filled starting from the bottom row of the atlas.
            int drawY_cellTop = atlasDimension - (grid_y_pos * pictoCellDimension) - pictoCellDimension;

            // If the image is shorter than the cell, adjust Y to align the image's bottom with the cell's conceptual bottom.
            if (imageToDraw.Height < pictoCellDimension)
            {
                drawY_cellTop += pictoCellDimension - imageToDraw.Height;
            }

            currentAtlasImage.Mutate(x => x.DrawImage(imageToDraw, new Point(drawX, drawY_cellTop), 1f));

            int atlasSheetIndex = i / imagesPerAtlas;
            imageDict.Add(pictoName, (atlasSheetIndex, (imageToDraw.Width, imageToDraw.Height)));

            if (indexInCurrentAtlas == imagesPerAtlas - 1 || i == convertedPictoPngPaths.Length - 1)
            {
                // Current atlas is full, or this is the last picto overall.
                atlasPics.Add(currentAtlasImage);
                currentAtlasImage = null; // Mark as added; new one will be created if loop continues.
            }
        }

        Logger.Log($"Finished creating {atlasPics.Count} atlas image(s) in memory.");
        return (imageDict, atlasPics);
    }

    /// <summary>
    /// Saves the generated atlas images to disk in the specified folder.
    /// </summary>
    private static void SaveAtlasImagesToDisk(List<Image<Rgba32>> atlasImages, string pictoAtlasTempFolder)
    {
        if (atlasImages.Count == 0)
        {
            Logger.Log("No atlas images to save.", LogLevel.Info);
            return;
        }

        Logger.Log($"Saving {atlasImages.Count} atlas image(s) to {pictoAtlasTempFolder}...");
        if (Directory.Exists(pictoAtlasTempFolder))
        {
            Directory.Delete(pictoAtlasTempFolder, true); // Clean up old atlases
        }

        Directory.CreateDirectory(pictoAtlasTempFolder); // Ensure directory exists

        for (int i = 0; i < atlasImages.Count; i++)
        {
            string atlasPath = Path.Combine(pictoAtlasTempFolder, $"atlas_{i}.png");
            atlasImages[i].Save(atlasPath);
            // Logger.Log($"Saved atlas: {atlasPath}"); // This can be too verbose for many atlases.
        }

        Logger.Log($"All atlas images saved successfully to {pictoAtlasTempFolder}.");
    }
}