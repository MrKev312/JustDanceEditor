using JustDanceEditor.Formats.JDI;
using JustDanceEditor.UI.Converting;
using JustDanceEditor.UI.DependencyInjection;
using JustDanceEditor.UI.Helpers;

using Microsoft.Extensions.Logging;

using System.Reflection;

namespace JustDanceEditor.UI;

internal sealed class ConsoleApp(IKeyedServiceProvider<IJdiFormat> formats, IEnumerable<IJdiFormat> formatsEnumerable, IConversionWorkflow conversionWorkflow, ILogger<ConsoleApp> logger)
{
    private readonly IKeyedServiceProvider<IJdiFormat> _formats = formats;
    private readonly IEnumerable<IJdiFormat> _formatsEnumerable = formatsEnumerable;
    private readonly IConversionWorkflow _conversionWorkflow = conversionWorkflow;
    private readonly ILogger<ConsoleApp> _logger = logger;

    public void Run()
    {
        _logger.LogInformation("Starting ConsoleApp");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("*************************************");
        Console.WriteLine("*      Just Dance Editor Tool       *");
        Console.WriteLine("*************************************");
        Console.ResetColor();
        Console.WriteLine("Developed by: MrKev312");

        string versionMessage = $"Version: {Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}";
        _logger.LogDebug(versionMessage);
        Console.WriteLine(versionMessage);

        string directoryMessage = $"Current Directory: {Environment.CurrentDirectory}";
        _logger.LogDebug(directoryMessage);
        Console.WriteLine(directoryMessage);
        Console.WriteLine();

        MainLoop();

        Console.WriteLine("\nThank you for using Just Dance Editor. Exiting...");
    }

    private void MainLoop()
    {
        while (true)
        {
            Console.WriteLine("\n================ Main Menu ================");
            int choice = Question.Ask([
                "Exit Program",
                "Convert Between Formats (Experimental)",
                "Batch Convert All Songs in a Folder",
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
                    Console.WriteLine("--- Format Conversion ---");
                    FormatConversionDialogue.Start(_formats, _formatsEnumerable, _logger);
                    break;
                case 2:
                    Console.WriteLine("--- Batch Convert Songs ---");
                    _conversionWorkflow.ConvertAllSongsInFolder();
                    break;
                case 3:
                    Console.WriteLine("--- Extract IPK Archive ---");
                    ExtractorDialogue.ExtractDialogue(_logger);
                    break;
                case 4:
                    Console.WriteLine("--- Generate New Cache ---");
                    CacheDialogue.GenerateCacheDialogue(_logger);
                    break;
                case 5:
                    Console.WriteLine("--- Spread Cache for exFAT ---");
                    CacheDialogue.SpreadCacheDialogue(_logger);
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