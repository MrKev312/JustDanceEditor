using JustDanceEditor.AppHost;
using JustDanceEditor.Cli.Interactive;
using JustDanceEditor.Cli.Interactive.Converting;
using JustDanceEditor.Cli.Interactive.Helpers;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.UbiArt.Export.Ipk;

using Microsoft.Extensions.Logging;

using System.CommandLine;

namespace JustDanceEditor.Cli;

internal sealed class CliApp(
    IEnumerable<IConverterPlugin> plugins,
    IEnumerable<IJdiFormat> formats,
    IEnumerable<IFormatConversionStrategy> strategies,
    IEnumerable<IToolProvider> toolProviders,
    DroppedPathProcessor droppedPathProcessor,
    IConversionInteraction interactiveInteraction,
    IConversionWorkflow conversionWorkflow,
    ToolDialogue toolDialogue,
    ConsoleApp consoleApp,
    ILogger<CliApp> logger)
{
    private readonly IConverterPlugin[] _plugins = [.. plugins];
    private readonly IJdiFormat[] _formats = [.. formats];
    private readonly IFormatConversionStrategy[] _strategies = [.. strategies];
    private readonly IToolProvider[] _toolProviders = [.. toolProviders];
    private readonly DroppedPathProcessor _droppedPathProcessor = droppedPathProcessor;
    private readonly ConsoleApp _consoleApp = consoleApp;
    private readonly ILogger<CliApp> _logger = logger;
    private readonly CliConversionRunner _conversionRunner = new([.. formats], [.. strategies], interactiveInteraction, conversionWorkflow, logger);
    private readonly CliToolRunner _toolRunner = new([.. toolProviders], interactiveInteraction, toolDialogue);

    public int Run(string[] args)
    {
        try
        {
            Command rootCommand = new CliCommandBuilder(this, _toolProviders, _droppedPathProcessor, _consoleApp).BuildRootCommand();
            return rootCommand.Parse(args).Invoke();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            return 1;
        }
    }

    internal int ExecuteCommand(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            return 1;
        }
    }

    internal int ListPlugins(CliOptions options)
        => new CliCatalogPrinter(_plugins, _formats, _strategies, _toolProviders).ListPlugins(options);

    internal int ListTools(CliOptions options)
        => new CliCatalogPrinter(_plugins, _formats, _strategies, _toolProviders).ListTools(options);

    internal int ListTargets(CliOptions options)
        => new CliCatalogPrinter(_plugins, _formats, _strategies, _toolProviders).ListTargets(options);

    internal int ConvertSingle(CliOptions options) => _conversionRunner.ConvertSingle(options);

    internal int ConvertBatch(CliOptions options) => _conversionRunner.ConvertBatch(options);

    internal int RunToolCommand(ParseResult parseResult, CliCommandSymbols symbols)
        => _toolRunner.RunToolCommand(parseResult, symbols);

    internal int RunToolByCode(string toolCode, CliOptions options)
        => _toolRunner.RunToolByCode(toolCode, options);

    internal int PackIpk(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => Question.AskFolder("Enter the folder to pack", mustExist: true));
        string? outputPath = options.Get("output");
        bool force = options.HasFlag("force");
        return _droppedPathProcessor.PackIpk(inputPath, outputPath, force) ? 0 : 1;
    }

    internal int RebuildSecureFat(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => Question.AskFolder("Enter the game archive folder", mustExist: true));
        string platform = options.Get("platform") ?? "pc";

        UbiArtSecureFatWriter.Update(inputPath, platform, _logger);
        Console.WriteLine($"Rebuilt secure_fat.gf for {platform}: {Path.Combine(inputPath, "secure_fat.gf")}");
        return 0;
    }

    internal int ConvertAudio(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => AskExistingFileOrFolder("Enter the audio file or folder"));
        string? outputPath = options.Get("output");
        bool force = options.HasFlag("force");
        return _droppedPathProcessor.ConvertAudio(inputPath, outputPath, force, options.Get("encoding"), options.Headless) ? 0 : 1;
    }

    internal int ConvertTexture(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => AskExistingFileOrFolder("Enter the image or texture file/folder"));
        string? outputPath = options.Get("output");
        bool force = options.HasFlag("force");
        return _droppedPathProcessor.ConvertTexture(inputPath, outputPath, force, options.Get("encoding"), options.Headless) ? 0 : 1;
    }

    private static string RequireOrAsk(CliOptions options, string key, Func<string> ask)
    {
        string? value = options.Get(key);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        if (options.Headless)
            return options.Require(key);

        return ask();
    }

    private static string AskExistingFileOrFolder(string question)
    {
        Console.WriteLine($"{question} (must already exist)");
        Console.WriteLine("You can also drag and drop the file or folder onto the console window and press Enter.");

        while (true)
        {
            Console.Write("Path: ");
            string? path = Console.ReadLine()?.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                return path;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Please enter an existing file or folder.");
            Console.ResetColor();
        }
    }
}