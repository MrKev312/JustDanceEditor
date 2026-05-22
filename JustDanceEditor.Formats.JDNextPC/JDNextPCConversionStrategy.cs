using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Conversion;

namespace JustDanceEditor.Formats.JDNextPC;

public sealed class JDNextPCConversionStrategy : IFormatConversionStrategy
{
    private static readonly ConversionTargetDefinition[] Targets =
    [
        new(
            FormatCode: "jdnext-pc",
            FormatName: "JDNext PC",
            TargetCode: "jdnext-pc",
            Platform: new PlatformDescriptor("pc", "PC"),
            Version: new TargetVersionDescriptor.Custom("jdnext-pc", "JDNext PC", 20),
            DisplayName: "JDNext PC",
            ExportPrompts:
            [
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the folder where the JDNext PC song folder should be written",
                    Required: false)
            ],
            Priority: 20)
    ];

    public string FormatCode => "jdnext-pc";
    public string FormatName => "JDNext PC";

    public IReadOnlyCollection<ConversionTargetDefinition> GetExportTargets() => Targets;

    public ConversionRequestBase CreateImportRequest(ConversionRequestContext context) =>
        new JDNextPCConversionRequest(context.InputPath, context.OutputPath);

    public ConversionRequestBase CreateExportRequest(ConversionRequestContext context) =>
        new JDNextPCConversionRequest(context.InputPath, context.OutputPath);
}