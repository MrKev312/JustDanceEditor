using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.Formats.Unity;

public sealed class UnityConversionStrategy : IFormatConversionStrategy
{
    private const string TemplatePathPromptId = "unity.templatePath";

    private static readonly ConversionTargetDefinition[] Targets =
    [
        new(
            FormatCode: "unity",
            FormatName: "Unity",
            TargetCode: "unity-custom-server",
            Platform: new PlatformDescriptor("switch", "Switch"),
            Version: new TargetVersionDescriptor.OpenEnded(2023, "JD2023+"),
            DisplayName: "JD2023+ (Unity)",
            ExportPrompts:
            [
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the Unity output root (custom server layout)",
                    Required: false),
                new ConversionPrompt(
                    TemplatePathPromptId,
                    ConversionPromptKind.FolderPath,
                    "Enter the Unity template folder path",
                    Required: true,
                    DefaultValue: "./Template",
                    MustExist: true)
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
        return new UnityConversionRequest(context.OutputPath, context.OutputPath, templatePath)
        {
            ExportType = ExportType.CustomServer
        };
    }

    private static string ResolveTemplatePath(PromptAnswerSet? answers)
    {
        if (answers != null && answers.TryGetString(TemplatePathPromptId, out string? templatePath) && !string.IsNullOrWhiteSpace(templatePath))
            return templatePath;

        const string defaultTemplate = "./Template";
        if (Directory.Exists(defaultTemplate))
            return defaultTemplate;

        throw new InvalidOperationException("Unity export requires a template folder path.");
    }
}
