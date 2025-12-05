using JustDanceEditor.Converter.Formats;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Logging;
using JustDanceEditor.UI.Helpers;

using System.Text.Json;

namespace JustDanceEditor.UI.Converting;

internal static class FormatConversionDialogue
{
    public static void Start()
    {
        IReadOnlyCollection<IJdiFormat> formats = JdiFormatRegistry.GetFormats();
        if (formats.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No format adapters are registered. Please ensure the converter assemblies are referenced correctly.");
            Console.ResetColor();
            return;
        }

        IJdiFormat[] sourceCandidates = [.. formats.Where(f => f.CanImport)];
        IJdiFormat[] targetCandidates = [.. formats.Where(f => f.CanExport)];

        if (sourceCandidates.Length == 0 || targetCandidates.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No compatible format combinations are available in this build.");
            Console.ResetColor();
            return;
        }

        string source = AskFormat("Select the source format", sourceCandidates);
        string target = AskFormat("Select the target format", targetCandidates);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("Source and target formats are identical. No conversion will be performed.");
            Console.ResetColor();
            return;
        }

        ConversionRequest request = CreateConversionRequest(source, target);

        FormatConversionService service = new();

        try
        {
            FormatConversionService.ConvertAsync(source, target, request).GetAwaiter().GetResult();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Conversion {source} → {target} completed successfully.");
            Console.ResetColor();
        }
        catch (NotImplementedException nie)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(nie.Message);
            Logger.Log(nie.Message, LogLevel.Warning);
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Conversion failed: {ex.Message}");
            Logger.Log($"Format conversion failed: {ex}", LogLevel.Error);
            Console.ResetColor();
        }
    }

    private static string AskFormat(string prompt, IEnumerable<IJdiFormat> candidates)
    {
        // Order by DisplayName, but prioritize "JDI" to be first if present
        IJdiFormat[] options = [.. candidates
            .OrderBy(f => f.DisplayName.Equals("JDI", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f.DisplayName)];

        if (options.Length == 0)
            throw new InvalidOperationException("No formats satisfy the requested capability.");

        string[] labels = [.. options.Select(f => f.DisplayName)];
        int selection = Question.Ask(labels, 0, prompt);
        return options[selection].DisplayName;
    }

    private static ConversionRequest CreateConversionRequest(string source, string target)
    {
        ConversionRequest request = source switch
        {
            "UbiArt" => ConverterDialogue.CreateConversionRequest(),
            _ => CreateGenericRequest(source, target)
        };

        if (source == "UbiArt" && target == "Unity")
        {
            ConfigureUbiArtToUnityRequest(request);
        }

        return request;
    }

    private static void ConfigureUbiArtToUnityRequest(ConversionRequest request)
    {
        bool configureAdvanced = Question.AskYesNo("Would you like to configure advanced cache/JD version options?");
        if (!configureAdvanced)
            return;

        request.CacheNumber = (uint)Question.AskNumber("Enter the target cache number for this song (e.g., 1, 123)", (int)(request.CacheNumber ?? 1));
        uint version = (uint)Question.AskNumber("Optionally force a specific JDVersion (e.g., 2019, 2022). Enter 0 for automatic detection.", (int)(request.JDVersion ?? 0));
        request.JDVersion = version == 0 ? null : version;
    }

    private static ConversionRequest CreateGenericRequest(string source, string target)
    {
        string inputPrompt = source switch
        {
            "Unity" => "Enter the Unity Custom Server export folder (must contain SongInfo.json)",
            "JDI" => "Enter the folder that contains metadata.json for the JDI package",
            _ => "Enter the input folder for this conversion"
        };

        string inputPath = Question.AskFolder(inputPrompt, true);

        string outputPrompt = target switch
        {
            "JDI" => "Enter the folder where the JDI package should be written",
            "Unity" => "Enter the Unity output root (existing cache/custom server structure)",
            _ => "Enter the destination folder for the converted files"
        };

        string outputPath = Question.AskFolder(outputPrompt, false);
        Directory.CreateDirectory(outputPath);

        ExportType exportType = ExportType.CustomServer;

        if (target == "Unity")
        {
            Console.WriteLine("Unity conversions currently support only the Custom Server folder layout.");
            WarnIfOfflineCacheDetected(outputPath);
        }

        ConversionRequest request = new()
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            TemplatePath = ResolveTemplatePath(),
            ExportType = exportType,
            OnlineCover = target == "Unity" && Question.AskYesNo("Attempt to download missing covers from the internet?")
        };

        if (source == "JDI")
        {
            string? inferredName = TryInferSongNameFromIntermediate(inputPath);
            if (!string.IsNullOrWhiteSpace(inferredName))
                request.SongName = inferredName;
            else
                throw new Exception("Failed to infer song name from JDI metadata. Please ensure metadata.json is present and valid.");
        }

        return request;
    }

    static readonly JsonSerializerOptions IntermediateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string? TryInferSongNameFromIntermediate(string inputPath)
    {
        try
        {
            string metadataPath = Path.Combine(inputPath, "metadata.json");
            if (!File.Exists(metadataPath))
                return null;

            using FileStream json = File.OpenRead(metadataPath);
            IntermediateMetadata? metadata = JsonSerializer.Deserialize<IntermediateMetadata>(json, IntermediateJsonOptions);
            if (metadata == null)
                return null;

            return string.IsNullOrWhiteSpace(metadata.MapName) ? metadata.Title : metadata.MapName;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to infer map name from intermediate metadata: {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    private static void WarnIfOfflineCacheDetected(string outputPath)
    {
        string cacheStatusPath = Path.Combine(outputPath, "SD_Cache.0000", "MapBaseCache", "cachingStatus.json");
        if (File.Exists(cacheStatusPath))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Warning: Offline cache layout detected. Unity conversions currently support only the Custom Server format.");
            Console.ResetColor();
        }
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