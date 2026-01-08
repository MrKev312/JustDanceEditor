using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.UI.DependencyInjection;
using JustDanceEditor.UI.Helpers;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.UI.Converting;

/// <summary>
/// Orchestrates song conversion workflows with dependency injection.
/// </summary>
public sealed class ConversionWorkflow(
    IKeyedServiceProvider<IJdiFormat> formats,
    ILogger<ConversionWorkflow> logger) : IConversionWorkflow
{
    private readonly IKeyedServiceProvider<IJdiFormat> _formats = formats;
    private readonly ILogger<ConversionWorkflow> _logger = logger;

    public void UpdateCovers()
    {
        try
        {
            if (!CheckTemplate())
                return;

            Console.WriteLine("This tool will update covers and song title logos for maps.");

            int choice = Question.Ask([
                "Update all songs",
                "Update only songs with missing song title logos"
            ], 0, "Please select an update mode:");

            bool updateOnlyMissing = choice == 1;

            string inputFolder = Question.AskFolder("Please enter the path to the folder containing the song folders to update.", true);

            string[] mapFolders = Directory.GetDirectories(inputFolder);

            if (updateOnlyMissing)
            {
                mapFolders = [.. mapFolders.Where(mapFolder => !Directory.Exists(Path.Combine(mapFolder, "songTitleLogo")))];
                Console.WriteLine($"Found {mapFolders.Length} map(s) with missing song title logos.");
            }

            if (mapFolders.Length == 0)
            {
                Console.WriteLine("No map folders to update.");
                return;
            }

            Console.WriteLine($"\nFound {mapFolders.Length} potential map(s). Starting processing...");
            int updatedCovers = 0;
            int updatedLogos = 0;

            Parallel.ForEach(mapFolders, new ParallelOptions { MaxDegreeOfParallelism = 4 }, mapFolder =>
            {
                string mapName = Path.GetFileName(mapFolder);

                int found = 0;

                // Update cover
                using (Image<Rgba32>? coverImage = ImageLoader.TryImageWeb(mapName, "Cover", _logger))
                {
                    if (coverImage is not null)
                    {
                        string templateCoverPath = Directory.GetFiles(Path.Combine("./Template", "Cover"))[0];
                        string outputCoverFolder = Path.Combine(inputFolder, mapName, "Cover");
                        if (Directory.Exists(outputCoverFolder))
                            Directory.Delete(outputCoverFolder, true);
                        UnityCoverRequest coverRequest = new(
                            mapName,
                            null,
                            null,
                            false,
                            templateCoverPath,
                            outputCoverFolder,
                            true,
                            coverImage);
                        CoverBundleBuilder.Generate(coverRequest, _logger);
                        Interlocked.Increment(ref updatedCovers);
                        found++;
                    }
                }

                // Update song title logo
                using Image<Rgba32>? titleLogoImage = ImageLoader.TryImageWeb(mapName, "Title", _logger);
                if (titleLogoImage is not null)
                {
                    string templateLogoPath = Directory.GetFiles(Path.Combine("./Template", "SongTitleLogo"))[0];
                    string outputLogoFolder = Path.Combine(inputFolder, mapName, "songTitleLogo");
                    if (Directory.Exists(outputLogoFolder))
                        Directory.Delete(outputLogoFolder, true);
                    UnitySongTitleRequest titleRequest = new(
                        mapName,
                        null,
                        null,
                        false,
                        templateLogoPath,
                        outputLogoFolder,
                        true,
                        titleLogoImage);
                    SongTitleBundleBuilder.Generate(titleRequest, _logger);
                    Interlocked.Increment(ref updatedLogos);
                    found++;
                }

                if (found == 0)
                    _logger.LogInformation("No online cover or title logo found for map '{MapName}'", mapName);
                else if (found == 2)
                    _logger.LogInformation("Updated both cover and title logo for map '{MapName}'", mapName);
                else
                    _logger.LogWarning("Somehow only one of cover or title logo was updated for map '{MapName}'", mapName);
            });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nUpdate process finished. Updated covers: {updatedCovers}. Updated song title logos: {updatedLogos}.");
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during cover update: {e.Message}");
            Console.ResetColor();
            _logger.LogCritical(e, "Cover update failed: {Message}", e.Message);
        }
    }

    public void ConvertAllSongsInFolder()
    {
        try
        {
            if (!CheckTemplate())
                return;

            Console.WriteLine("Starting batch conversion process for all songs in a folder.");
            string inputFolder = AskMultiInputFolder();
            string outputFolder = AskOutputFolder();
            bool onlineCover = AskOnlineCover();
            List<string> existingSongs = Directory.Exists(outputFolder)
                ? [.. Directory.GetDirectories(outputFolder).Select(Path.GetFileName).Where(name => name is not null).Select(name => name!)]
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

                    Console.WriteLine($"   Converting '{songName}'...");
                    UbiArtConversionRequest importRequest = new(songParentFolder, outputFolder, songName);
                    UnityConversionRequest exportRequest = new(outputFolder, outputFolder, "./Template")
                    {
                        ExportType = ExportType.CustomServer,
                        OnlineCover = onlineCover
                    };
                    RunUbiArtToUnityConversion(importRequest, exportRequest);
                    convertedCount++;
                    Console.WriteLine($"   Conversion of '{songName}' finished.");
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

    private (UbiArtConversionRequest importRequest, UnityConversionRequest exportRequest) CreateUbiArtToUnityRequests()
    {
        (string inputPath, string songName) = AskInputFolder();
        string outputPath = AskOutputFolder();
        bool onlineCover = AskOnlineCover();

        Directory.CreateDirectory(outputPath);

        UbiArtConversionRequest importRequest = new(inputPath, outputPath, songName);
        UnityConversionRequest exportRequest = new(outputPath, outputPath, "./Template")
        {
            ExportType = ExportType.CustomServer,
            OnlineCover = onlineCover
        };

        return (importRequest, exportRequest);
    }

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

    private void RunUbiArtToUnityConversion(UbiArtConversionRequest importRequest, UnityConversionRequest exportRequest)
    {
        IJdiFormat sourceFormat = _formats.Get("UbiArt");
        IJdiFormat targetFormat = _formats.Get("Unity");

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

        maps = maps.Select(Path.GetFileName).ToArray()!;

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

    private static bool AskOnlineCover() =>
        Question.AskYesNo("Do you want to attempt to download cover art from the internet if not found locally?");

    private static string AskOutputFolder() =>
        Question.AskFolder("Please enter the full path for the output folder where converted files will be saved");

    private static bool CheckTemplate()
    {
        Console.WriteLine("Checking for template files...");
        string templateRoot = "./template";
        bool baseFolderMissing = !Directory.Exists(templateRoot);

        string[] requiredSubFolders = [
            "Cover",
            "MapPackage",
            "CoachesLarge",
            "CoachesSmall",
            "songTitleLogo",
        ];

        List<string> missingMessages = [];

        if (baseFolderMissing)
        {
            Directory.CreateDirectory(templateRoot); // Create base if missing
            missingMessages.Add($"Base template folder '{templateRoot}' was missing and has been created.");
            missingMessages.Add("Please populate it with the required template subfolders and files as per documentation.");
        }

        foreach (string subFolder in requiredSubFolders)
        {
            string fullPath = Path.Combine(templateRoot, subFolder);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath); // Create subfolder if missing
                missingMessages.Add($"Template subfolder '{fullPath}' was missing and has been created.");
                missingMessages.Add($"Ensure it contains a valid template bundle file from an official Just Dance Next song.");

            }
            else if (Directory.GetFiles(fullPath).Length == 0)
            {
                missingMessages.Add($"Template subfolder '{fullPath}' is empty. It must contain a template bundle file.");
            }
        }

        if (missingMessages.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n--- Template Setup Incomplete ---");
            foreach (string msg in missingMessages)
            {
                Console.WriteLine(msg);
            }

            Console.WriteLine("\nPlease refer to the README for detailed instructions on template setup.");
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Template files seem to be in place.");
        Console.ResetColor();
        return true;
    }
}