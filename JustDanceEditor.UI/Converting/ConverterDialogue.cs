using JustDanceEditor.Converter;
using JustDanceEditor.Converter.Formats;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.Unity;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Logging;
using JustDanceEditor.UI.Helpers;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text.Json;

namespace JustDanceEditor.UI.Converting;

public class ConverterDialogue
{
    public static void ConvertSingleDialogue()
    {
        try
        {
            if (!CheckTemplate())
                return;

            Console.WriteLine("Starting standard conversion process...");
            ConversionRequest conversionRequest = CreateConversionRequest();
            Console.WriteLine("\nProcessing conversion request...");

            RunFormatConversion(conversionRequest);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nConversion completed successfully!");
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during conversion: {e.Message}");
            Console.ResetColor();
            Logger.Log($"Conversion failed: {e.Message}", LogLevel.Fatal);
#if DEBUG
            throw; // In debug mode, rethrow to allow debugging
#endif
        }
    }

    public static void ConvertSingleDialogueAdvanced()
    {
        try
        {
            if (!CheckTemplate())
                return;

            Console.WriteLine("Starting advanced conversion process...");
            ConversionRequest conversionRequest = CreateConversionRequest();

            // Ask for the cache number
            conversionRequest.CacheNumber = (uint)Question.AskNumber("Enter the target cache number for this song (e.g., 1, 123)", 1);

            // Ask for the JD version
            uint version = (uint)Question.AskNumber("Optionally, force a specific JDVersion for compatibility (e.g., 2019, 2022). Enter 0 for automatic detection.", 0);
            conversionRequest.JDVersion = version == 0 ? null : version;

            Console.WriteLine("\nProcessing advanced conversion request...");

            RunFormatConversion(conversionRequest);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nAdvanced conversion completed successfully!");
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during advanced conversion: {e.Message}");
            Console.ResetColor();
            Logger.Log($"Advanced conversion failed: {e.Message}", LogLevel.Fatal);
        }
    }

    public static void UpdateCovers()
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
                using (Image<Rgba32>? coverImage = UnityCoverArtGenerator.TryImageWeb(mapName, "Cover"))
                {
                    if (coverImage is not null)
                    {
                        string templateCoverPath = Directory.GetFiles(Path.Combine("./Template", "Cover"))[0];
                        string outputCoverFolder = Path.Combine(inputFolder, mapName, "Cover");
                        if (Directory.Exists(outputCoverFolder))
                            Directory.Delete(outputCoverFolder, true);
                        UnityCoverBundleRequest coverRequest = new(
                            mapName,
                            coverImage,
                            templateCoverPath,
                            outputCoverFolder,
                            true);
                        CoverBundleBuilder.GenerateCoverBundle(coverRequest);
                        Interlocked.Increment(ref updatedCovers);
                        found++;
                    }
                }

                // Update song title logo
                using Image<Rgba32>? titleLogoImage = UnityCoverArtGenerator.TryImageWeb(mapName, "Title");
                if (titleLogoImage is not null)
                {
                    string templateLogoPath = Directory.GetFiles(Path.Combine("./Template", "SongTitleLogo"))[0];
                    string outputLogoFolder = Path.Combine(inputFolder, mapName, "songTitleLogo");
                    if (Directory.Exists(outputLogoFolder))
                        Directory.Delete(outputLogoFolder, true);
                    UnitySongTitleBundleRequest titleRequest = new(
                        mapName,
                        titleLogoImage,
                        templateLogoPath,
                        outputLogoFolder,
                        true);
                    SongTitleBundleBuilder.GenerateSongTitleLogo(titleRequest);
                    Interlocked.Increment(ref updatedLogos);
                    found++;
                }

                if (found == 0)
                    Logger.Log($"No online cover or title logo found for map '{mapName}'.", LogLevel.Info);
                else if (found == 2)
                    Logger.Log($"Updated both cover and title logo for map '{mapName}'.", LogLevel.Important);
                else
                    Logger.Log($"Somehow only one of cover or title logo was updated for map '{mapName}'.", LogLevel.Warning);
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
            Logger.Log($"Cover update failed: {e.Message}", LogLevel.Fatal);
        }
    }

    public static void ConvertAllSongsInFolder()
    {
        try
        {
            if (!CheckTemplate())
                return;

            Console.WriteLine("Starting batch conversion process for all songs in a folder.");
            string inputFolder = AskMultiInputFolder();
            string outputFolder = AskOutputFolder();
            bool onlineCover = AskOnlineCover();
            ExportType exportType;

            List<string> existingSongs = [];

            // Determine export type and populate existing songs list
            string cacheStatusPath = Path.Combine(outputFolder, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");
            if (File.Exists(cacheStatusPath))
            {
                Console.WriteLine("Detected existing Offline Cache structure in output folder.");
                string json = File.ReadAllText(cacheStatusPath);
                existingSongs = [.. JsonSerializer.Deserialize<JDCacheJSON>(json)!.MapsDict.Select(x => x.Value.SongDatabaseEntry.ParentMapId)];
                exportType = ExportType.OfflineCache;
            }
            else
            {
                Console.WriteLine("Assuming Custom Server export type (no Offline Cache structure found in output folder).");
                exportType = ExportType.CustomServer;
                if (Directory.Exists(outputFolder))
                {
                    string[] outputSongs = Directory.GetDirectories(outputFolder);
                    existingSongs = outputSongs.Select(Path.GetFileName).ToList()!;
                }
            }

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
                    Logger.Log($"Skipping '{songParentFolder}' as it does not contain 'world/maps' subfolder.", LogLevel.Warning);
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
                        Logger.Log($"Skipping song '{songName}' in '{songParentFolder}': Missing 'cache/itf_cooked' folder.", LogLevel.Warning);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   Skipped '{songName}': Missing platform cache folder.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }

                    string[] platformDirs = Directory.GetDirectories(platformCacheFolder);
                    if (platformDirs.Length == 0)
                    {
                        Logger.Log($"Skipping song '{songName}' in '{songParentFolder}': No platform found in 'cache/itf_cooked'.", LogLevel.Warning);
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
                        Logger.Log($"Skipping song '{songName}' in '{songParentFolder}': No 'songdesc.tpl.ckd', '{songName}_mainscene.isc.ckd', or 'jddb.json' found.", LogLevel.Warning);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"   Skipped '{songName}': Missing essential song description file.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }

                    if (existingSongs.Contains(songName, StringComparer.OrdinalIgnoreCase))
                    {
                        Logger.Log($"Skipping song '{songName}' as it is already present in the output location.", LogLevel.Important);
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"   Skipped '{songName}': Already exists in output.");
                        Console.ResetColor();
                        skippedCount++;
                        continue;
                    }

                    Console.WriteLine($"   Converting '{songName}'...");
                    ConversionRequest conversionRequest = new()
                    {
                        TemplatePath = "./Template", // Assuming template is in current dir
                        InputPath = songParentFolder,
                        OutputPath = outputFolder,
                        ExportType = exportType,
                        OnlineCover = onlineCover,
                        SongName = songName
                    };
                    RunFormatConversion(conversionRequest);
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
            Logger.Log($"Batch conversion failed: {e.Message}", LogLevel.Fatal);
        }
    }

    internal static ConversionRequest CreateConversionRequest()
    {
        (string inputPath, string songName) = AskInputFolder();
        string outputPath = AskOutputFolder();
        bool onlineCover = AskOnlineCover();

        Directory.CreateDirectory(outputPath); // Ensure output directory exists

        // Determine export type based on output folder content
        string cacheStatusPath = Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");
        ExportType exportType = File.Exists(cacheStatusPath)
            ? ExportType.OfflineCache
            : ExportType.CustomServer;

        Console.WriteLine(exportType == ExportType.OfflineCache
            ? "Detected Offline Cache structure. Exporting for offline cache."
            : "No Offline Cache structure found. Exporting for custom server.");

        ConversionRequest conversionRequest = new()
        {
            TemplatePath = "./Template", // Assuming template is in current dir
            InputPath = inputPath,
            OutputPath = outputPath,
            ExportType = exportType,
            OnlineCover = onlineCover,
            SongName = songName
        };

        return conversionRequest;
    }

    static string AskMultiInputFolder()
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

    private static void RunFormatConversion(ConversionRequest request)
    {
        FormatConversionService service = new();
        service.ConvertAsync(JdiFormatKind.UbiArt, JdiFormatKind.Unity, request).GetAwaiter().GetResult();
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