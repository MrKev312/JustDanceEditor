using JustDanceEditor.Converter;
using JustDanceEditor.Converter.Converters;
using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI.Converting;
public class ConverterDialogue
{
    public static void ConvertDialogue()
    {
        if (!Directory.Exists("./template"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("""
				Template folder not found!
				Please put a template in the folder named "template".
				In it should exist out of a "cache0" and a "cachex" folder.
				Place the map files of the template map in the corresponding folders.
				For example, the MapPackage should be in cachex/MapPackage/*.
				""");
            Console.ResetColor();

            return;
        }

        string[] folders = [
            "./template/cache0/Cover",
            "./template/cachex/MapPackage",
            "./template/cachex/CoachesLarge",
            "./template/cachex/CoachesSmall"
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
            return;
        }

        // Ask for the input and output path
        string inputPath = Question.AskFolder("Enter the path to the map folder you want to convert (the one containing cache and world)", true);
        string outputPath = Question.AskFolder("Enter the path to the output folder", false);
        bool onlineCover = Question.AskYesNo("Do you want to look up the cover online if needed?");

        // Create the output folder if it doesn't exist
        Directory.CreateDirectory(outputPath);

        ConversionRequest conversionRequest = new()
        {
            TemplatePath = "./Template",
            InputPath = inputPath,
            OutputPath = outputPath,
            OnlineCover = onlineCover
        };

        ConvertUbiArtToUnity converter = new(conversionRequest);
        converter.Convert();
    }
}
