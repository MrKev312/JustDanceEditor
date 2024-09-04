using JustDanceEditor.Converter;
using JustDanceEditor.Converter.Converters;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;
using JustDanceEditor.UI.Helpers;

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

            ConversionRequest conversionRequest = CreateConversionRequest();
            Console.WriteLine();

            ConvertUbiArtToUnity converter = new(conversionRequest);
            converter.Convert();
        }
        catch (Exception e)
        {
            Logger.Log(e.Message, LogLevel.Fatal);
            throw;
        }
    }

    public static void ConvertSingleDialogueAdvanced()
    {
        try
        {
            if (!CheckTemplate())
                return;

            ConversionRequest conversionRequest = CreateConversionRequest();

            // Ask for the cache number
            conversionRequest.CacheNumber = (uint)Question.AskNumber("Enter the cache number", 1);

            // Ask for the JD version
            uint version = (uint)Question.AskNumber("Force the JD version (0 for normal)", 0);
            conversionRequest.JDVersion = version == 0 ? null : version;
            Console.WriteLine();

            ConvertUbiArtToUnity converter = new(conversionRequest);
            converter.Convert();
        }
        catch (Exception e)
        {
            Logger.Log(e.Message, LogLevel.Fatal);
            throw;
        }
    }

    public static void ConvertAllSongsInFolder()
    {
        try
        {
            if (!CheckTemplate())
                return;

            string inputFolder = AskMultiInputFolder();
            string outputFolder = AskOutputFolder();

            // First parse the cachingStatus.json
            JDCacheJSON? cacheJSON = null;

            string cacheStatusPath = Path.Combine(outputFolder, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");
            if (File.Exists(cacheStatusPath))
            {
                string json = File.ReadAllText(cacheStatusPath);
                cacheJSON = JsonSerializer.Deserialize<JDCacheJSON>(json);
            }

            bool onlineCover = AskOnlineCover();
            string[] inputFolders = Directory.Exists(Path.Combine(inputFolder, "cache")) && Directory.Exists(Path.Combine(inputFolder, "world"))
                ? [inputFolder]
                : Directory.GetDirectories(inputFolder);

            // Remove bundle_nx and patch_nx folders
            inputFolders = inputFolders.Where(x => !x.Contains("bundle_nx") && !x.Contains("patch_nx")).ToArray();

            foreach (string folder in inputFolders)
            {

                // Get all the songs in the folder
                string inputMapsFolder = Path.Combine(folder, "world", "maps");
                string[] songs = Directory.GetDirectories(inputMapsFolder);

                foreach (string songPath in songs)
                {
                    // Only convert the song if it's a valid song
                    string song = Path.GetFileName(songPath);
                    string platform = Directory.GetDirectories(Path.Combine(folder, "cache", "itf_cooked"))[0];
                    string descPath = Path.Combine(platform, "world", "maps", song, $"{song}_main_scene.isc.ckd");

                    if (!File.Exists(descPath))
                    {
                        Logger.Log($"Skipping {song} as it is not a valid song", LogLevel.Important);
                        continue;
                    }

                    // If the song is already cached, skip it
                    if (cacheJSON != null && cacheJSON.MapsDict.Any(x => x.Value.SongDatabaseEntry.ParentMapId.Equals(song, StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.Log($"Skipping {song} as it is already cached", LogLevel.Important);
                        continue;
                    }

                    ConversionRequest conversionRequest = new()
                    {
                        TemplatePath = "./Template",
                        InputPath = Path.Combine(folder),
                        OutputPath = Path.Combine(outputFolder),
                        OnlineCover = onlineCover,
                        SongName = song
                    };
                    ConvertUbiArtToUnity converter = new(conversionRequest);
                    converter.Convert();
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log(e.Message, LogLevel.Fatal);
            throw;
        }
    }

    private static ConversionRequest CreateConversionRequest()
    {
        (string inputPath, string songName) = AskInputFolder();

        string outputPath = AskOutputFolder();
        bool onlineCover = AskOnlineCover();

        // Create the output folder if it doesn't exist
        Directory.CreateDirectory(outputPath);

        ConversionRequest conversionRequest = new()
        {
            TemplatePath = "./Template",
            InputPath = inputPath,
            OutputPath = outputPath,
            OnlineCover = onlineCover,
            SongName = songName
        };

        return conversionRequest;
    }

    static string AskMultiInputFolder()
    {
        string inputPath;
        while (true)
        {
            inputPath = Question.AskFolder("Enter the path to the folder containing the cache and world folders", true);

            if (Directory.Exists(Path.Combine(inputPath, "cache")) && Directory.Exists(Path.Combine(inputPath, "world")))
            {
                break;
            }

            // Else any of the subfolders has to contain a cache and world folder
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
            {
                break;
            }
        }

        return inputPath;
    }

    private static (string inputPath, string songName) AskInputFolder()
    {
        string inputPath = "";
        string[] maps = [];

        while (maps.Length == 0)
        {
            // Ask for the input and output path
            inputPath = Question.AskFolder("Enter the path to the map folder you want to convert (the one containing cache and world)", true);
            maps = Directory.GetDirectories(Path.Combine(inputPath, "world", "maps"));
        }

        // Foreach map, replace it with the Path.GetFileName of the map
        maps = maps.Select(Path.GetFileName).ToArray()!;

        int index = 0;
        if (maps.Length > 1)
        {
            index = Question.Ask(maps, 0, "Which map do you want to convert");
        }

        return (inputPath, maps[index]);
    }

    private static bool AskOnlineCover() => 
        Question.AskYesNo("Do you want to look up the cover online if needed?");

    private static string AskOutputFolder() => 
        Question.AskFolder("Enter the path to the output folder", false);

    private static bool CheckTemplate()
    {
        bool missing = !Directory.Exists("./template");

        string[] folders = [
            "./template/Cover",
            "./template/MapPackage",
            "./template/CoachesLarge",
            "./template/CoachesSmall",
            "./template/songTitleLogo",
        ];

        // If any of the folders don't exist, create them
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        if (missing)
        {
            Directory.CreateDirectory("./template");

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("""
				Template folder not found!
				Please put a template in the folder named "template".
				Place the map files of the template map in the corresponding folders.
				For example, the MapPackage should be in ./template/MapPackage/*.
				""");
            Console.ResetColor();

            return false;
        }

        missing = false;
        // If any of the folders is empty, ask the user to put the files in the folder
        foreach (string folder in folders)
        {
            if (Directory.GetFiles(folder).Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"The folder {folder} is empty. Please put a template file in the folder.");
                Console.ResetColor();
                missing = true;
            }
        }

        return !missing;
    }
}
