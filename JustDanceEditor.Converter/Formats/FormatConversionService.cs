using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Converter.Formats;

public class FormatConversionService
{
    public async Task ConvertAsync(JdiFormatKind source, JdiFormatKind target, ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (source == target)
            return;

        IJdiFormat sourceFormat = ResolveFormat(source);
        IJdiFormat targetFormat = ResolveFormat(target);

        if (!sourceFormat.CanImport)
            throw new NotSupportedException($"Format '{sourceFormat.DisplayName}' cannot be used as a conversion source.");
        if (!targetFormat.CanExport)
            throw new NotSupportedException($"Format '{targetFormat.DisplayName}' cannot be used as a conversion target.");

        JdiImportResult importResult = await sourceFormat.ImportAsync(request);

        try
        {
            await targetFormat.ExportAsync(importResult, request);
        }
        finally
        {
            if (importResult.MaterializedRootIsTemporary)
                JdiConversionHelpers.TryDeleteDirectory(importResult.MaterializedRoot);
        }
    }

    private static IJdiFormat ResolveFormat(JdiFormatKind kind)
    {
        if (JdiFormatRegistry.TryGetFormat(kind, out IJdiFormat? format))
            return format;

        throw new NotSupportedException($"No converter registered for format '{kind}'.");
    }
}
