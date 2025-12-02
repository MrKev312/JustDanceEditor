using JustDanceEditor.Logging;
using JustDanceEditor.UI.Converting;
using JustDanceEditor.UI.Helpers;

using System.Reflection;

namespace JustDanceEditor.UI;

internal class Program
{
    static void Main()
    {
        // Delete old log
        Logger.ClearLog();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("*************************************");
        Console.WriteLine("*      Just Dance Editor Tool       *");
        Console.WriteLine("*************************************");
        Console.ResetColor();
        Console.WriteLine("Developed by: MrKev312");

        // Get version from nerdbank.gitversioning
        string versionMessage = $"Version: {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}";
        Logger.Log(versionMessage, LogLevel.Debug);
        Console.WriteLine(versionMessage);

        string directoryMessage = $"Current Directory: {Environment.CurrentDirectory}";
        Logger.Log(directoryMessage, LogLevel.Debug);
        Console.WriteLine(directoryMessage);
        Console.WriteLine();

        MainLoop();

        Console.WriteLine("\nThank you for using Just Dance Editor. Exiting...");
    }

    static void MainLoop()
    {
        while (true)
        {
            Console.WriteLine("\n================ Main Menu ================");
            int choice = Question.Ask([
                "Exit Program",
                "Convert UbiArt Map to Unity (Standard)",
                "Convert UbiArt Map to Unity (Advanced Options)",
                "Convert Between Formats (Experimental)",
                "Batch Convert All Songs in a Folder",
                "Update All Covers",
                "Extract IPK Archive File",
                "Generate a New Cache Structure",
                "Optimize Cache Folders for exFAT (Spread Caches equally)"
            ], 0, "Please select an action:");

            Console.WriteLine("=========================================\n");

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    Console.WriteLine("--- Standard UbiArt to Unity Conversion ---");
                    ConverterDialogue.ConvertSingleDialogue();
                    break;
                case 2:
                    Console.WriteLine("--- Advanced UbiArt to Unity Conversion ---");
                    ConverterDialogue.ConvertSingleDialogueAdvanced();
                    break;
                case 3:
                    Console.WriteLine("--- Format Conversion ---");
                    FormatConversionDialogue.Start();
                    break;
                case 4:
                    Console.WriteLine("--- Batch Convert Songs ---");
                    ConverterDialogue.ConvertAllSongsInFolder();
                    break;
                case 5:
                    Console.WriteLine("--- Update Covers ---");
                    ConverterDialogue.UpdateCovers();
                    break;
                case 6:
                    Console.WriteLine("--- Extract IPK Archive ---");
                    ExtractorDialogue.ExtractDialogue();
                    break;
                case 7:
                    Console.WriteLine("--- Generate New Cache ---");
                    CacheDialogue.GenerateCacheDialogue();
                    break;
                case 8:
                    Console.WriteLine("--- Spread Cache for exFAT ---");
                    CacheDialogue.SpreadCacheDialogue();
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Invalid selection. Please try again.");
                    Console.ResetColor();
                    break;
            }

            Console.WriteLine("\nPress any key to return to the main menu...");
            Console.ReadKey();
            Console.Clear();
        }
    }
}