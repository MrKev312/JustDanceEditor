using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.Formats.Unity;

public sealed class UnityConversionStrategy : IFormatConversionStrategy
{
    private const string CustomServerTargetCode = "unity-custom-server";
    private const string OfflineCacheTargetCode = "unity-offline-cache";

    private static readonly ConversionTargetDefinition[] Targets =
    [
        new(
            FormatCode: "unity",
            FormatName: "Unity",
            TargetCode: CustomServerTargetCode,
            Platform: new PlatformDescriptor("pc", "PC"),
            Version: new TargetVersionDescriptor.OpenEnded(2023, "JD2023+"),
            DisplayName: "JDNext Server",
            ExportPrompts:
            [
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the Unity output root (custom server layout)",
                    Required: false),
                new ConversionPrompt(
                    UnityPromptIds.TemplatePath,
                    ConversionPromptKind.FolderPath,
                    "Enter the Unity template folder path",
                    Required: true,
                    DefaultValue: "./Template",
                    MustExist: true)
            ],
            Priority: 50),
        new(
            FormatCode: "unity",
            FormatName: "Unity",
            TargetCode: OfflineCacheTargetCode,
            Platform: new PlatformDescriptor("switch", "NX (Nintendo Switch)"),
            Version: new TargetVersionDescriptor.OpenEnded(2023, "JD2023+"),
            DisplayName: "JD2023+ (Unity)",
            ExportPrompts:
            [
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the Unity cache root or any folder inside the SD_Cache tree",
                    Required: false),
                new ConversionPrompt(
                    UnityPromptIds.TemplatePath,
                    ConversionPromptKind.FolderPath,
                    "Enter the Unity template folder path",
                    Required: true,
                    DefaultValue: "./Template",
                    MustExist: true),
                new ConversionPrompt(
                    UnityPromptIds.CacheNumber,
                    ConversionPromptKind.Integer,
                    "Enter the runtime SD_Cache number",
                    Required: false,
                    DefaultValue: "1")
            ],
            Priority: 50)
    ];

    public string FormatCode => "unity";
    public string FormatName => "Unity";

    public IReadOnlyCollection<ConversionTargetDefinition> GetExportTargets() => Targets;

    public ConversionRequestBase CreateImportRequest(ConversionRequestContext context)
    {
        return new UnityConversionRequest(context.InputPath, context.OutputPath, context.OutputPath)
        {
            ExportType = ExportType.CustomServer
        };
    }

    public ConversionRequestBase CreateExportRequest(ConversionRequestContext context)
    {
        string templatePath = ResolveTemplatePath(context.Answers);
        bool offlineCache = context.Target?.TargetCode.Equals(OfflineCacheTargetCode, StringComparison.OrdinalIgnoreCase) == true;
        return new UnityConversionRequest(context.InputPath, context.OutputPath, templatePath)
        {
            ExportType = offlineCache ? ExportType.OfflineCache : ExportType.CustomServer,
            CacheNumber = offlineCache ? ResolveCacheNumber(context.Answers) : null,
            GenerateCacheIfMissing = ResolveGenerateCacheIfMissing(context.Answers),
            Interaction = context.Interaction
        };
    }

    private static string ResolveTemplatePath(PromptAnswerSet? answers)
    {
        if (answers != null && answers.TryGetString(UnityPromptIds.TemplatePath, out string? templatePath) && !string.IsNullOrWhiteSpace(templatePath))
            return templatePath;

        const string defaultTemplate = "./Template";
        if (Directory.Exists(defaultTemplate))
            return defaultTemplate;

        throw new InvalidOperationException("Unity export requires a template folder path.");
    }

    private static uint ResolveCacheNumber(PromptAnswerSet? answers)
    {
        if (answers == null || !answers.TryGetString(UnityPromptIds.CacheNumber, out string? cacheNumberRaw) || string.IsNullOrWhiteSpace(cacheNumberRaw))
            return 1;

        if (!uint.TryParse(cacheNumberRaw, out uint cacheNumber) || cacheNumber == 0)
            throw new ArgumentException("Unity offline cache number must be a positive integer.");

        return cacheNumber;
    }

    private static bool? ResolveGenerateCacheIfMissing(PromptAnswerSet? answers)
    {
        if (answers == null || !answers.TryGetString(UnityPromptIds.GenerateCacheIfMissing, out string? raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        if (!bool.TryParse(raw, out bool value))
            throw new ArgumentException($"'{UnityPromptIds.GenerateCacheIfMissing}' must be true or false.");

        return value;
    }
}
