using JustDanceEditor.Converter;
using JustDanceEditor.Converter.Converters;
using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI.Converting;
public class ConverterDialogue
{
    public static void ConvertDialogue()
    {
        if (!CheckTemplate())
            return;

        ConversionRequest conversionRequest = CreateConversionRequest();
        Console.WriteLine();

        ConvertUbiArtToUnity converter = new(conversionRequest);
        converter.Convert();
    }

    public static void ConvertDialogueAdvanced()
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
        if (!Directory.Exists("./template"))
        {
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

        string[] folders = [
            "./template/Cover",
            "./template/MapPackage",
            "./template/CoachesLarge",
            "./template/CoachesSmall"
        ];

        // If any of the folders don't exist, create them
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        bool missing = false;
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

        if (missing)
        {
            return false;
        }

        return true;
    }
}
