using JustDanceEditor.IPK;
using JustDanceEditor.Logging;
using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI.Converting;

internal class ExtractorDialogue
{
    public static void ExtractDialogue()
    {
        Console.WriteLine("This option will extract the contents of an IPK archive file.");
        // Ask for the input and output path
        string inputPath = Question.AskFile("Please enter the full path to the .ipk file you want to extract", true);
        string defaultOutputPath = Path.Combine(Path.GetDirectoryName(inputPath)!, Path.GetFileNameWithoutExtension(inputPath));
        string outputPath = Question.AskFolder($"Please enter the full path for the output folder (default: {defaultOutputPath})", false);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = defaultOutputPath;
            Console.WriteLine($"Using default output path: {outputPath}");
        }

        // Create the output folder if it doesn't exist
        Directory.CreateDirectory(outputPath);

        try
        {
            Console.WriteLine($"\nExtracting '{Path.GetFileName(inputPath)}' to '{outputPath}'...");
            // Extract the IPK file
            JustDanceIPKParser parser = new(inputPath, outputPath);
            parser.Parse(ShowInfo: true); // Assuming ShowInfo: true provides progress/details

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nIPK file extracted successfully!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during extraction: {ex.Message}");
            Console.ResetColor();
            Logger.Log($"IPK Extraction failed for {inputPath}: {ex.Message}", LogLevel.Error);
        }
    }
}