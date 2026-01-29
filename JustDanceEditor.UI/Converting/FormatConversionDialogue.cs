using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt;
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

        IJdiFormat sourceFormat = formats.First(f => f.DisplayName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));
        IJdiFormat targetFormat = formats.First(f => f.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        (ConversionRequestBase importRequest, ConversionRequestBase exportRequest) = BuildRequests(sourceName, targetName, inputPath);

        try
        {
            while (true)
            {
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

                    break; // Success - exit the retry loop
                }
                catch (MultipleSongsFoundException msEx)
                {
                    // Multi-song bundle detected - ask user to select
                    string[] songChoices = [.. msEx.AvailableSongs];
                    int songSelection = Question.Ask(songChoices, 0, "Multiple songs found in the bundle. Which one should be converted?");
                    string selectedSong = songChoices[songSelection];

                    // Update the request with the selected song and retry
                    if (importRequest is UbiArtConversionRequest ubiRequest)
                    {
                        ubiRequest.SongName = selectedSong;
                    }

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
        // Ask user for platform and engine version
        UbiArtPlatformType platform = AskUbiArtPlatform("Select the target platform for export");
        UbiArtEngineVersionType engineVersion = AskUbiArtEngineVersion("Select the target engine version for export");

        string? songName = ResolveUbiArtSongName(inputPath);
        UbiArtConversionRequest request = new(inputPath, outputPath, songName)
        {
            Type = platform == UbiArtPlatformType.Uncooked ? UbiArtType.Uncooked : UbiArtType.Cooked,
            ExportPlatform = platform,
            ExportEngineVersion = engineVersion
        };

        if (platform is UbiArtPlatformType.Uncooked
            or UbiArtPlatformType.NX
            or UbiArtPlatformType.WiiU
            or UbiArtPlatformType.Wii
            or UbiArtPlatformType.PC)
        {
            return request;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Note: Cooked export for {engineVersion} {platform} is experimental and may not be fully functional yet.");
        Console.ResetColor();

        return request;
    }

    private static UbiArtPlatformType AskUbiArtPlatform(string prompt)
    {
        string[] platforms = ["Uncooked", "Wii", "WiiU", "NX", "PC"];
        int selection = Question.Ask(platforms, 0, prompt);
        return (UbiArtPlatformType)selection;
    }

    private static UbiArtEngineVersionType AskUbiArtEngineVersion(string prompt)
    {
        string[] versions = [
            "JD2014", "JD2015",
            "JD2016", "JD2017", "JD2018",
            "JD2019", "JD2020", "JD2021", "JD2022"
        ];
        int selection = Question.Ask(versions, 0, prompt);
        return Enum.Parse<UbiArtEngineVersionType>(versions[selection]);
    }

    private static string? ResolveUbiArtSongName(string inputPath)
    {
        // Only try to detect from direct filesystem structure
        // Bundled/IPK files will be detected during import and handled via MultipleSongsFoundException
        string mapsPath = Path.Combine(inputPath, "world", "maps");
        if (!Directory.Exists(mapsPath))
            return null;

        string[] maps = Directory.GetDirectories(mapsPath);
        if (maps.Length == 0)
            return null;

        // Single map folder - auto-select it
        if (maps.Length == 1)
            return Path.GetFileName(maps[0]);

        // Multiple direct folders - ask the user (but this is the old behavior)
        // Note: If input is a bundle (IPK), this won't run, and the exception handler will catch it
        string[] mapNames = [.. maps.Select(Path.GetFileName).Where(name => name is not null).Select(name => name!)];
        if (mapNames.Length > 0)
        {
            int selection = Question.Ask(mapNames, 0, "Multiple maps found in direct filesystem. Which one should be converted?");
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