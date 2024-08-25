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

            string inputFolder = AskInputFolder();
            string outputFolder = AskOutputFolder();

            // First parse the cachingStatus.json
            JDCacheJSON? cacheJSON = null;

            string cacheStatusPath = Path.Combine(outputFolder, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");
            if (File.Exists(cacheStatusPath))
            {
                string json = File.ReadAllText(cacheStatusPath);
                cacheJSON = JsonSerializer.Deserialize<JDCacheJSON>(json);
            }

            // Get all the songs in the folder
            string inputMapsFolder = Path.Combine(inputFolder, "world", "maps");
            string[] songs = Directory.GetDirectories(inputMapsFolder).Select(Path.GetFileName).ToArray()!;

            bool onlineCover = AskOnlineCover();

            foreach (string song in songs)
            {
                // If the song is already cached, skip it
                if (cacheJSON != null && cacheJSON.MapsDict.Any(x => x.Value.SongDatabaseEntry.MapId.Equals(song, StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.Log($"Skipping {song} as it is already cached", LogLevel.Important);
                    continue;
                }

                ConversionRequest conversionRequest = new()
                {
                    TemplatePath = "./Template",
                    InputPath = Path.Combine(inputFolder),
                    OutputPath = Path.Combine(outputFolder),
                    OnlineCover = onlineCover,
                    SongName = song
                };
                ConvertUbiArtToUnity converter = new(conversionRequest);
                converter.Convert();
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
        (string inputPath, string songName) = AskSpecificInputFolder();

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

    private static string AskInputFolder()
    {
        // Must have a cache and world folder
        string inputPath = "";

        while (!Directory.Exists(Path.Combine(inputPath, "cache")) || !Directory.Exists(Path.Combine(inputPath, "world")))
        {
            inputPath = Question.AskFolder("Enter the path to the folder containing the cache and world folders", true);
        }

        return inputPath;
    }

    private static (string inputPath, string songName) AskSpecificInputFolder()
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
