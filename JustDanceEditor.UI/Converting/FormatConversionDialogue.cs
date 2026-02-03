using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt;
using JustDanceEditor.Formats.Unity;
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

        // Ask for target platform/version (unified flow)
        TargetSelection target = PlatformVersionSelector.AskTarget();

        IJdiFormat sourceFormat = formats.First(f => f.DisplayName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));

        // Determine target format based on selection
        string targetName = target.FormatName;
        IJdiFormat targetFormat = formats.First(f => f.DisplayName.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        (ConversionRequestBase importRequest, ConversionRequestBase exportRequest) = BuildRequests(sourceName, target, inputPath);

        // Ask about downloading online assets BEFORE conversion
        bool downloadOnlineAssets = Question.Ask(["Yes", "No"], 0, "Download online assets for this song?") == 0;

        try
        {
            while (true)
            {
                try
                {
                    JdiImportResult importResult = sourceFormat.ImportAsync(importRequest).GetAwaiter().GetResult();

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

    private static (ConversionRequestBase importRequest, ConversionRequestBase exportRequest) BuildRequests(string source, TargetSelection target, string inputPath)
    {
        string targetName = target.FormatName;
        string outputPath = AskOutputPath(target);

        string intermediatePath = target.IsJdi ? outputPath : Path.Combine(Path.GetTempPath(), "JustDanceEditor", "JDI", Path.GetFileName(inputPath) ?? "Export");

        ConversionRequestBase importRequest = source switch
        {
            "UbiArt" => BuildUbiArtImportRequest(inputPath, intermediatePath),
            "Unity" => BuildUnityImportRequest(inputPath, intermediatePath, targetName),
            "JDI" => new JdiConversionRequest(inputPath, intermediatePath),
            _ => throw new NotSupportedException($"Unknown source format '{source}'.")
        };

        ConversionRequestBase exportRequest = target.IsJdi
            ? new JdiConversionRequest(inputPath, outputPath)
            : target.IsUnityEngine
                ? BuildUnityExportRequest(outputPath)
                : BuildUbiArtExportRequest(inputPath, outputPath, target);

        return (importRequest, exportRequest);
    }

    private static string AskOutputPath(TargetSelection target)
    {
        string prompt = target.IsUnityEngine
            ? "Enter the Unity output root (custom server layout)"
            : target.IsJdi
                ? "Enter the folder where the JDI package should be written"
                : "Enter the destination folder for the converted files";

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
            ExportType = ExportType.CustomServer
        };

        return request;
    }

    private static ConversionRequestBase BuildUnityExportRequest(string outputPath)
    {
        string templatePath = ResolveTemplatePath();

        UnityConversionRequest request = new(outputPath, outputPath, templatePath)
        {
            ExportType = ExportType.CustomServer
        };

        return request;
    }

    private static ConversionRequestBase BuildUbiArtExportRequest(string inputPath, string outputPath, TargetSelection target)
    {
        if (target.IsUnityEngine)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Note: JD2023+ uses the Unity engine. Use Unity export instead.");
            Console.ResetColor();
            throw new NotSupportedException("Cannot export to UbiArt format for JD2023+. Use Unity format.");
        }

        string? songName = ResolveUbiArtSongName(inputPath);
        UbiArtConversionRequest request = new(inputPath, outputPath, songName)
        {
            Type = target.Platform == TargetPlatform.Uncooked ? CookedType.Uncooked : CookedType.Cooked,
            ExportPlatform = target.ToUbiArtPlatform(),
            ExportEngineVersion = target.ToUbiArtEngineVersion()
        };

        return request;
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