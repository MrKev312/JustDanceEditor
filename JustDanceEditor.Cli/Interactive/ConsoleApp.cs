using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Cli.Interactive.Converting;
using JustDanceEditor.Cli.Interactive.Helpers;

using Microsoft.Extensions.Logging;

using System.Reflection;

namespace JustDanceEditor.Cli.Interactive;

internal sealed class ConsoleApp(IEnumerable<IJdiFormat> formatsEnumerable, IEnumerable<IFormatConversionStrategy> formatStrategies, IConversionWorkflow conversionWorkflow, IConversionInteraction interaction, ToolDialogue toolDialogue, ILogger<ConsoleApp> logger)
{
    private readonly IEnumerable<IJdiFormat> _formatsEnumerable = formatsEnumerable;
    private readonly IEnumerable<IFormatConversionStrategy> _formatStrategies = formatStrategies;
    private readonly IConversionWorkflow _conversionWorkflow = conversionWorkflow;
    private readonly IConversionInteraction _interaction = interaction;
    private readonly ToolDialogue _toolDialogue = toolDialogue;
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

        AssemblyInformationalVersionAttribute versionAttribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?? throw new InvalidOperationException("Assembly informational version attribute is missing.");
        string versionMessage = $"Version: {versionAttribute.InformationalVersion}";
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
                "Convert Between Formats",
                "Batch Convert All Songs in a Folder",
                "Tools"
            ], 0, "Please select an action:");

            Console.WriteLine("=========================================\n");

            switch (choice)
            {
                case 0:
                    return;
                case 1:
                    Console.WriteLine("--- Format Conversion ---");
                    FormatConversionDialogue.Start(_formatsEnumerable, _formatStrategies, _interaction, _logger);
                    break;
                case 2:
                    Console.WriteLine("--- Batch Convert Songs ---");
                    _conversionWorkflow.ConvertAllSongsInFolder();
                    break;
                case 3:
                    Console.WriteLine("--- Tools ---");
                    _toolDialogue.Start();
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
