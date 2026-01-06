using JustDanceEditor.Formats.JDI;
using JustDanceEditor.UI.DependencyInjection;
using JustDanceEditor.UI.Helpers;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.UI.Converting;

internal static class FormatConversionDialogue
{
    public static void Start(IKeyedServiceProvider<IJdiFormat> formatsProvider, IEnumerable<IJdiFormat> formatsEnumerable, ILogger logger)
    {
        // Ask for input folder or IPK up front so we can auto-detect its format
        string inputPath = Question.AskFolderOrIpk("Enter the input folder or IPK for the conversion");

        IJdiFormat[] formats = [.. formatsEnumerable];

        IJdiFormat[] sourceCandidates = [.. formats.Where(f => f.CanImport)];
        IJdiFormat[] targetCandidates = [.. formats.Where(f => f.CanExport)];

        if (sourceCandidates.Length == 0 || targetCandidates.Length == 0)
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

        string targetName = AskFormat("Select the target format", targetCandidates);

        if (string.Equals(sourceName, targetName, StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Source and target formats are identical. No conversion will be performed.");
            Console.ResetColor();
            return;
        }

        IJdiFormat sourceFormat = formats.First(f => f.DisplayName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
        IJdiFormat targetFormat = formats.First(f => f.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        (ConversionRequestBase importRequest, ConversionRequestBase exportRequest) = BuildRequests(sourceName, targetName, inputPath);

        try
        {
            JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();

            try
            {
                targetFormat.ExportAsync(importResult, exportRequest).GetAwaiter().GetResult();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Conversion {sourceName} -> {targetName} completed successfully.");
                Console.ResetColor();
            }
            finally
            {
                if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null && Directory.Exists(importResult.MaterializedRoot))
                    Directory.Delete(importResult.MaterializedRoot, true);
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

    private static (ConversionRequestBase importRequest, ConversionRequestBase exportRequest) BuildRequests(string source, string target, string inputPath)
    {
        string outputPath = AskOutputPath(target);

        string intermediatePath = target == "JDI" ? outputPath : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileName(inputPath) ?? "Export");

        ConversionRequestBase importRequest = source switch
        {
            "UbiArt" => BuildUbiArtImportRequest(inputPath, intermediatePath),
            "Unity" => BuildUnityImportRequest(inputPath, intermediatePath, target),
            "JDI" => new JdiConversionRequest(inputPath, intermediatePath),
            _ => throw new NotSupportedException($"Unknown source format '{source}'.")
        };

        ConversionRequestBase exportRequest = target switch
        {
            "Unity" => BuildUnityExportRequest(outputPath),
            "UbiArt" => BuildUbiArtExportRequest(inputPath, outputPath),
            "JDI" => new JdiConversionRequest(inputPath, outputPath),
            _ => throw new NotSupportedException("Exporting to the selected target is not supported.")
        };

        return (importRequest, exportRequest);
    }

    private static string AskOutputPath(string target)
    {
        string prompt = target switch
        {
            "Unity" => "Enter the Unity output root (custom server layout)",
            "JDI" => "Enter the folder where the JDI package should be written",
            _ => "Enter the destination folder for the converted files"
        };

        string path = Question.AskFolder(prompt, false);
        Directory.CreateDirectory(path);
        return path;
    }

    private static ConversionRequestBase BuildUbiArtImportRequest(string inputPath, string outputPath)
    {
        string? songName = ResolveUbiArtSongName(inputPath);
        return new UbiArtConversionRequest(inputPath, outputPath, songName);
    }

    private static ConversionRequestBase BuildUnityImportRequest(string inputPath, string outputPath, string target)
    {
        // Template path is unused during import; supply outputPath to satisfy constructor.
        UnityConversionRequest request = new(inputPath, outputPath, outputPath)
        {
            ExportType = ExportType.CustomServer,
            OnlineCover = target == "Unity" && Question.AskYesNo("Attempt to download missing covers from the internet?")
        };

        return request;
    }

    private static ConversionRequestBase BuildUnityExportRequest(string outputPath)
    {
        string templatePath = ResolveTemplatePath();

        UnityConversionRequest request = new(outputPath, outputPath, templatePath)
        {
            ExportType = ExportType.CustomServer,
            OnlineCover = Question.AskYesNo("Attempt to download missing covers from the internet?")
        };

        return request;
    }

    private static ConversionRequestBase BuildUbiArtExportRequest(string inputPath, string outputPath)
    {
        // For now, default to Uncooked export
        string? songName = ResolveUbiArtSongName(inputPath);
        return new UbiArtConversionRequest(inputPath, outputPath, songName)
        {
            Type = UbiArtType.Uncooked
        };
    }

    private static string? ResolveUbiArtSongName(string inputPath)
    {
        string mapsPath = Path.Combine(inputPath, "world", "maps");
        if (!Directory.Exists(mapsPath))
            return null;

        string[] maps = Directory.GetDirectories(mapsPath);
        if (maps.Length == 1)
            return Path.GetFileName(maps[0]);

        if (maps.Length > 1)
        {
            string[] mapNames = [.. maps.Select(Path.GetFileName).Where(name => name is not null).Select(name => name!)];
            int selection = Question.Ask(mapNames, 0, "Multiple maps found. Which one should be converted?");
            return mapNames[selection];
        }

        return null;
    }

    private static string ResolveTemplatePath()
    {
        const string defaultTemplate = "./Template";
        if (Directory.Exists(defaultTemplate))
            return defaultTemplate;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Default template folder './Template' not found. Please specify the template path manually.");
        Console.ResetColor();
        return Question.AskFolder("Enter the template folder path", true);
    }
}