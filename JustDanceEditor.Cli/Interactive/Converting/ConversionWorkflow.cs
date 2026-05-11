using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.AppHost;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Cli.Interactive.Helpers;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Cli.Interactive.Converting;

/// <summary>
/// Orchestrates song conversion workflows with dependency injection.
/// </summary>
public sealed class ConversionWorkflow(
    IEnumerable<IJdiFormat> formatsEnumerable,
    IEnumerable<IFormatConversionStrategy> strategies,
    IConversionInteraction interaction,
    ILogger<ConversionWorkflow> logger) : IConversionWorkflow
{
    private readonly IJdiFormat[] _formatsEnumerable = [.. formatsEnumerable];
    private readonly IFormatConversionStrategy[] _strategies = [.. strategies];
    private readonly IConversionInteraction _interaction = interaction;
    private readonly ILogger<ConversionWorkflow> _logger = logger;

    public void ConvertAllSongsInFolder()
    {
        try
        {
            Console.WriteLine("Starting batch conversion process for all songs in a folder.");

            ConversionTargetDefinition target = ConsoleConversionTargetSelector.AskTarget(
                _strategies,
                (choices, defaultIndex, question) => Question.Ask([.. choices], defaultIndex, question),
                "Select the export target for all songs");
            PromptAnswerSet targetAnswers = AskTargetPrompts(target);

            string inputFolder = AskBatchInputPath();
            string outputFolder = GetOutputPath(targetAnswers);
            bool downloadOnlineAssets = Question.Ask(["Yes", "No"], 0, "Download online assets for all imported songs when available?") == 0;
            HashSet<string> existingSongs = Directory.Exists(outputFolder)
                ? new HashSet<string>(Directory.GetDirectories(outputFolder).Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            BatchInput[] inputs = DetectBatchInputs(inputFolder);

            if (inputs.Length == 0)
            {
                Console.WriteLine("No compatible song inputs found in the specified path.");
                return;
            }

            Console.WriteLine($"\nFound {inputs.Length} compatible source(s). Starting processing...");
            int convertedCount = 0;
            int skippedCount = 0;

            foreach (BatchInput input in inputs)
            {
                Console.WriteLine($"\nProcessing {input.SourceFormat.DisplayName} source: {input.Path}");

                try
                {
                    BatchConversionResult result = RunBatchConversion(input, outputFolder, target, targetAnswers, existingSongs, downloadOnlineAssets);
                    convertedCount += result.ConvertedCount;
                    skippedCount += result.SkippedCount;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to convert source '{Source}': {Message}", input.Path, ex.Message);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"   Failed to convert source '{input.Path}': {ex.Message}");
                    Console.ResetColor();
                    skippedCount++;
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nBatch conversion finished. Converted: {convertedCount} song(s). Skipped: {skippedCount} song(s).");
            Console.ResetColor();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn error occurred during batch conversion: {e.Message}");
            Console.ResetColor();
            _logger.LogCritical(e, "Batch conversion failed: {Message}", e.Message);
        }
    }

    private BatchConversionResult RunBatchConversion(BatchInput input, string outputFolder, ConversionTargetDefinition target, PromptAnswerSet targetAnswers, ISet<string> existingSongs, bool downloadOnlineAssets)
    {
        IFormatConversionStrategy sourceStrategy = GetStrategy(input.SourceFormat.DisplayName);
        IFormatConversionStrategy targetStrategy = GetStrategy(target.FormatName);

        IJdiFormat targetFormat = GetFormat(targetStrategy.FormatName);

        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputFolder
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileNameWithoutExtension(input.Path) ?? "Export");

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(input.Path, intermediatePath));

        try
        {
            bool converted = ConvertSingleBatchSong(input.SourceFormat, targetFormat, targetStrategy, importRequest, input.Path, outputFolder, target, targetAnswers, existingSongs, downloadOnlineAssets);
            return converted ? new(1, 0) : new(0, 1);
        }
        catch (MultipleConversionItemsFoundException msEx)
        {
            int converted = 0;
            int skipped = 0;

            foreach (string songName in msEx.AvailableItems)
            {
                if (existingSongs.Contains(songName, StringComparer.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"   Skipped '{songName}': Already exists in output.");
                    Console.ResetColor();
                    skipped++;
                    continue;
                }

                ConversionRequestBase songRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(input.Path, intermediatePath, songName));
                try
                {
                    if (ConvertSingleBatchSong(input.SourceFormat, targetFormat, targetStrategy, songRequest, input.Path, outputFolder, target, targetAnswers, existingSongs, downloadOnlineAssets))
                        converted++;
                    else
                        skipped++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to convert song '{SongName}' from '{Source}': {Message}", songName, input.Path, ex.Message);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"   Failed to convert '{songName}': {ex.Message}");
                    Console.ResetColor();
                    skipped++;
                }
            }

            return new(converted, skipped);
        }
    }

    private bool ConvertSingleBatchSong(
        IJdiFormat sourceFormat,
        IJdiFormat targetFormat,
        IFormatConversionStrategy targetStrategy,
        ConversionRequestBase importRequest,
        string inputPath,
        string outputFolder,
        ConversionTargetDefinition target,
        PromptAnswerSet targetAnswers,
        ISet<string> existingSongs,
        bool downloadOnlineAssets)
    {
        string importDestination = importRequest.OutputPath;
        LogBatchImportStep(sourceFormat.DisplayName, importRequest.InputPath, importDestination);
        JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();
        string songName = importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? Path.GetFileNameWithoutExtension(inputPath) ?? "song";
        LogBatchImportStepCompleted(sourceFormat.DisplayName, songName, importResult.MaterializedRoot);

        if (existingSongs.Contains(songName, StringComparer.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   Skipped '{songName}': Already exists in output.");
            Console.ResetColor();
            CleanupTemporaryImport(importResult);
            return false;
        }

        Console.WriteLine($"   Converting '{songName}' to {target.DisplayName}...");

        try
        {
            if (downloadOnlineAssets && importResult.MaterializedRoot is not null)
                DownloadOnlineAssets(importResult);

            ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(new ConversionRequestContext(inputPath, outputFolder, songName, target, targetAnswers));
            LogBatchExportStep(target.FormatName, songName, outputFolder);
            targetFormat.ExportAsync(importResult, exportRequest).GetAwaiter().GetResult();
            LogBatchExportStepCompleted(target.FormatName, songName, outputFolder);
            Console.WriteLine($"   Conversion of '{songName}' finished.");
            existingSongs.Add(songName);
            return true;
        }
        finally
        {
            CleanupTemporaryImport(importResult);
        }
    }

    private IFormatConversionStrategy GetStrategy(string formatName) =>
        _strategies.First(strategy => strategy.FormatName.Equals(formatName, StringComparison.OrdinalIgnoreCase));

    private IJdiFormat GetFormat(string formatName) =>
        _formatsEnumerable.First(format => format.DisplayName.Equals(formatName, StringComparison.OrdinalIgnoreCase));

    private BatchInput[] DetectBatchInputs(string inputPath)
    {
        if (Path.GetExtension(inputPath).Equals(".ipk", StringComparison.OrdinalIgnoreCase))
            return DetectInput(inputPath) is { } ipkInput ? [ipkInput] : [];

        BatchInput? directInput = DetectInput(inputPath);
        if (directInput is not null)
            return [directInput];

        string[] ignoreFolders = ["bundle_nx", "patch_nx", "sku_nx", "bin", "obj"];
        return [.. Directory.GetDirectories(inputPath)
            .Where(path => !ignoreFolders.Any(folder => Path.GetFileName(path).Contains(folder, StringComparison.OrdinalIgnoreCase)))
            .Select(DetectInput)
            .OfType<BatchInput>()];
    }

    private BatchInput? DetectInput(string path)
    {
        IJdiFormat[] detected = [.. _formatsEnumerable
            .Where(format => format.CanImport)
            .Where(format => format.Check(path))];

        if (detected.Length == 0)
            return null;

        IJdiFormat sourceFormat = detected.Length == 1
            ? detected[0]
            : detected.FirstOrDefault(format => !format.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase)) ?? detected[0];

        return new(path, sourceFormat);
    }

    private void DownloadOnlineAssets(JdiImportResult importResult)
    {
        try
        {
            OnlineAssetDownloader downloader = new(_logger);
            downloader.DownloadAssetsAsync(importResult.MaterializedRoot!, importResult.Package).GetAwaiter().GetResult();
            Console.WriteLine("   Online assets downloaded successfully.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"   Failed to download online assets: {ex.Message}");
            Console.ResetColor();
            _logger.LogWarning(ex, "Online asset download failed: {Message}", ex.Message);
        }
    }

    private static void CleanupTemporaryImport(JdiImportResult importResult)
    {
        if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
            Directory.Delete(importResult.MaterializedRoot, true);
    }

    private void LogBatchImportStep(string sourceName, string inputPath, string intermediatePath)
    {
        if (sourceName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Batch: loading JDI source package from '{InputPath}'", inputPath);
            return;
        }

        _logger.LogInformation("Batch: starting {SourceFormat} -> JDI conversion from '{InputPath}' into '{IntermediatePath}'", sourceName, inputPath, intermediatePath);
    }

    private void LogBatchImportStepCompleted(string sourceName, string songName, string? materializedRoot)
    {
        if (sourceName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Batch: loaded JDI source package for '{SongName}'", songName);
            return;
        }

        _logger.LogInformation("Batch: completed {SourceFormat} -> JDI conversion for '{SongName}' at '{MaterializedRoot}'", sourceName, songName, materializedRoot);
    }

    private void LogBatchExportStep(string targetName, string songName, string outputPath)
    {
        if (targetName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Batch: writing JDI package for '{SongName}' to '{OutputPath}'", songName, outputPath);
            return;
        }

        _logger.LogInformation("Batch: starting JDI -> {TargetFormat} conversion for '{SongName}' into '{OutputPath}'", targetName, songName, outputPath);
    }

    private void LogBatchExportStepCompleted(string targetName, string songName, string outputPath)
    {
        if (targetName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Batch: JDI package write completed for '{SongName}' at '{OutputPath}'", songName, outputPath);
            return;
        }

        _logger.LogInformation("Batch: completed JDI -> {TargetFormat} conversion for '{SongName}' into '{OutputPath}'", targetName, songName, outputPath);
    }

    private static string AskBatchInputPath()
    {
        Console.WriteLine("Please provide the path to either:");
        Console.WriteLine("1. A single song folder or IPK.");
        Console.WriteLine("2. A parent folder containing multiple song folders.");
        while (true)
        {
            string inputPath = Question.AskFolderOrIpk("Enter the path to the source folder(s) or IPK");

            if (File.Exists(inputPath) || Directory.Exists(inputPath))
                return inputPath;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Input path not found.");
            Console.ResetColor();
        }
    }

    private static (string inputPath, string songName) AskInputFolder()
    {
        string inputPath = "";
        string[] maps = [];

        while (maps.Length == 0)
        {
            inputPath = Question.AskFolder("Please enter the full path to the extracted UbiArt map folder (this folder should contain 'cache' and 'world' subdirectories)", true);
            string mapsPath = Path.Combine(inputPath, "world", "maps");
            if (Directory.Exists(mapsPath))
            {
                maps = Directory.GetDirectories(mapsPath);
            }

            if (maps.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No maps found in 'world/maps' subdirectory. Please check the path.");
                Console.ResetColor();
            }
        }

        maps = [.. maps.Select(Path.GetFileName).OfType<string>()];

        int index = 0;
        if (maps.Length > 1)
        {
            Console.WriteLine("Multiple maps found in the provided folder:");
            index = Question.Ask(maps, 0, "Which map do you want to convert?");
        }
        else if (maps.Length == 1)
        {
            Console.WriteLine($"Selected map: {maps[0]}");
        }

        return (inputPath, maps[index]);
    }

    private PromptAnswerSet AskTargetPrompts(ConversionTargetDefinition target)
    {
        IReadOnlyList<ConversionPrompt> prompts = target.ExportPrompts.Count == 0
            ? [new ConversionPrompt(ConversionPromptIds.OutputPath, ConversionPromptKind.FolderPath, "Please enter the full path for the output folder where converted files will be saved", Required: false)]
            : target.ExportPrompts;

        return _interaction.AskAsync(new ConversionPromptSet($"target.{target.TargetCode}", $"Configure {target.DisplayName}", prompts)).GetAwaiter().GetResult();
    }

    private static string GetOutputPath(PromptAnswerSet answers)
    {
        string outputPath = answers.GetString(ConversionPromptIds.OutputPath);
        Directory.CreateDirectory(outputPath);
        return outputPath;
    }

    private sealed record BatchInput(string Path, IJdiFormat SourceFormat);

    private readonly record struct BatchConversionResult(int ConvertedCount, int SkippedCount);
}
