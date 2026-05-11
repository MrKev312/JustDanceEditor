using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.AppHost;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Cli.Interactive.Helpers;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Cli.Interactive.Converting;

internal static class FormatConversionDialogue
{
    public static void Start(IEnumerable<IJdiFormat> formatsEnumerable, IEnumerable<IFormatConversionStrategy> strategies, IConversionInteraction interaction, ILogger logger)
    {
        // Ask for input folder or IPK up front so we can auto-detect its format
        string inputPath = Question.AskFolderOrIpk("Enter the input folder or IPK for the conversion");

        IJdiFormat[] formats = [.. formatsEnumerable];
        IFormatConversionStrategy[] conversionStrategies = [.. strategies];

        IJdiFormat[] sourceCandidates = [.. formats.Where(f => f.CanImport)];

        if (sourceCandidates.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No compatible format combinations are available in this build.");
            Console.ResetColor();
            return;
        }

        // Auto-detect source format from input folder. Only ask the user when ambiguous.
        IJdiFormat[] detected = [.. sourceCandidates.Where(f => f.Check(inputPath))];
        string sourceName;
        if (detected.Length == 1)
        {
            sourceName = detected[0].DisplayName;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Auto-detected source format: {sourceName}");
            Console.ResetColor();
        }
        else if (detected.Length > 1)
        {
            string[] labels = [.. detected.Select(f => f.DisplayName)];
            int sel = Question.Ask(labels, 0, "Multiple source formats detected. Which one is the source?");
            sourceName = labels[sel];
        }
        else
        {
            sourceName = AskFormat("Select the source format", sourceCandidates);
        }

        IJdiFormat sourceFormat = formats.First(f => f.DisplayName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
        IFormatConversionStrategy sourceStrategy = ResolveStrategy(conversionStrategies, sourceName);

        ConversionTargetDefinition target = ConsoleConversionTargetSelector.AskTarget(
            conversionStrategies,
            (choices, defaultIndex, question) => Question.Ask([.. choices], defaultIndex, question));
        IFormatConversionStrategy targetStrategy = ResolveStrategy(conversionStrategies, target.FormatName);

        string targetName = target.FormatName;
        IJdiFormat targetFormat = formats.First(f => f.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        PromptAnswerSet targetAnswers = AskTargetPrompts(target, interaction);
        string outputPath = GetOutputPath(targetAnswers);
        string intermediatePath = target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileName(inputPath) ?? "Export");

        ConversionRequestBase importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(
            InputPath: inputPath,
            OutputPath: intermediatePath,
            Interaction: interaction));

        ConversionRequestBase exportRequest = targetStrategy.CreateExportRequest(new ConversionRequestContext(
            InputPath: inputPath,
            OutputPath: outputPath,
            Target: target,
            Answers: targetAnswers,
            Interaction: interaction));

        // Ask about downloading online assets BEFORE conversion
        bool downloadOnlineAssets = Question.Ask(["Yes", "No"], 0, "Download online assets for this song?") == 0;

        try
        {
            while (true)
            {
                try
                {
                    LogImportStep(logger, sourceName, inputPath, intermediatePath);
                    JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();
                    LogImportStepCompleted(logger, sourceName, importResult);

                    // Download online assets after successful conversion if requested
                    if (downloadOnlineAssets && importResult.Package is not null && importResult.MaterializedRoot is not null)
                    {
                        try
                        {
                            OnlineAssetDownloader downloader = new(logger);
                            downloader.DownloadAssetsAsync(importResult.MaterializedRoot, importResult.Package).GetAwaiter().GetResult();
                            Console.WriteLine("Online assets downloaded successfully.");
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Failed to download online assets: {ex.Message}");
                            Console.ResetColor();
                            logger.LogWarning(ex, "Online asset download failed: {Message}", ex.Message);
                        }
                    }

                    try
                    {
                        LogExportStep(logger, targetName, outputPath);
                        targetFormat.ExportAsync(importResult, exportRequest).GetAwaiter().GetResult();
                        LogExportStepCompleted(logger, targetName, outputPath);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Conversion {sourceName} -> {targetName} completed successfully.");
                        Console.ResetColor();
                    }
                    finally
                    {
                        if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
                            Directory.Delete(importResult.MaterializedRoot, true);
                    }

                    break; // Success - exit the retry loop
                }
                catch (MultipleConversionItemsFoundException msEx)
                {
                    // Multi-song bundle detected - ask user to select
                    string[] songChoices = [.. msEx.AvailableItems];
                    int songSelection = Question.Ask(songChoices, 0, "Multiple songs found in the bundle. Which one should be converted?");
                    string selectedSong = songChoices[songSelection];

                    importRequest = sourceStrategy.CreateImportRequest(new ConversionRequestContext(
                        InputPath: inputPath,
                        OutputPath: intermediatePath,
                        SongName: selectedSong,
                        Interaction: interaction));

                    // Retry the import with the selected song
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Conversion failed: {ex.Message}");
            logger.LogError(ex, "Format conversion failed: {Exception}", ex);
            Console.ResetColor();
        }
    }

    private static string AskFormat(string prompt, IEnumerable<IJdiFormat> candidates)
    {
        IJdiFormat[] options = [.. candidates
            .OrderBy(f => f.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f.DisplayName)];

        if (options.Length == 0)
            throw new InvalidOperationException("No formats satisfy the requested capability.");

        string[] labels = [.. options.Select(f => f.DisplayName)];
        int selection = Question.Ask(labels, 0, prompt);
        return options[selection].DisplayName;
    }

    private static IFormatConversionStrategy ResolveStrategy(IEnumerable<IFormatConversionStrategy> strategies, string formatName)
    {
        return strategies.First(strategy => strategy.FormatName.Equals(formatName, StringComparison.OrdinalIgnoreCase));
    }

    private static PromptAnswerSet AskTargetPrompts(ConversionTargetDefinition target, IConversionInteraction interaction)
    {
        IReadOnlyList<ConversionPrompt> prompts = target.ExportPrompts.Count == 0
            ? [new ConversionPrompt(ConversionPromptIds.OutputPath, ConversionPromptKind.FolderPath, "Enter the destination folder for the converted files", Required: false)]
            : target.ExportPrompts;

        return interaction.AskAsync(new ConversionPromptSet($"target.{target.TargetCode}", $"Configure {target.DisplayName}", prompts)).GetAwaiter().GetResult();
    }

    private static string GetOutputPath(PromptAnswerSet answers)
    {
        string path = answers.GetString(ConversionPromptIds.OutputPath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void LogImportStep(ILogger logger, string sourceName, string inputPath, string intermediatePath)
    {
        if (sourceName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Loading JDI source package from '{InputPath}'", inputPath);
            return;
        }

        logger.LogInformation("Starting {SourceFormat} -> JDI conversion from '{InputPath}' into '{IntermediatePath}'", sourceName, inputPath, intermediatePath);
    }

    private static void LogImportStepCompleted(ILogger logger, string sourceName, JdiImportResult importResult)
    {
        string mapName = importResult.Package.Metadata.MapName ?? importResult.Package.Metadata.Title ?? "song";
        if (sourceName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Loaded JDI source package for '{MapName}'", mapName);
            return;
        }

        logger.LogInformation("Completed {SourceFormat} -> JDI conversion for '{MapName}' at '{MaterializedRoot}'", sourceName, mapName, importResult.MaterializedRoot);
    }

    private static void LogExportStep(ILogger logger, string targetName, string outputPath)
    {
        if (targetName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Writing JDI package to '{OutputPath}'", outputPath);
            return;
        }

        logger.LogInformation("Starting JDI -> {TargetFormat} conversion into '{OutputPath}'", targetName, outputPath);
    }

    private static void LogExportStepCompleted(ILogger logger, string targetName, string outputPath)
    {
        if (targetName.Equals("JDI", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("JDI package write completed at '{OutputPath}'", outputPath);
            return;
        }

        logger.LogInformation("Completed JDI -> {TargetFormat} conversion into '{OutputPath}'", targetName, outputPath);
    }
}
