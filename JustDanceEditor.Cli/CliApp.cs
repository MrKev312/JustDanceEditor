using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.AppHost;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Cli;

internal sealed class CliApp(
    IEnumerable<IConverterPlugin> plugins,
    IEnumerable<IJdiFormat> formats,
    IEnumerable<IFormatConversionStrategy> strategies,
    IEnumerable<IToolProvider> toolProviders,
    ILogger<CliApp> logger)
{
    private readonly IConverterPlugin[] _plugins = [.. plugins];
    private readonly IJdiFormat[] _formats = [.. formats];
    private readonly IFormatConversionStrategy[] _strategies = [.. strategies];
    private readonly IToolProvider[] _toolProviders = [.. toolProviders];
    private readonly ILogger<CliApp> _logger = logger;

    public int Run(string[] args)
    {
        try
        {
            string command = args[0].ToLowerInvariant();
            string[] remainingArgs = [.. args.Skip(1)];
            if (command is "tool" or "run-tool")
                return RunToolCommand(remainingArgs);

            CliArguments options = CliArguments.Parse(remainingArgs);

            return command switch
            {
                "help" or "-h" or "--help" => PrintHelp(),
                "plugins" or "list-plugins" => ListPlugins(options),
                "targets" or "list-targets" => ListTargets(options),
                "tools" or "list-tools" => ListTools(options),
                "convert" => ConvertSingle(options),
                "batch" or "batch-convert" => ConvertBatch(options),
                "extract-ipk" => RunToolByCode("ipk.extract", options),
                "cache-create" => RunToolByCode("unity.cache-create", options),
                "cache-spread" => RunToolByCode("unity.cache-spread", options),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(ex.Message);
            Console.ResetColor();
            _logger.LogError(ex, "CLI command failed: {Message}", ex.Message);
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
        JustDanceEditor CLI

        Commands:
          targets
              List export targets grouped by platform.

          plugins
              List loaded converter plugins, formats, targets, and assembly diagnostics.

          tools
              List tool providers and available tools.

          convert --input <path> --target <target-code> --output <folder> [options]
              Convert one source to a target through JDI.

          batch --input <folder-or-ipk> --target <target-code> --output <folder> [options]
              Convert every detected source in a folder. Multi-map UbiArt bundles are expanded.

          extract-ipk --input <ipk> --output <folder>
              Extract an IPK archive.

          cache-create --output <folder>
              Create an empty NX Unity cache structure.

          cache-spread --input <cache-folder> [--force]
              Spread cache folders for exFAT. --force continues when SD_Cache.002A is absent.

          tool <provider.tool> [--input <path>] [--output <path>] [--force] [--answer <id=value>]
              Run any registered tool. Use 'tools' to list provider/tool codes and prompt ids.

        Common conversion options:
          --source <format>            Force source format by code or display name.
          --song <map-name>            Select a specific map/song when a source contains several.
          --answer <id=value>          Supply a prompt answer. Repeatable.
          --template <folder>          Shortcut for --answer unity.templatePath=<folder>.
          --download-online-assets     Download online assets after import.

        Examples:
          JustDanceEditor.Cli convert --input C:\Songs\HighHopes --target nx-2022 --output C:\Out
          JustDanceEditor.Cli convert --input C:\UnitySong --target uncooked --output C:\Out --source Unity
          JustDanceEditor.Cli batch --input C:\Bundles --target durango-2022 --output C:\Out
          JustDanceEditor.Cli targets
        """);
        return 0;
    }

    private int ListPlugins(CliArguments options)
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

    private int ListTools(CliArguments options)
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

    private int ListTargets(CliArguments options)
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

    private int ConvertSingle(CliArguments options)
    {
        string inputPath = options.Require("input");
        string outputPath = options.Require("output");
        ConversionTargetDefinition target = ResolveTarget(options.Require("target"));
        IJdiFormat sourceFormat = ResolveSourceFormat(inputPath, options.Get("source"));
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(sourceFormat.DisplayName);
        IFormatConversionStrategy targetStrategy = ResolveStrategy(target.FormatName);
        IJdiFormat targetFormat = ResolveFormat(target.FormatName);

        PromptAnswerSet answers = BuildAnswers(options, outputPath);
        string? songName = options.Get("song");
        if (!string.IsNullOrWhiteSpace(songName))
            answers.Set("ubiart.songName", songName);

        IConversionInteraction interaction = new StaticConversionInteraction(answers);
        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileName(inputPath) ?? "Export");

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(inputPath, intermediatePath, songName, Interaction: interaction));
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

    private int ConvertBatch(CliArguments options)
    {
        string inputPath = options.Require("input");
        string outputPath = options.Require("output");
        ConversionTargetDefinition target = ResolveTarget(options.Require("target"));
        IFormatConversionStrategy targetStrategy = ResolveStrategy(target.FormatName);
        IJdiFormat targetFormat = ResolveFormat(target.FormatName);
        PromptAnswerSet answers = BuildAnswers(options, outputPath);
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
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "Failed to convert '{Input}': {Message}", input.Path, ex.Message);
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
            ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(input.Path, intermediatePath));
            return ConvertOneBatchSong(input.SourceFormat, targetFormat, targetStrategy, importRequest, input.Path, outputPath, target, answers, existingSongs, downloadOnlineAssets)
                ? new BatchConversionResult(1, 0)
                : new BatchConversionResult(0, 1);
        }
        catch (MultipleConversionItemsFoundException ex)
        {
            int converted = 0;
            int skipped = 0;
            foreach (string songName in ex.AvailableItems)
            {
                ConversionRequestBase songRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(input.Path, intermediatePath, songName));
                if (ConvertOneBatchSong(input.SourceFormat, targetFormat, targetStrategy, songRequest, input.Path, outputPath, target, answers, existingSongs, downloadOnlineAssets))
                    converted++;
                else
                    skipped++;
            }

            return new BatchConversionResult(converted, skipped);
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

    private PromptAnswerSet BuildAnswers(CliArguments options, string outputPath)
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

        string? template = options.Get("template");
        if (!string.IsNullOrWhiteSpace(template))
            answers.Set("unity.templatePath", template);

        return answers;
    }

    private int RunToolCommand(string[] args)
    {
        string? toolCode = null;
        string[] optionArgs = args;

        if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            toolCode = args[0];
            optionArgs = [.. args.Skip(1)];
        }

        CliArguments options = CliArguments.Parse(optionArgs);
        toolCode ??= options.Get("id") ?? options.Get("tool");
        if (string.IsNullOrWhiteSpace(toolCode))
            throw new ArgumentException("Missing tool code. Use 'tool <provider.tool>' or '--id <provider.tool>'.");

        return RunToolByCode(toolCode, options);
    }

    private int RunToolByCode(string toolCode, CliArguments options)
    {
        (IToolProvider provider, ToolDefinition tool) = ResolveTool(toolCode);
        PromptAnswerSet seedAnswers = BuildToolAnswers(options, tool);
        IConversionInteraction interaction = new StaticConversionInteraction(seedAnswers);
        PromptAnswerSet promptAnswers = tool.Prompts.Count == 0
            ? seedAnswers
            : interaction.AskAsync(new ConversionPromptSet(tool.FullCode, tool.DisplayName, tool.Prompts)).GetAwaiter().GetResult();
        PromptAnswerSet answers = MergeToolAnswers(seedAnswers, promptAnswers);

        provider.ExecuteAsync(new ToolExecutionContext(tool, answers, interaction)).GetAwaiter().GetResult();
        Console.WriteLine($"{tool.DisplayName} completed.");
        return 0;
    }

    private PromptAnswerSet BuildToolAnswers(CliArguments options, ToolDefinition tool)
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

    private static int UnknownCommand(string command)
    {
        Console.WriteLine($"Unknown command '{command}'.");
        Console.WriteLine("Run 'help' for usage.");
        return 1;
    }

    private sealed record BatchInput(string Path, IJdiFormat SourceFormat);

    private readonly record struct BatchConversionResult(int Converted, int Skipped);
}
