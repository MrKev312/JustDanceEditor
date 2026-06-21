using JustDanceEditor.AppHost;
using JustDanceEditor.Cli.Interactive;
using JustDanceEditor.Cli.Interactive.Converting;
using JustDanceEditor.Cli.Interactive.Helpers;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Conversion.Abstractions.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export.Ipk;

using Microsoft.Extensions.Logging;

using System.CommandLine;
using System.CommandLine.Help;

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
    private readonly IConversionInteraction _interactiveInteraction = interactiveInteraction;
    private readonly IConversionWorkflow _conversionWorkflow = conversionWorkflow;
    private readonly ToolDialogue _toolDialogue = toolDialogue;
    private readonly ConsoleApp _consoleApp = consoleApp;
    private readonly ILogger<CliApp> _logger = logger;

    public int Run(string[] args)
    {
        try
        {
            Command rootCommand = BuildRootCommand();
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

    private Command BuildRootCommand()
    {
        CliCommandSymbols symbols = new();
        Command rootCommand = new("JustDanceEditor.Cli", "Just Dance Editor command-line and interactive console.")
        {
            TreatUnmatchedTokensAsErrors = true
        };
        AddHelp(rootCommand);
        AddOptions(rootCommand, symbols, CliOptionProfile.DroppedPath);
        rootCommand.Arguments.Add(symbols.PathsArgument);

        rootCommand.SetAction(parseResult => ExecuteCommand(() =>
        {
            CliOptions options = CreateOptions(parseResult, symbols);
            string[] paths = parseResult.GetValue(symbols.PathsArgument) ?? [];
            if (paths.Length > 0)
            {
                return _droppedPathProcessor.ProcessDroppedPaths(paths, new DroppedPathOptions(
                    Headless: options.Headless,
                    OutputPath: options.Get("output"),
                    Force: options.HasFlag("force"),
                    WaitForKey: !options.HasFlag("no-wait"),
                    AudioEncoding: options.Get("audio-encoding") ?? options.Get("encoding"),
                    TextureEncoding: options.Get("texture-encoding") ?? options.Get("encoding")));
            }

            if (options.Headless)
            {
                Console.WriteLine("Missing command or dropped path. Run '--help' for usage.");
                return 1;
            }

            _consoleApp.Run();
            return 0;
        }));

        Command helpCommand = new("help", "Show help and usage information.");
        helpCommand.SetAction(_ => rootCommand.Parse(["--help"]).Invoke());
        rootCommand.Subcommands.Add(helpCommand);

        rootCommand.Subcommands.Add(CreateCommand("plugins", "List loaded converter plugins, formats, targets, and assembly diagnostics.", symbols, CliOptionProfile.Headless, ListPlugins, "list-plugins"));
        rootCommand.Subcommands.Add(CreateCommand("targets", "List export targets grouped by platform.", symbols, CliOptionProfile.Headless | CliOptionProfile.Platform, ListTargets, "list-targets"));
        rootCommand.Subcommands.Add(CreateCommand("tools", "List tool providers and available tools.", symbols, CliOptionProfile.Headless | CliOptionProfile.Provider, ListTools, "list-tools"));
        rootCommand.Subcommands.Add(CreateCommand("convert", "Convert one source to a target through JDI.", symbols, CliOptionProfile.JdiConversion, ConvertSingle));
        rootCommand.Subcommands.Add(CreateCommand("batch", "Convert every detected source in a folder.", symbols, CliOptionProfile.JdiConversion, ConvertBatch, "batch-convert"));
        rootCommand.Subcommands.Add(CreateCommand("extract-ipk", "Extract an IPK archive.", symbols, CliOptionProfile.IpkTool, options => RunToolByCode("ipk.extract", options)));
        rootCommand.Subcommands.Add(CreateCommand("pack-ipk", "Pack a folder into an IPK archive.", symbols, CliOptionProfile.IpkTool, PackIpk));
        rootCommand.Subcommands.Add(CreateCommand("rebuild-secure-fat", "Rebuild secure_fat.gf from platform IPK archives.", symbols, CliOptionProfile.Headless | CliOptionProfile.Input | CliOptionProfile.Platform, RebuildSecureFat));
        rootCommand.Subcommands.Add(CreateCommand("audio", "Convert audio files to a selected target encoding.", symbols, CliOptionProfile.MediaConversion, ConvertAudio, "convert-audio"));
        rootCommand.Subcommands.Add(CreateCommand("texture", "Convert image and texture files to a selected target encoding.", symbols, CliOptionProfile.MediaConversion, ConvertTexture, "convert-texture"));
        rootCommand.Subcommands.Add(CreateCommand("cache-spread", "Spread cache folders for exFAT.", symbols, CliOptionProfile.Headless | CliOptionProfile.Input | CliOptionProfile.Force, options => RunToolByCode("unity.cache-spread", options)));
        rootCommand.Subcommands.Add(CreateToolCommand(symbols));

        return rootCommand;
    }

    private Command CreateCommand(string name, string description, CliCommandSymbols symbols, CliOptionProfile options, Func<CliOptions, int> action, params string[] aliases)
    {
        Command command = new(name, description);
        foreach (string alias in aliases)
            command.Aliases.Add(alias);

        AddHelp(command);
        AddOptions(command, symbols, options);
        command.SetAction(parseResult => ExecuteCommand(() => action(CreateOptions(parseResult, symbols))));
        return command;
    }

    private Command CreateToolCommand(CliCommandSymbols symbols)
    {
        Command command = new("tool", "Run a registered provider tool.");
        command.Aliases.Add("run-tool");
        AddHelp(command);
        AddOptions(command, symbols, CliOptionProfile.ToolExecution);
        command.Arguments.Add(symbols.ToolCodeArgument);

        foreach (string promptId in _toolProviders
            .SelectMany(provider => provider.GetTools())
            .SelectMany(tool => tool.Prompts)
            .Select(prompt => prompt.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(promptId => !IsCommonOptionName(promptId)))
        {
            Option<string?> promptOption = new($"--{promptId}")
            {
                Description = $"Answer for prompt '{promptId}'."
            };
            symbols.DynamicPromptOptions[promptId] = promptOption;
            command.Options.Add(promptOption);
        }

        command.SetAction(parseResult => ExecuteCommand(() => RunToolCommand(parseResult, symbols)));
        return command;
    }

    private int ExecuteCommand(Func<int> action)
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

    private static void AddOptions(Command command, CliCommandSymbols symbols, CliOptionProfile options)
    {
        if (options.HasFlag(CliOptionProfile.Headless))
            command.Options.Add(symbols.HeadlessOption);
        if (options.HasFlag(CliOptionProfile.Input))
            command.Options.Add(symbols.InputOption);
        if (options.HasFlag(CliOptionProfile.Output))
            command.Options.Add(symbols.OutputOption);
        if (options.HasFlag(CliOptionProfile.Target))
            command.Options.Add(symbols.TargetOption);
        if (options.HasFlag(CliOptionProfile.Source))
            command.Options.Add(symbols.SourceOption);
        if (options.HasFlag(CliOptionProfile.Song))
            command.Options.Add(symbols.SongOption);
        if (options.HasFlag(CliOptionProfile.Answer))
            command.Options.Add(symbols.AnswerOption);
        if (options.HasFlag(CliOptionProfile.DownloadOnlineAssets))
            command.Options.Add(symbols.DownloadOnlineAssetsOption);
        if (options.HasFlag(CliOptionProfile.Speedtest))
            command.Options.Add(symbols.SpeedtestOption);
        if (options.HasFlag(CliOptionProfile.Provider))
            command.Options.Add(symbols.ProviderOption);
        if (options.HasFlag(CliOptionProfile.Platform))
            command.Options.Add(symbols.PlatformOption);
        if (options.HasFlag(CliOptionProfile.Id))
            command.Options.Add(symbols.IdOption);
        if (options.HasFlag(CliOptionProfile.Tool))
            command.Options.Add(symbols.ToolOption);
        if (options.HasFlag(CliOptionProfile.Force))
            command.Options.Add(symbols.ForceOption);
        if (options.HasFlag(CliOptionProfile.NoWait))
            command.Options.Add(symbols.NoWaitOption);
        if (options.HasFlag(CliOptionProfile.Encoding))
            command.Options.Add(symbols.EncodingOption);
        if (options.HasFlag(CliOptionProfile.AudioEncoding))
            command.Options.Add(symbols.AudioEncodingOption);
        if (options.HasFlag(CliOptionProfile.TextureEncoding))
            command.Options.Add(symbols.TextureEncodingOption);
    }

    private static void AddHelp(Command command)
    {
        command.Options.Add(new HelpOption("-h", "--help"));
    }

    private static CliOptions CreateOptions(ParseResult parseResult, CliCommandSymbols symbols)
    {
        CliOptions options = new(GetValue(parseResult, symbols.HeadlessOption));
        options.Set("input", GetValue(parseResult, symbols.InputOption));
        options.Set("output", GetValue(parseResult, symbols.OutputOption));
        options.Set("target", GetValue(parseResult, symbols.TargetOption));
        options.Set("source", GetValue(parseResult, symbols.SourceOption));
        options.Set("song", GetValue(parseResult, symbols.SongOption));
        options.Set("answer", GetValue(parseResult, symbols.AnswerOption) ?? []);
        options.Set("provider", GetValue(parseResult, symbols.ProviderOption));
        options.Set("platform", GetValue(parseResult, symbols.PlatformOption));
        options.Set("id", GetValue(parseResult, symbols.IdOption));
        options.Set("tool", GetValue(parseResult, symbols.ToolOption));
        options.Set("encoding", GetValue(parseResult, symbols.EncodingOption));
        options.Set("audio-encoding", GetValue(parseResult, symbols.AudioEncodingOption));
        options.Set("texture-encoding", GetValue(parseResult, symbols.TextureEncodingOption));
        options.SetFlag("download-online-assets", GetValue(parseResult, symbols.DownloadOnlineAssetsOption));
        options.SetFlag("speedtest", GetValue(parseResult, symbols.SpeedtestOption));
        options.SetFlag("force", GetValue(parseResult, symbols.ForceOption));
        options.SetFlag("no-wait", GetValue(parseResult, symbols.NoWaitOption));

        foreach ((string promptId, Option<string?> promptOption) in symbols.DynamicPromptOptions)
            options.Set(promptId, GetValue(parseResult, promptOption));

        return options;
    }

    private static TValue? GetValue<TValue>(ParseResult parseResult, Option<TValue> option)
    {
        try
        {
            return parseResult.GetValue(option);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
        catch (ArgumentException)
        {
            return default;
        }
    }

    private int ListPlugins(CliOptions options)
    {
        Console.WriteLine("Converter plugins:");
        foreach (IConverterPlugin plugin in _plugins.OrderBy(plugin => plugin.Priority).ThenBy(plugin => plugin.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            string assemblyPath = plugin.GetType().Assembly.Location;
            Console.WriteLine($"- {plugin.DisplayName} ({plugin.Code}) priority {plugin.Priority}");
            Console.WriteLine($"  Assembly: {assemblyPath}");
        }

        Console.WriteLine();
        Console.WriteLine("Formats:");
        foreach (IJdiFormat format in _formats.OrderBy(format => format.DisplayName, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"- {format.DisplayName} import={format.CanImport} export={format.CanExport}");

        Console.WriteLine();
        ListTargets(options);

        Console.WriteLine();
        ListTools(options);

        Console.WriteLine();
        Console.WriteLine("Assembly search directories:");
        foreach (string directory in ConverterPluginLoader.AssemblySearchDirectories)
            Console.WriteLine($"- {directory}");

        if (ConverterPluginLoader.LoadWarnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Load warnings:");
            foreach (string warning in ConverterPluginLoader.LoadWarnings)
                Console.WriteLine($"- {warning}");
        }

        return 0;
    }

    private int ListTools(CliOptions options)
    {
        string? providerFilter = options.Get("provider");

        Console.WriteLine("Tools:");
        foreach (IToolProvider provider in _toolProviders
            .OrderBy(provider => provider.Priority)
            .ThenBy(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(providerFilter) &&
                !provider.ProviderCode.Equals(providerFilter, StringComparison.OrdinalIgnoreCase) &&
                !provider.ProviderName.Equals(providerFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ToolDefinition[] tools = [.. provider.GetTools()
                .OrderBy(tool => tool.Priority)
                .ThenBy(tool => tool.DisplayName, StringComparer.OrdinalIgnoreCase)];
            if (tools.Length == 0)
                continue;

            Console.WriteLine($"{provider.ProviderName} ({provider.ProviderCode})");
            foreach (ToolDefinition tool in tools)
            {
                Console.WriteLine($"  {tool.FullCode,-28} {tool.DisplayName}");
                if (!string.IsNullOrWhiteSpace(tool.Description))
                    Console.WriteLine($"    {tool.Description}");

                foreach (ConversionPrompt prompt in tool.Prompts)
                {
                    string required = prompt.Required ? "required" : "optional";
                    Console.WriteLine($"    --{prompt.Id,-18} {prompt.Label} ({prompt.Kind}, {required})");
                }
            }
        }

        return 0;
    }

    private int ListTargets(CliOptions options)
    {
        ConversionTargetDefinition[] targets = ConversionTargetSelector.GetAvailableTargets(_strategies);
        string? platformFilter = options.Get("platform");

        foreach (PlatformDescriptor platform in ConversionTargetSelector.GetSortedPlatforms(targets))
        {
            if (!string.IsNullOrWhiteSpace(platformFilter) &&
                !platform.PlatformCode.Equals(platformFilter, StringComparison.OrdinalIgnoreCase) &&
                !platform.DisplayName.Equals(platformFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine(platform.DisplayName);
            foreach (ConversionTargetDefinition target in ConversionTargetSelector.GetSortedTargetsForPlatform(targets, platform.PlatformCode))
                Console.WriteLine($"  {target.TargetCode,-24} {ConversionTargetSelector.FormatTargetLabel(target, includeSupportStatus: true)}");
        }

        return 0;
    }

    private int ConvertSingle(CliOptions options)
    {
        if (!HasRequiredOptions(options, "input", "output", "target"))
        {
            if (options.Headless)
            {
                options.Require("input");
                options.Require("output");
                options.Require("target");
            }

            FormatConversionDialogue.Start(_formats, _strategies, _interactiveInteraction, _logger);
            return 0;
        }

        string inputPath = options.Require("input");
        string outputPath = options.Require("output");
        ConversionTargetDefinition target = ResolveTarget(options.Require("target"));
        IJdiFormat sourceFormat = ResolveSourceFormat(inputPath, options.Get("source"));
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(sourceFormat.DisplayName);
        IFormatConversionStrategy targetStrategy = ResolveStrategy(target.FormatName);
        IJdiFormat targetFormat = ResolveFormat(target.FormatName);

        PromptAnswerSet answers = CompleteTargetAnswers(options, target, BuildAnswers(options, outputPath));
        string? songName = options.Get("song");
        if (!string.IsNullOrWhiteSpace(songName))
            answers.Set("ubiart.songName", songName);

        IConversionInteraction interaction = CreateInteraction(options, answers);
        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileName(inputPath) ?? "Export");

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(inputPath, intermediatePath, songName, Answers: answers, Interaction: interaction));
        ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(new ConversionRequestContext(inputPath, outputPath, songName, target, answers, interaction));

        try
        {
            JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();
            if (options.HasFlag("download-online-assets") && importResult.MaterializedRoot is not null)
                new OnlineAssetDownloader(_logger).DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package).GetAwaiter().GetResult();

            try
            {
                targetFormat.ExportAsync(importResult, exportRequest).GetAwaiter().GetResult();
            }
            finally
            {
                CleanupTemporaryImport(importResult);
            }

            Console.WriteLine($"Converted {sourceFormat.DisplayName} -> {target.FormatName}: {outputPath}");
            return 0;
        }
        catch (MultipleConversionItemsFoundException ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("The source contains multiple songs/maps. Re-run with --song <name> or --answer ubiart.songName=<name>.");
            foreach (string item in ex.AvailableItems)
                Console.WriteLine($"- {item}");
            Console.ResetColor();
            return 2;
        }
    }

    private int ConvertBatch(CliOptions options)
    {
        if (!HasRequiredOptions(options, "input", "output", "target"))
        {
            if (options.Headless)
            {
                options.Require("input");
                options.Require("output");
                options.Require("target");
            }

            _conversionWorkflow.ConvertAllSongsInFolder();
            return 0;
        }

        string inputPath = options.Require("input");
        string outputPath = options.Require("output");
        ConversionTargetDefinition target = ResolveTarget(options.Require("target"));
        IFormatConversionStrategy targetStrategy = ResolveStrategy(target.FormatName);
        IJdiFormat targetFormat = ResolveFormat(target.FormatName);
        PromptAnswerSet answers = CompleteTargetAnswers(options, target, BuildAnswers(options, outputPath));
        string? forcedSource = options.Get("source");
        bool downloadOnlineAssets = options.HasFlag("download-online-assets");
        HashSet<string> existingSongs = Directory.Exists(outputPath)
            ? new(Directory.GetDirectories(outputPath).Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase);

        BatchInput[] inputs = DetectBatchInputs(inputPath, forcedSource);
        if (inputs.Length == 0)
        {
            Console.WriteLine("No compatible inputs were found.");
            return 1;
        }

        int converted = 0;
        int skipped = 0;
        int failed = 0;
        foreach (BatchInput input in inputs)
        {
            try
            {
                BatchConversionResult result = ConvertBatchInput(input, target, targetFormat, targetStrategy, answers, outputPath, existingSongs, downloadOnlineAssets);
                converted += result.Converted;
                skipped += result.Skipped;
                failed += result.Failed;
            }
            catch (Exception ex)
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed: {input.Path}: {ex.Message}");
                Console.ResetColor();
            }
        }

        Console.WriteLine($"Batch complete. Converted: {converted}. Skipped: {skipped}. Failed: {failed}.");
        return failed == 0 ? 0 : 1;
    }

    private BatchConversionResult ConvertBatchInput(
        BatchInput input,
        ConversionTargetDefinition target,
        IJdiFormat targetFormat,
        IFormatConversionStrategy targetStrategy,
        PromptAnswerSet answers,
        string outputPath,
        ISet<string> existingSongs,
        bool downloadOnlineAssets)
    {
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(input.SourceFormat.DisplayName);
        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileNameWithoutExtension(input.Path) ?? "Export");

        try
        {
            ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(input.Path, intermediatePath, Answers: answers));
            return ConvertOneBatchSong(input.SourceFormat, targetFormat, targetStrategy, importRequest, input.Path, outputPath, target, answers, existingSongs, downloadOnlineAssets)
                ? new BatchConversionResult(1, 0, 0)
                : new BatchConversionResult(0, 1, 0);
        }
        catch (MultipleConversionItemsFoundException ex)
        {
            int converted = 0;
            int skipped = 0;
            int failed = 0;
            foreach (string songName in ex.AvailableItems
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    ConversionRequestBase songRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(input.Path, intermediatePath, songName, Answers: answers));
                    if (ConvertOneBatchSong(input.SourceFormat, targetFormat, targetStrategy, songRequest, input.Path, outputPath, target, answers, existingSongs, downloadOnlineAssets))
                        converted++;
                    else
                        skipped++;
                }
                catch (Exception songException)
                {
                    failed++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Failed: {input.Path} [{songName}]: {songException.Message}");
                    Console.ResetColor();
                }
            }

            return new BatchConversionResult(converted, skipped, failed);
        }
    }

    private bool ConvertOneBatchSong(
        IJdiFormat sourceFormat,
        IJdiFormat targetFormat,
        IFormatConversionStrategy targetStrategy,
        ConversionRequestBase importRequest,
        string inputPath,
        string outputPath,
        ConversionTargetDefinition target,
        PromptAnswerSet answers,
        ISet<string> existingSongs,
        bool downloadOnlineAssets)
    {
        JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();
        string songName = importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? Path.GetFileNameWithoutExtension(inputPath) ?? "song";
        if (existingSongs.Contains(songName))
        {
            CleanupTemporaryImport(importResult);
            Console.WriteLine($"Skipped '{songName}': already exists in output.");
            return false;
        }

        try
        {
            if (downloadOnlineAssets && importResult.MaterializedRoot is not null)
                new OnlineAssetDownloader(_logger).DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package).GetAwaiter().GetResult();

            ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(new ConversionRequestContext(inputPath, outputPath, songName, target, answers));
            targetFormat.ExportAsync(importResult, exportRequest).GetAwaiter().GetResult();
            existingSongs.Add(songName);
            Console.WriteLine($"Converted '{songName}' to {target.DisplayName}.");
            return true;
        }
        finally
        {
            CleanupTemporaryImport(importResult);
        }
    }

    private PromptAnswerSet BuildAnswers(CliOptions options, string outputPath)
    {
        PromptAnswerSet answers = new();
        answers.Set(ConversionPromptIds.OutputPath, outputPath);

        foreach (string answer in options.GetMany("answer"))
        {
            int equals = answer.IndexOf('=');
            if (equals <= 0)
                throw new ArgumentException($"Invalid --answer '{answer}'. Expected --answer key=value.");

            answers.Set(answer[..equals], answer[(equals + 1)..]);
        }

        if (options.HasFlag("speedtest"))
            answers.Set("ubiart.renderSpeedTest", "true");

        return answers;
    }

    private PromptAnswerSet CompleteTargetAnswers(CliOptions options, ConversionTargetDefinition target, PromptAnswerSet seedAnswers)
    {
        if (target.ExportPrompts.Count == 0)
            return seedAnswers;

        IConversionInteraction interaction = CreateInteraction(options, seedAnswers);
        PromptAnswerSet promptAnswers = interaction
            .AskAsync(new ConversionPromptSet($"target.{target.TargetCode}", $"Configure {target.DisplayName}", target.ExportPrompts))
            .AsTask()
            .GetAwaiter()
            .GetResult();

        return MergeToolAnswers(seedAnswers, promptAnswers);
    }

    private IConversionInteraction CreateInteraction(CliOptions options, PromptAnswerSet seedAnswers)
    {
        return options.Headless
            ? new StaticConversionInteraction(seedAnswers)
            : new SeededConversionInteraction(seedAnswers, _interactiveInteraction);
    }

    private static bool HasRequiredOptions(CliOptions options, params string[] keys)
    {
        return keys.All(key => !string.IsNullOrWhiteSpace(options.Get(key)));
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

    private int RunToolCommand(ParseResult parseResult, CliCommandSymbols symbols)
    {
        CliOptions options = CreateOptions(parseResult, symbols);
        string? toolCode = parseResult.GetValue(symbols.ToolCodeArgument);
        toolCode ??= options.Get("id") ?? options.Get("tool");
        if (string.IsNullOrWhiteSpace(toolCode))
        {
            if (options.Headless)
                throw new ArgumentException("Missing tool code. Use 'tool <provider.tool>' or '--id <provider.tool>'.");

            _toolDialogue.Start();
            return 0;
        }

        return RunToolByCode(toolCode, options);
    }

    private int RunToolByCode(string toolCode, CliOptions options)
    {
        (IToolProvider provider, ToolDefinition tool) = ResolveTool(toolCode);
        PromptAnswerSet seedAnswers = BuildToolAnswers(options, tool);
        IConversionInteraction interaction = CreateInteraction(options, seedAnswers);
        PromptAnswerSet promptAnswers = tool.Prompts.Count == 0
            ? seedAnswers
            : interaction.AskAsync(new ConversionPromptSet(tool.FullCode, tool.DisplayName, tool.Prompts)).AsTask().GetAwaiter().GetResult();
        PromptAnswerSet answers = MergeToolAnswers(seedAnswers, promptAnswers);

        provider.ExecuteAsync(new ToolExecutionContext(tool, answers, interaction)).GetAwaiter().GetResult();
        Console.WriteLine($"{tool.DisplayName} completed.");
        return 0;
    }

    private int PackIpk(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => Question.AskFolder("Enter the folder to pack", mustExist: true));
        string? outputPath = options.Get("output");
        bool force = options.HasFlag("force");
        return _droppedPathProcessor.PackIpk(inputPath, outputPath, force) ? 0 : 1;
    }

    private int RebuildSecureFat(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => Question.AskFolder("Enter the game archive folder", mustExist: true));
        string platform = options.Get("platform") ?? "pc";

        UbiArtSecureFatWriter.Update(inputPath, platform, _logger);
        Console.WriteLine($"Rebuilt secure_fat.gf for {platform}: {Path.Combine(inputPath, "secure_fat.gf")}");
        return 0;
    }

    private int ConvertAudio(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => AskExistingFileOrFolder("Enter the audio file or folder"));
        string? outputPath = options.Get("output");
        bool force = options.HasFlag("force");
        return _droppedPathProcessor.ConvertAudio(inputPath, outputPath, force, options.Get("encoding"), options.Headless) ? 0 : 1;
    }

    private int ConvertTexture(CliOptions options)
    {
        string inputPath = RequireOrAsk(options, "input", () => AskExistingFileOrFolder("Enter the image or texture file/folder"));
        string? outputPath = options.Get("output");
        bool force = options.HasFlag("force");
        return _droppedPathProcessor.ConvertTexture(inputPath, outputPath, force, options.Get("encoding"), options.Headless) ? 0 : 1;
    }

    private PromptAnswerSet BuildToolAnswers(CliOptions options, ToolDefinition tool)
    {
        PromptAnswerSet answers = new();

        foreach (string answer in options.GetMany("answer"))
        {
            int equals = answer.IndexOf('=');
            if (equals <= 0)
                throw new ArgumentException($"Invalid --answer '{answer}'. Expected --answer key=value.");

            answers.Set(answer[..equals], answer[(equals + 1)..]);
        }

        if (options.Get("input") is { } inputPath)
            answers.Set(ConversionPromptIds.InputPath, inputPath);
        if (options.Get("output") is { } outputPath)
            answers.Set(ConversionPromptIds.OutputPath, outputPath);
        if (options.HasFlag("force"))
            answers.Set(ConversionPromptIds.Force, options.Get("force") ?? "true");

        foreach (ConversionPrompt prompt in tool.Prompts)
        {
            if (options.Get(prompt.Id) is { } promptValue)
            {
                answers.Set(prompt.Id, promptValue);
                continue;
            }

            if (options.HasFlag(prompt.Id))
                answers.Set(prompt.Id, "true");
        }

        return answers;
    }

    private static PromptAnswerSet MergeToolAnswers(PromptAnswerSet seedAnswers, PromptAnswerSet promptAnswers)
    {
        PromptAnswerSet merged = new(seedAnswers.Answers);
        foreach (KeyValuePair<string, string> answer in promptAnswers.Answers)
            merged.Set(answer.Key, answer.Value);

        return merged;
    }

    private (IToolProvider Provider, ToolDefinition Tool) ResolveTool(string toolCode)
    {
        (IToolProvider Provider, ToolDefinition Tool)[] tools = [.. _toolProviders
            .SelectMany(provider => provider.GetTools().Select(tool => (provider, tool)))];

        (IToolProvider Provider, ToolDefinition Tool)[] exactMatches = [.. tools.Where(item =>
            item.Tool.FullCode.Equals(toolCode, StringComparison.OrdinalIgnoreCase))];
        if (exactMatches.Length == 1)
            return exactMatches[0];

        (IToolProvider Provider, ToolDefinition Tool)[] shortMatches = [.. tools.Where(item =>
            item.Tool.ToolCode.Equals(toolCode, StringComparison.OrdinalIgnoreCase) ||
            item.Tool.DisplayName.Equals(toolCode, StringComparison.OrdinalIgnoreCase))];
        if (shortMatches.Length == 1)
            return shortMatches[0];

        if (exactMatches.Length + shortMatches.Length > 1)
            throw new ArgumentException($"Tool '{toolCode}' is ambiguous. Use the full provider.tool code.");

        throw new ArgumentException($"Unknown tool '{toolCode}'. Run 'tools' to list available tools.");
    }

    private ConversionTargetDefinition ResolveTarget(string targetCode)
    {
        ConversionTargetDefinition[] targets = ConversionTargetSelector.GetAvailableTargets(_strategies);
        return targets.FirstOrDefault(target =>
                target.TargetCode.Equals(targetCode, StringComparison.OrdinalIgnoreCase) ||
                $"{target.FormatCode}:{target.TargetCode}".Equals(targetCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown target '{targetCode}'. Run 'targets' to list available targets.");
    }

    private IJdiFormat ResolveSourceFormat(string inputPath, string? source)
    {
        if (!string.IsNullOrWhiteSpace(source))
            return ResolveFormat(source);

        IJdiFormat[] detected = [.. _formats.Where(format => format.CanImport && format.Check(inputPath))];
        return detected.Length switch
        {
            1 => detected[0],
            > 1 => detected.FirstOrDefault(format => !format.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase)) ?? detected[0],
            _ => throw new InvalidOperationException($"Could not detect source format for '{inputPath}'. Use --source <format>.")
        };
    }

    private IJdiFormat ResolveFormat(string format)
    {
        IFormatConversionStrategy? strategy = _strategies.FirstOrDefault(strategy =>
            strategy.FormatCode.Equals(format, StringComparison.OrdinalIgnoreCase) ||
            strategy.FormatName.Equals(format, StringComparison.OrdinalIgnoreCase));

        string formatName = strategy?.FormatName ?? format;
        return _formats.FirstOrDefault(item => item.DisplayName.Equals(formatName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown format '{format}'.");
    }

    private IFormatConversionStrategy ResolveStrategy(string format)
    {
        return _strategies.FirstOrDefault(strategy =>
                strategy.FormatCode.Equals(format, StringComparison.OrdinalIgnoreCase) ||
                strategy.FormatName.Equals(format, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No conversion strategy registered for '{format}'.");
    }

    private BatchInput[] DetectBatchInputs(string inputPath, string? forcedSource)
    {
        if (File.Exists(inputPath) || Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
            return DetectInput(inputPath, forcedSource) is { } fileInput ? [fileInput] : [];

        BatchInput? directInput = DetectInput(inputPath, forcedSource);
        if (directInput is not null)
            return [directInput];

        string[] ignoreFolders = ["bundle_nx", "patch_nx", "sku_nx", "bin", "obj"];
        return [.. Directory.GetDirectories(inputPath)
            .Where(path => !ignoreFolders.Any(folder => Path.GetFileName(path).Contains(folder, StringComparison.OrdinalIgnoreCase)))
            .Select(path => DetectInput(path, forcedSource))
            .OfType<BatchInput>()];
    }

    private BatchInput? DetectInput(string path, string? forcedSource)
    {
        try
        {
            IJdiFormat format = ResolveSourceFormat(path, forcedSource);
            return new(path, format);
        }
        catch
        {
            return null;
        }
    }

    private static void CleanupTemporaryImport(JdiImportResult importResult)
    {
        if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
            Directory.Delete(importResult.MaterializedRoot, true);
    }

    private static bool IsCommonOptionName(string promptId)
    {
        string[] common =
        [
            "input",
            "output",
            "target",
            "source",
            "song",
            "answer",
            "provider",
            "platform",
            "id",
            "tool",
            "force",
            "encoding",
            "audio-encoding",
            "texture-encoding",
            ConversionPromptIds.InputPath,
            ConversionPromptIds.OutputPath,
            ConversionPromptIds.Force,
        ];

        return common.Any(option => option.Equals(promptId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record BatchInput(string Path, IJdiFormat SourceFormat);

    private readonly record struct BatchConversionResult(int Converted, int Skipped, int Failed);

    [Flags]
    private enum CliOptionProfile
    {
        None = 0,
        Headless = 1 << 0,
        Input = 1 << 1,
        Output = 1 << 2,
        Target = 1 << 3,
        Source = 1 << 4,
        Song = 1 << 5,
        Answer = 1 << 6,
        DownloadOnlineAssets = 1 << 7,
        Provider = 1 << 8,
        Platform = 1 << 9,
        Id = 1 << 10,
        Tool = 1 << 11,
        Force = 1 << 12,
        NoWait = 1 << 13,
        Encoding = 1 << 14,
        AudioEncoding = 1 << 15,
        TextureEncoding = 1 << 16,
        Speedtest = 1 << 17,

        DroppedPath = Headless | Output | Force | NoWait | Encoding | AudioEncoding | TextureEncoding,
        JdiConversion = Headless | Input | Output | Target | Source | Song | Answer | DownloadOnlineAssets | Speedtest,
        IpkTool = Headless | Input | Output | Force,
        MediaConversion = Headless | Input | Output | Force | Encoding,
        ToolExecution = Headless | Input | Output | Force | Answer | Id | Tool,
    }

    private sealed class CliCommandSymbols
    {
        public Option<bool> HeadlessOption { get; } = new("--headless", "--non-interactive", "-n")
        {
            Description = "Run as a strict non-interactive CLI."
        };

        public Option<string?> InputOption { get; } = new("--input", "-i")
        {
            Description = "Input file or folder."
        };

        public Option<string?> OutputOption { get; } = new("--output", "-o")
        {
            Description = "Output file or folder."
        };

        public Option<string?> TargetOption { get; } = new("--target", "-t")
        {
            Description = "Conversion target code."
        };

        public Option<string?> SourceOption { get; } = new("--source")
        {
            Description = "Force source format by code or display name."
        };

        public Option<string?> SongOption { get; } = new("--song")
        {
            Description = "Select a specific map/song when a source contains several."
        };

        public Option<string[]> AnswerOption { get; } = new("--answer")
        {
            Description = "Supply a prompt answer as id=value. Can be repeated.",
            Arity = ArgumentArity.ZeroOrMore
        };

        public Option<bool> DownloadOnlineAssetsOption { get; } = new("--download-online-assets")
        {
            Description = "Download online assets after import."
        };

        public Option<bool> SpeedtestOption { get; } = new("--speedtest", "--render-speedtest")
        {
            Description = "Render cinematic video frames and discard the output stream instead of encoding video."
        };

        public Option<string?> ProviderOption { get; } = new("--provider")
        {
            Description = "Filter by tool provider or platform."
        };

        public Option<string?> PlatformOption { get; } = new("--platform")
        {
            Description = "Filter targets by platform."
        };

        public Option<string?> IdOption { get; } = new("--id")
        {
            Description = "Tool id."
        };

        public Option<string?> ToolOption { get; } = new("--tool")
        {
            Description = "Tool id."
        };

        public Option<bool> ForceOption { get; } = new("--force", "-f")
        {
            Description = "Overwrite existing output where supported."
        };

        public Option<bool> NoWaitOption { get; } = new("--no-wait")
        {
            Description = "Do not wait for a key after dropped-path processing."
        };

        public Option<string?> EncodingOption { get; } = new("--encoding", "--target-encoding", "--target-format")
        {
            Description = "Target audio/image/texture encoding."
        };

        public Option<string?> AudioEncodingOption { get; } = new("--audio-encoding")
        {
            Description = "Target audio encoding for mixed dropped-path batches."
        };

        public Option<string?> TextureEncodingOption { get; } = new("--texture-encoding", "--image-encoding")
        {
            Description = "Target image/texture encoding for mixed dropped-path batches."
        };

        public Argument<string[]> PathsArgument { get; } = new("path")
        {
            Description = "Dropped files or folders.",
            Arity = ArgumentArity.ZeroOrMore
        };

        public Argument<string?> ToolCodeArgument { get; } = new("tool-code")
        {
            Description = "Provider tool code, for example ipk.extract.",
            Arity = ArgumentArity.ZeroOrOne
        };

        public Dictionary<string, Option<string?>> DynamicPromptOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
