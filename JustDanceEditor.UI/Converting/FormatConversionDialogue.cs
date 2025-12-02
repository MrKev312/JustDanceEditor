using System.Text.Json;

using JustDanceEditor.Converter;
using JustDanceEditor.Converter.Formats;
using JustDanceEditor.Formats.Intermediate.Metadata;
using JustDanceEditor.Logging;
using JustDanceEditor.UI.Helpers;

namespace JustDanceEditor.UI.Converting;

internal static class FormatConversionDialogue
{
    public static void Start()
    {
        FormatKind source = AskFormat("Select the source format");
        FormatKind target = AskFormat("Select the target format");

        if (source == target)
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
            service.ConvertAsync(source, target, request).GetAwaiter().GetResult();
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

    private static FormatKind AskFormat(string prompt)
    {
        string[] options = Enum.GetNames<FormatKind>();
        int selection = Question.Ask(options, 0, prompt);
        return Enum.Parse<FormatKind>(options[selection]);
    }

    private static ConversionRequest CreateConversionRequest(FormatKind source, FormatKind target)
    {
        ConversionRequest request = source switch
        {
            FormatKind.UbiArt => ConverterDialogue.CreateConversionRequest(),
            _ => CreateGenericRequest(source, target)
        };

        switch (source, target)
        {
            case (FormatKind.UbiArt, FormatKind.Unity):
                ConfigureUbiArtToUnityRequest(request);
                break;
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

    private static ConversionRequest CreateGenericRequest(FormatKind source, FormatKind target)
    {
        string inputPrompt = source switch
        {
            FormatKind.Unity => "Enter the Unity Custom Server export folder (must contain SongInfo.json)",
            FormatKind.JDI => "Enter the folder that contains manifest.json for the JDI package",
            _ => "Enter the input folder for this conversion"
        };

        string inputPath = Question.AskFolder(inputPrompt, true);

        string outputPrompt = target switch
        {
            FormatKind.JDI => "Enter the folder where the JDI package should be written",
            FormatKind.Unity => "Enter the Unity output root (existing cache/custom server structure)",
            _ => "Enter the destination folder for the converted files"
        };

        string outputPath = Question.AskFolder(outputPrompt, false);
        Directory.CreateDirectory(outputPath);

        ExportType exportType = ExportType.CustomServer;

        if (target == FormatKind.Unity)
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
            OnlineCover = target == FormatKind.Unity && Question.AskYesNo("Attempt to download missing covers from the internet?")
        };

        if (source == FormatKind.JDI)
        {
            string? inferredName = TryInferSongNameFromIntermediate(inputPath);
            if (!string.IsNullOrWhiteSpace(inferredName))
                request.SongName = inferredName;
            else
                request.SongName = AskSongName();
        }
        else if (source != FormatKind.Unity)
        {
            request.SongName = AskSongName();
        }

        return request;
    }

    private static string AskSongName()
    {
        Console.Write("Enter the song/map codename (e.g., mapname_00): ");
        string? value = Console.ReadLine()?.Trim();
        while (string.IsNullOrWhiteSpace(value))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Song/map codename cannot be empty.");
            Console.ResetColor();
            Console.Write("Enter the song/map codename: ");
            value = Console.ReadLine()?.Trim();
        }

        return value;
    }

    private static string? TryInferSongNameFromIntermediate(string inputPath)
    {
        try
        {
            string? folder = LocateIntermediateFolder(inputPath);
            if (folder == null)
                return null;

            string metadataPath = Path.Combine(folder, "metadata.json");
            if (!File.Exists(metadataPath))
                return null;

            string json = File.ReadAllText(metadataPath);
            IntermediateMetadata? metadata = JsonSerializer.Deserialize<IntermediateMetadata>(json);
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

    private static string? LocateIntermediateFolder(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return null;

        string manifestPath = Path.Combine(inputPath, "manifest.json");
        if (File.Exists(manifestPath))
            return inputPath;

        string nested = Path.Combine(inputPath, "Intermediate");
        if (File.Exists(Path.Combine(nested, "manifest.json")))
            return nested;

        try
        {
            foreach (string manifest in Directory.EnumerateFiles(inputPath, "manifest.json", SearchOption.AllDirectories))
            {
                string? folder = Path.GetDirectoryName(manifest);
                if (!string.IsNullOrEmpty(folder))
                    return folder;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore folders we cannot read.
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        return null;
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
