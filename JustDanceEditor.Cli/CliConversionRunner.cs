using JustDanceEditor.AppHost;
using JustDanceEditor.Cli.Interactive.Converting;
using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Cli;

internal sealed class CliConversionRunner(
    IReadOnlyList<IJdiFormat> formats,
    IReadOnlyList<IFormatConversionStrategy> strategies,
    IConversionInteraction interactiveInteraction,
    IConversionWorkflow conversionWorkflow,
    ILogger logger)
{
    public int ConvertSingle(CliOptions options)
    {
        if (!HasRequiredOptions(options, "input", "output"))
        {
            if (options.Headless)
            {
                options.Require("input");
                options.Require("output");
            }

            FormatConversionDialogue.Start(formats, strategies, interactiveInteraction, logger);
            return 0;
        }

        string inputPath = options.Require("input");
        string outputPath = options.Require("output");
        ConversionTargetDefinition target = ResolveTargetForConversion(options, outputPath);
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
                new OnlineAssetDownloader(logger).DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package).GetAwaiter().GetResult();

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

    public int ConvertBatch(CliOptions options)
    {
        if (!HasRequiredOptions(options, "input", "output"))
        {
            if (options.Headless)
            {
                options.Require("input");
                options.Require("output");
            }

            conversionWorkflow.ConvertAllSongsInFolder();
            return 0;
        }

        string inputPath = options.Require("input");
        string outputPath = options.Require("output");
        ConversionTargetDefinition target = ResolveTargetForConversion(options, outputPath);
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
            foreach (string songName in ex.AvailableItems.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
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
                new OnlineAssetDownloader(logger).DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package).GetAwaiter().GetResult();

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

        return CliToolRunner.MergeAnswers(seedAnswers, promptAnswers);
    }

    private IConversionInteraction CreateInteraction(CliOptions options, PromptAnswerSet seedAnswers)
    {
        return options.Headless
            ? new StaticConversionInteraction(seedAnswers)
            : new SeededConversionInteraction(seedAnswers, interactiveInteraction);
    }

    private ConversionTargetDefinition ResolveTarget(string targetCode)
    {
        ConversionTargetDefinition[] targets = ConversionTargetSelector.GetAvailableTargets(strategies);
        return targets.FirstOrDefault(target =>
                target.TargetCode.Equals(targetCode, StringComparison.OrdinalIgnoreCase) ||
                $"{target.FormatCode}:{target.TargetCode}".Equals(targetCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown target '{targetCode}'. Run 'targets' to list available targets.");
    }

    private ConversionTargetDefinition ResolveTargetForConversion(CliOptions options, string outputPath)
    {
        string? targetCode = options.Get("target");
        if (!string.IsNullOrWhiteSpace(targetCode))
            return ResolveTarget(targetCode);

        OutputTargetDetectionResult detectedOutput = OutputTargetDetector.Detect(outputPath, formats, strategies);
        if (detectedOutput.Target is not null)
        {
            Console.WriteLine(detectedOutput.Message);
            return detectedOutput.Target;
        }

        throw new ArgumentException($"{detectedOutput.Message} Pass --target <target>.");
    }

    private IJdiFormat ResolveSourceFormat(string inputPath, string? source)
    {
        if (!string.IsNullOrWhiteSpace(source))
            return ResolveFormat(source);

        IJdiFormat[] detected = [.. formats.Where(format => format.CanImport && format.Check(inputPath))];
        return detected.Length switch
        {
            1 => detected[0],
            > 1 => throw new ArgumentException($"Multiple source formats detected for '{inputPath}': {string.Join(", ", detected.Select(format => format.DisplayName))}. Use --source <format>."),
            _ => throw new InvalidOperationException($"Could not detect source format for '{inputPath}'. Use --source <format>.")
        };
    }

    private IJdiFormat ResolveFormat(string format)
    {
        IFormatConversionStrategy? strategy = strategies.FirstOrDefault(strategy =>
            strategy.FormatCode.Equals(format, StringComparison.OrdinalIgnoreCase) ||
            strategy.FormatName.Equals(format, StringComparison.OrdinalIgnoreCase));

        string formatName = strategy?.FormatName ?? format;
        return formats.FirstOrDefault(item => item.DisplayName.Equals(formatName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown format '{format}'.");
    }

    private IFormatConversionStrategy ResolveStrategy(string format)
    {
        return strategies.FirstOrDefault(strategy =>
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
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool HasRequiredOptions(CliOptions options, params string[] keys)
        => keys.All(key => !string.IsNullOrWhiteSpace(options.Get(key)));

    private static void CleanupTemporaryImport(JdiImportResult importResult)
    {
        if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
            Directory.Delete(importResult.MaterializedRoot, true);
    }

    private sealed record BatchInput(string Path, IJdiFormat SourceFormat);

    private readonly record struct BatchConversionResult(int Converted, int Skipped, int Failed);
}