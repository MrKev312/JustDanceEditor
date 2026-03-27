using JustDanceEditor.Formats.JDI;
using JustDanceEditor.UI.DependencyInjection;
using JustDanceEditor.UI.Helpers;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.UI.Converting;

/// <summary>
/// Orchestrates song conversion workflows with dependency injection.
/// </summary>
public sealed class ConversionWorkflow(
    IKeyedServiceProvider<IJdiFormat> formats,
    IEnumerable<IFormatConversionStrategy> strategies,
    ILogger<ConversionWorkflow> logger) : IConversionWorkflow
{
    private readonly IKeyedServiceProvider<IJdiFormat> _formats = formats;
    private readonly IFormatConversionStrategy[] _strategies = [.. strategies];
    private readonly ILogger<ConversionWorkflow> _logger = logger;

    public void ConvertAllSongsInFolder()
    {
        try
        {
            Console.WriteLine("Starting batch conversion process for all songs in a folder.");

            ConversionTarget target = ConversionTargetSelector.AskTarget(_strategies, "Select the export target for all songs");

            string inputFolder = AskMultiInputFolder();
            string outputFolder = AskOutputFolder();
            List<string> existingSongs = Directory.Exists(outputFolder)
                ? [.. Directory.GetDirectories(outputFolder).Select(Path.GetFileName).OfType<string>()]
                : [];

            string[] inputSongParentFolders = Directory.Exists(Path.Combine(inputFolder, "cache")) && Directory.Exists(Path.Combine(inputFolder, "world"))
                ? [inputFolder] // The provided path is a single song's root
                : Directory.GetDirectories(inputFolder); // The provided path is a parent of multiple song roots

            // Filter out common non-song folders
            string[] ignoreFolders = ["bundle_nx", "patch_nx", "sku_nx"];
            inputSongParentFolders = [.. inputSongParentFolders.Where(x => !ignoreFolders.Any(Path.GetFileName(x).Contains))];

            if (inputSongParentFolders.Length == 0)
            {
                Console.WriteLine("No valid song folders found in the specified input path.");
                return;
            }

            Console.WriteLine($"\nFound {inputSongParentFolders.Length} potential song source(s). Starting processing...");
            int convertedCount = 0;
            int skippedCount = 0;

            foreach (string songParentFolder in inputSongParentFolders)
            {
                Console.WriteLine($"\nProcessing source: {songParentFolder}");
                string inputMapsFolder = Path.Combine(songParentFolder, "world", "maps");

                if (!Directory.Exists(inputMapsFolder))
                {
                    _logger.LogWarning("Skipping '{SongParentFolder}' as it does not contain 'world/maps' subfolder.", songParentFolder);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Skipping '{Path.GetFileName(songParentFolder)}': Missing 'world/maps' subfolder.");
                    Console.ResetColor();
                    skippedCount++;
                    continue;
                }

                string[] songsInSource = Directory.GetDirectories(inputMapsFolder);

                foreach (string songPath in songsInSource)
                {
                    string songName = Path.GetFileName(songPath);
                    Console.WriteLine($"-- Checking song: {songName}");

                    // Basic validation (can be expanded)
                    string platformCacheFolder = Path.Combine(songParentFolder, "cache", "itf_cooked");
                    if (!Directory.Exists(platformCacheFolder))
                    {
                        _logger.LogWarning("Skipping song '{SongName}' in '{Parent}': Missing 'cache/itf_cooked' folder.", songName, songParentFolder);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   Skipped '{songName}': Missing platform cache folder.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }

                    string[] platformDirs = Directory.GetDirectories(platformCacheFolder);
                    if (platformDirs.Length == 0)
                    {
                        _logger.LogWarning("Skipping song '{SongName}' in '{Parent}': No platform found in 'cache/itf_cooked'.", songName, songParentFolder);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   Skipped '{songName}': No platform found in cache.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }
                    // Assuming first platform dir is the one to use, could be more robust
                    string platform = Path.GetFileName(platformDirs[0]);
                    string songDescPath = Path.Combine(platformCacheFolder, platform, "world", "maps", songName, "songdesc.tpl.ckd"); // Common path, can vary

                    if (!File.Exists(songDescPath) && !File.Exists(Path.Combine(songParentFolder, "jddb.json")))
                    {
                        _logger.LogWarning("Skipping song '{SongName}' in '{Parent}': No 'songdesc.tpl.ckd', '{Song}_mainscene.isc.ckd', or 'jddb.json' found.", songName, songParentFolder, songName);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   Skipped '{songName}': Missing essential song description file.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }

                    if (existingSongs.Contains(songName, StringComparer.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Skipping song '{SongName}' as it is already present in the output location.", songName);
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"   Skipped '{songName}': Already exists in output.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }

                    Console.WriteLine($"   Converting '{songName}' to {target.DisplayName}...");

                    try
                    {
                        RunBatchConversion(songParentFolder, outputFolder, songName, target);
                        convertedCount++;
                        Console.WriteLine($"   Conversion of '{songName}' finished.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to convert song '{SongName}': {Message}", songName, ex.Message);
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"   Failed to convert '{songName}': {ex.Message}");
                        Console.ResetColor();
                        skippedCount++;
                    }
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nBatch conversion finished. Converted: {convertedCount} song(s). Skipped: {skippedCount} song(s).");
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during batch conversion: {e.Message}");
            Console.ResetColor();
            _logger.LogCritical(e, "Batch conversion failed: {Message}", e.Message);
        }
    }

    private void RunBatchConversion(string inputFolder, string outputFolder, string songName, ConversionTarget target)
    {
        IFormatConversionStrategy sourceStrategy = GetStrategy("UbiArt");
        IFormatConversionStrategy targetStrategy = GetStrategy(target.FormatName);

        IJdiFormat sourceFormat = _formats.Get(sourceStrategy.FormatName);
        IJdiFormat targetFormat = _formats.Get(targetStrategy.FormatName);

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(inputFolder, outputFolder, songName);
        ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(outputFolder, outputFolder, target, songName);

        JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();

        try
        {
            targetFormat.ExportAsync(importResult, exportRequest).GetAwaiter().GetResult();
        }
        finally
        {
            if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
                Directory.Delete(importResult.MaterializedRoot, true);
        }
    }

    private IFormatConversionStrategy GetStrategy(string formatName) =>
        _strategies.First(strategy => strategy.FormatName.Equals(formatName, StringComparison.OrdinalIgnoreCase));

    private static string AskMultiInputFolder()
    {
        Console.WriteLine("Please provide the path to either:");
        Console.WriteLine("1. A single extracted song folder (containing 'cache' and 'world' subdirectories).");
        Console.WriteLine("2. A parent folder containing multiple such extracted song folders.");
        string inputPath;
        while (true)
        {
            inputPath = Question.AskFolder("Enter the path to the source folder(s)", true);

            if (Directory.Exists(Path.Combine(inputPath, "cache")) && Directory.Exists(Path.Combine(inputPath, "world")))
            {
                break; // Path is a single song's root
            }

            // Check if any subfolders contain cache and world
            if (Directory.Exists(inputPath))
            {
                string[] subFolders = Directory.GetDirectories(inputPath);
                bool found = false;
                foreach (string subFolder in subFolders)
                {
                    if (Directory.Exists(Path.Combine(subFolder, "cache")) && Directory.Exists(Path.Combine(subFolder, "world")))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    break; // Path is a parent of multiple song roots
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid folder structure. Ensure the folder (or its subfolders) contains 'cache' and 'world' directories.");
            Console.ResetColor();
        }

        return inputPath;
    }

    private static (string inputPath, string songName) AskInputFolder()
    {
        string inputPath = "";
        string[] maps = [];

        while (maps.Length == 0)
        {
            inputPath = Question.AskFolder("Please enter the full path to the extracted UbiArt map folder (this folder should contain 'cache' and 'world' subdirectories)", true);
            string mapsPath = Path.Combine(inputPath, "world", "maps");
            if (Directory.Exists(mapsPath))
            {
                maps = Directory.GetDirectories(mapsPath);
            }

            if (maps.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No maps found in 'world/maps' subdirectory. Please check the path.");
                Console.ResetColor();
            }
        }

        maps = [.. maps.Select(Path.GetFileName).OfType<string>()];

        int index = 0;
        if (maps.Length > 1)
        {
            Console.WriteLine("Multiple maps found in the provided folder:");
            index = Question.Ask(maps, 0, "Which map do you want to convert?");
        }
        else if (maps.Length == 1)
        {
            Console.WriteLine($"Selected map: {maps[0]}");
        }

        return (inputPath, maps[index]);
    }

    private static string AskOutputFolder() =>
        Question.AskFolder("Please enter the full path for the output folder where converted files will be saved");
}