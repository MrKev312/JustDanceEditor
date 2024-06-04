using JustDanceEditor.Converter;
using JustDanceEditor.Converter.Converters;
using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI.Converting;
internal class ConverterDialogue
{
    public static void ConvertDialogue()
    {
        // Check if there's a template folder
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

            Directory.CreateDirectory("./template");
            Directory.CreateDirectory("./template/cache0");
            Directory.CreateDirectory("./template/cachex");

            return;
        }

        // Ask for the input and output path
        string inputPath = Question.AskFolder("Enter the path to the map folder you want to convert (the one containing cache and world): ", true);
        string outputPath = Question.AskFolder("Enter the path to the output folder: ", false);

        // Create the output folder if it doesn't exist
        Directory.CreateDirectory(outputPath);

        ConversionRequest conversionRequest = new()
        {
            TemplatePath = "./Template",
            InputPath = inputPath,
            OutputPath = outputPath
        };

        ConvertUbiArtToUnity converted = new(conversionRequest);
    }
}
