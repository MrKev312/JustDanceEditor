using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Logging;

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Formats.UbiArt.Images;

public sealed record UbiArtMenuArtConversionRequest(
    IReadOnlyList<CookedFile> MenuArtFiles,
    string OutputFolder);

public static partial class UbiArtMenuArtConverter
{
    public static Task ConvertMenuArtAsync(UbiArtMenuArtConversionRequest request) =>
        Task.Run(() => ConvertMenuArt(request));

    public static void ConvertMenuArt(UbiArtMenuArtConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MenuArtFiles == null)
            throw new ArgumentException("Menu art files must be provided.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);

        Directory.CreateDirectory(request.OutputFolder);
        CookedFile[] orderedFiles = [.. request.MenuArtFiles
            .OrderByDescending(f => f.IsCooked)
            .ThenBy(f => (string)f, StringComparer.OrdinalIgnoreCase)];

        Logger.Log($"Converting {orderedFiles.Length} menu art files...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach (CookedFile file in orderedFiles)
        {
            try
            {
                CookedFile cookedFile = new(file);
                string pngPath = Path.Combine(request.OutputFolder, cookedFile.Name + ".png");
                if (File.Exists(pngPath))
                    continue;

                TextureConverter.TextureConverter.ExtractToPNG(file, pngPath);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to convert menu art file {file}: {ex.Message}", LogLevel.Error);
            }
        }

        NormalizeFilenames(request.OutputFolder);

        stopwatch.Stop();
        Logger.Log($"Finished converting menu art files in {stopwatch.ElapsedMilliseconds}ms");
    }

    private static void NormalizeFilenames(string folder)
    {
        string[] pngFiles = Directory.GetFiles(folder, "*.png");
        foreach (string pngFile in pngFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(pngFile);
            if (CoachPattern().IsMatch(fileName))
            {
                string newFileName = CoachPattern().Replace(fileName, "_Coach_");
                string newFilePath = Path.Combine(folder, newFileName + ".png");
                if (!File.Exists(newFilePath))
                    File.Move(pngFile, newFilePath);
            }

            if (fileName.Contains("_AlbumCoach", StringComparison.OrdinalIgnoreCase) &&
                !fileName.Contains("_Cover_AlbumCoach", StringComparison.OrdinalIgnoreCase))
            {
                string newFileName = fileName.Replace("_AlbumCoach", "_Cover_AlbumCoach", StringComparison.OrdinalIgnoreCase);
                string newFilePath = Path.Combine(folder, newFileName + ".png");
                if (!File.Exists(newFilePath))
                    File.Move(pngFile, newFilePath);
            }
        }
    }

    [GeneratedRegex("_coach_?")]
    private static partial Regex CoachPattern();
}
