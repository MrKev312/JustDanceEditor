using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Tapes.Clips;
using JustDanceEditor.Formats.UbiArt.Files;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;
using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Images;

public sealed record UbiArtPictoConversionRequest(
    JDUbiArtSong SongData,
    IReadOnlyList<CookedFile> SourceFiles,
    string PictoTempFolder);

public static class UbiArtPictoConverter
{
    static ImageEncoder Encoder => JDI.Utilities.WebpSettings.LosslessWebpEncoder;

    public static void Convert(UbiArtPictoConversionRequest request, ILogger logger, ITextureService textureService, LayeredFileSystem fileSystem, IFileSystem? io = null)
    {
        IFileSystem fs = io ?? new SystemFileSystem();
        ArgumentNullException.ThrowIfNull(request);
        JDUbiArtSong songData = request.SongData ?? throw new ArgumentNullException(nameof(request.SongData));
        ArgumentNullException.ThrowIfNull(request.SourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoTempFolder);

        CookedFile[] pictoFiles = [.. request.SourceFiles];
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

    private static void ProcessAndSaveRawPictoFiles(JDUbiArtSong songData, CookedFile[] pictoFiles, string pictoTempFolder, ILogger logger, ITextureService textureService, IFileSystem fs, LayeredFileSystem fileSystem)
    {
        logger.LogInformation("Processing {Count} pictograms...", pictoFiles.Length);
        Parallel.For(0, pictoFiles.Length, i =>
        {
            CookedFile cooked = pictoFiles[i];
            string baseName = Path.GetFileName(cooked.RelativePath).Split('.')[0];

            try
            {
                using Stream s = fileSystem.GetFileStream(cooked);
                using Image<Bgra32>? pictoImage = textureService.ConvertToImage(s);
                if (pictoImage is null)
                {
                    logger?.LogWarning("Failed to convert pictogram: {Path}", cooked.RelativePath);
                    return;
                }

                if (baseName.Equals("montage", StringComparison.OrdinalIgnoreCase))
                {
                    SplitAndSaveMontageParts(pictoImage!, songData, pictoTempFolder, logger, textureService, fs, fileSystem.VersionProfile.PictoNameComparer);
                }
                else
                {
                    ResizeAndSaveIndividualPicto(pictoImage!, baseName, songData, pictoTempFolder, fs);
                }
            }
            catch (FileNotFoundException)
            {
                logger?.LogWarning("Failed to convert pictogram: {Path} (not found)", cooked.RelativePath);
                return;
            }
        });
        logger.LogInformation("Finished processing pictograms.");
    }

    private static void ResizeAndSaveIndividualPicto(Image<Bgra32> pictoImage, string name, JDUbiArtSong songData, string pictoTempFolder, IFileSystem fs)
    {
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

    private static void SplitAndSaveMontageParts(Image<Bgra32> montageImage, JDUbiArtSong songData, string pictoTempFolder, ILogger logger, ITextureService textureService, IFileSystem fs, IComparer<string>? comparer = null)
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
            ResizeAndSaveIndividualPicto(pictoPart, pictoNamesFromClips[i], songData, pictoTempFolder, fs);
        }

        logger.LogInformation("Finished splitting montage into individual pictos.");
    }
}