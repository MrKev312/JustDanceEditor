using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.Formats.JDI.Conversion;

public sealed class JdiConversionStrategy : IFormatConversionStrategy
{
    private static readonly ConversionTargetDefinition[] Targets =
    [
        new(
            FormatCode: "jdi",
            FormatName: "JDI",
            TargetCode: "jdi",
            Platform: new PlatformDescriptor("intermediate", "Intermediate"),
            Version: new TargetVersionDescriptor.None("JDI package"),
            DisplayName: "JDI package",
            ExportPrompts:
            [
                new ConversionPrompt(
                    ConversionPromptIds.OutputPath,
                    ConversionPromptKind.FolderPath,
                    "Enter the folder where the JDI package should be written",
                    Required: false)
            ],
            Priority: 0)
    ];

    public string FormatCode => "jdi";
    public string FormatName => "JDI";

    public IReadOnlyCollection<ConversionTargetDefinition> GetExportTargets() => Targets;

    public ConversionRequestBase CreateImportRequest(ConversionRequestContext context) =>
        new JdiConversionRequest(context.InputPath, context.OutputPath);

    public ConversionRequestBase CreateExportRequest(ConversionRequestContext context) =>
        new JdiConversionRequest(context.InputPath, context.OutputPath);
}
