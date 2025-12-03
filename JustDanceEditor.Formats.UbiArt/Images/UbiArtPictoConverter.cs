using JustDanceEditor.Formats.UbiArt.Tapes.Clips;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;

namespace JustDanceEditor.Formats.UbiArt.Images;

public sealed record UbiArtPictoConversionRequest(
    JDUbiArtSong SongData,
    IReadOnlyList<string> SourceFiles,
    string PictoTempFolder);

public static class UbiArtPictoConverter
{
    public static void Convert(UbiArtPictoConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JDUbiArtSong songData = request.SongData ?? throw new ArgumentNullException(nameof(request.SongData));
        ArgumentNullException.ThrowIfNull(request.SourceFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PictoTempFolder);

        string[] pictoFiles = [.. request.SourceFiles];
        if (pictoFiles.Length == 0)
        {
            Logger.Log("No pictos found in input folder, skipping picto conversion.", LogLevel.Warning);
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        ResetDirectory(request.PictoTempFolder);

        Logger.Log($"Found {pictoFiles.Length} raw picto source files to process.");
        ProcessAndSaveRawPictoFiles(songData, pictoFiles, request.PictoTempFolder);

        stopwatch.Stop();
        Logger.Log($"Finished converting pictos in {stopwatch.ElapsedMilliseconds}ms.");
    }

    private static void ResetDirectory(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);
    }

    private static void ProcessAndSaveRawPictoFiles(JDUbiArtSong songData, string[] pictoFiles, string pictoTempFolder)
    {
        Logger.Log($"Processing {pictoFiles.Length} raw picto files...");
        Parallel.For(0, pictoFiles.Length, i =>
        {
            string rawPictoPath = pictoFiles[i];
            string baseName = Path.GetFileName(rawPictoPath).Split('.')[0];

            using Image<Bgra32> pictoImage = TextureConverter.TextureConverter.ConvertToImage(rawPictoPath);

            if (baseName.Equals("montage", StringComparison.OrdinalIgnoreCase))
            {
                SplitAndSaveMontageParts(pictoImage, songData, pictoTempFolder);
            }
            else
            {
                ResizeAndSaveIndividualPicto(pictoImage, baseName, songData, pictoTempFolder);
            }
        });
        Logger.Log("Finished processing raw picto files into temporary PNGs.");
    }

    private static void ResizeAndSaveIndividualPicto(Image<Bgra32> pictoImage, string name, JDUbiArtSong songData, string pictoTempFolder)
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

        pictoImage.Save(Path.Combine(pictoTempFolder, name + ".png"));
    }

    private static void SplitAndSaveMontageParts(Image<Bgra32> montageImage, JDUbiArtSong songData, string pictoTempFolder)
    {
        List<string> pictoNamesFromClips = songData.Clips
            .OfType<PictogramClip>()
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

        int columns = songData.CoachCount == 1 ? 8 : 4;
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
            ResizeAndSaveIndividualPicto(pictoPart, pictoNamesFromClips[i], songData, pictoTempFolder);
        }

        Logger.Log("Finished splitting montage into individual pictos.");
    }
}
