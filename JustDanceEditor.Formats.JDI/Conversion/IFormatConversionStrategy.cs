using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.Formats.JDI.Conversion;

public interface IFormatConversionStrategy
{
    string FormatCode { get; }
    string FormatName { get; }
    IReadOnlyCollection<ConversionTargetDefinition> GetExportTargets();
    ConversionRequestBase CreateImportRequest(ConversionRequestContext context);
    ConversionRequestBase CreateExportRequest(ConversionRequestContext context);
}
