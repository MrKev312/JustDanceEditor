using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace JustDanceEditor.Formats.UbiArt.Import.AssetExtraction;

public sealed record UbiArtPictoConversionRequest(
    JDUbiArtSong SongData,
    IReadOnlyList<CookedFile> SourceFiles,
    string PictoTempFolder);

public static class UbiArtPictoConverter
{
    static ImageEncoder Encoder => JDI.Utilities.WebpSettings.LosslessWebpEncoder;

    public static void Convert(UbiArtPictoConversionRequest request, ILogger logger, ITextureService textureService, JustDanceUbiArtFileSystem fileSystem, IFileSystem? io = null)
    {
        IFileSystem fs = io ?? new SystemFileSystem();
        ArgumentNullException.ThrowIfNull(request);
        JDUbiArtSong songData = request.SongData ?? throw new ArgumentException("SongData cannot be null.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.SourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoTempFolder);

        CookedFile[] pictoFiles = DeduplicatePictoSources(request.SourceFiles, logger);
        if (pictoFiles.Length == 0)
        {
            logger.LogWarning("No pictos found in input folder, skipping picto conversion.");
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        ResetDirectory(request.PictoTempFolder, fs);

        logger.LogInformation("Found {Count} cooked pictograms to process.", pictoFiles.Length);
        ProcessAndSaveRawPictoFiles(songData, pictoFiles, request.PictoTempFolder, logger, textureService, fs, fileSystem);

        stopwatch.Stop();
        logger.LogInformation("Finished converting pictos in {ElapsedMs}ms.", stopwatch.ElapsedMilliseconds);
    }

    private static void ResetDirectory(string folder, IFileSystem io)
    {
        if (io.DirectoryExists(folder))
            io.DeleteDirectory(folder, true);
        io.CreateDirectory(folder);
    }

    internal static CookedFile[] DeduplicatePictoSources(IEnumerable<CookedFile> sourceFiles, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);

        List<CookedFile> unique = [];
        HashSet<string> seenOutputNames = new(StringComparer.OrdinalIgnoreCase);
        int duplicateCount = 0;

        foreach (CookedFile file in sourceFiles)
        {
            string outputName = GetOutputName(file);
            if (seenOutputNames.Add(outputName))
            {
                unique.Add(file);
                continue;
            }

            duplicateCount++;
            logger?.LogDebug(
                "Skipping duplicate pictogram source '{Path}' because '{OutputName}.webp' is already queued.",
                file.RelativePath,
                outputName);
        }

        if (duplicateCount > 0)
        {
            logger?.LogInformation(
                "Skipped {DuplicateCount} duplicate pictogram source(s) that would write to an existing output name.",
                duplicateCount);
        }

        return [.. unique];
    }

    private static void ProcessAndSaveRawPictoFiles(JDUbiArtSong songData, CookedFile[] pictoFiles, string pictoTempFolder, ILogger logger, ITextureService textureService, IFileSystem fs, JustDanceUbiArtFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("Processing {Count} pictograms...", pictoFiles.Length);
        ConcurrentDictionary<string, byte> writtenPictograms = new(StringComparer.OrdinalIgnoreCase);
        CookedFile[] montageFiles = [.. pictoFiles.Where(file => IsMontage(file))];
        CookedFile[] individualFiles = [.. pictoFiles.Where(file => !IsMontage(file))];

        Parallel.For(0, individualFiles.Length, i =>
        {
            CookedFile cooked = individualFiles[i];
            string baseName = GetOutputName(cooked);

            try
            {
                using Stream s = fileSystem.GetFileStream(cooked);
                using Image<Bgra32>? pictoImage = textureService.ConvertToImage(s);
                if (pictoImage is null)
                {
                    logger.LogWarning("Failed to convert pictogram: {Path} (texture decoder returned no image)", cooked.RelativePath);
                    return;
                }

                ResizeAndSaveIndividualPicto(pictoImage, baseName, songData, pictoTempFolder, logger, fs, writtenPictograms);
            }
            catch (FileNotFoundException)
            {
                logger.LogWarning("Failed to convert pictogram: {Path} (not found)", cooked.RelativePath);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to convert pictogram: {Path}: {Message}", cooked.RelativePath, ex.Message);
                return;
            }
        });

        foreach (CookedFile montageFile in montageFiles)
        {
            try
            {
                using Stream s = fileSystem.GetFileStream(montageFile);
                using Image<Bgra32>? pictoImage = textureService.ConvertToImage(s);
                if (pictoImage is null)
                {
                    logger.LogWarning("Failed to convert pictogram: {Path} (texture decoder returned no image)", montageFile.RelativePath);
                    continue;
                }

                SplitAndSaveMontageParts(pictoImage, songData, pictoTempFolder, logger, textureService, fs, writtenPictograms, fileSystem.VersionProfile.PictoNameComparer);
            }
            catch (FileNotFoundException)
            {
                logger.LogWarning("Failed to convert pictogram: {Path} (not found)", montageFile.RelativePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to convert pictogram: {Path}: {Message}", montageFile.RelativePath, ex.Message);
            }
        }

        logger.LogInformation("Finished processing pictograms.");
    }

    private static string GetOutputName(CookedFile file) =>
        Path.GetFileName(file.RelativePath).Split('.')[0];

    private static bool IsMontage(CookedFile file) =>
        GetOutputName(file).Equals("montage", StringComparison.OrdinalIgnoreCase);

    private static void ResizeAndSaveIndividualPicto(Image<Bgra32> pictoImage, string name, JDUbiArtSong songData, string pictoTempFolder, ILogger logger, IFileSystem fs, ConcurrentDictionary<string, byte> writtenPictograms)
    {
        if (!writtenPictograms.TryAdd(name, 0))
        {
            logger.LogDebug("Skipping duplicate pictogram output '{Name}.webp'.", name);
            return;
        }

        int coachCount = songData.CoachCount;
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

        pictoImage.Save(fs.Combine(pictoTempFolder, name + ".webp"), Encoder);
    }

    private static void SplitAndSaveMontageParts(Image<Bgra32> montageImage, JDUbiArtSong songData, string pictoTempFolder, ILogger logger, ITextureService textureService, IFileSystem fs, ConcurrentDictionary<string, byte> writtenPictograms, IComparer<string>? comparer = null)
    {
        // Sort pictograms using a comparer supplied by the caller; if none supplied, fall back to
        // the existing AlphanumericTextFirstComparer to preserve historic behaviour.
        IComparer<string> finalComparer = comparer ?? AlphanumericTextFirstComparer.Instance;

        List<string> pictoNamesFromClips = [.. songData.Clips
            .OfType<PictogramClip>()
            .Select(clip => Path.GetFileNameWithoutExtension(clip.PictoPath))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, finalComparer)];

        int pictoCount = pictoNamesFromClips.Count;
        if (pictoCount == 0)
        {
            logger.LogWarning("Montage processing skipped; no pictogram names found in song clips.");
            return;
        }

        int columns = songData.CoachCount == 1 ? 8 : 4;
        int rows = Math.Max(1, (int)Math.Ceiling(pictoCount / (double)columns));

        int montageWidth = montageImage.Width;
        int montageHeight = montageImage.Height;

        if (montageWidth < columns || montageHeight < rows)
        {
            logger.LogError("Montage dimensions too small for expected pictogram grid.");
            return;
        }

        int cellWidth = montageWidth / columns;
        int cellHeight = montageHeight / rows;
        if (cellWidth == 0 || cellHeight == 0)
        {
            logger.LogError("Calculated montage cell dimensions are zero; cannot split montage.");
            return;
        }

        for (int i = 0; i < pictoCount; i++)
        {
            int rowIndex = i / columns;
            int colIndex = i % columns;

            Rectangle cropRectangle = new(colIndex * cellWidth, rowIndex * cellHeight, cellWidth, cellHeight);
            using Image<Bgra32> pictoPart = montageImage.Clone(x => x.Crop(cropRectangle));
            ResizeAndSaveIndividualPicto(pictoPart, pictoNamesFromClips[i], songData, pictoTempFolder, logger, fs, writtenPictograms);
        }

        logger.LogInformation("Finished splitting montage into individual pictos.");
    }
}
