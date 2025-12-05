using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Converter.Formats;

public class FormatConversionService
{
    public static async Task ConvertAsync(string sourceFormatName, string targetFormatName, ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(sourceFormatName, targetFormatName, StringComparison.OrdinalIgnoreCase))
            return;

        IJdiFormat sourceFormat = ResolveFormat(sourceFormatName);
        IJdiFormat targetFormat = ResolveFormat(targetFormatName);

        if (!sourceFormat.CanImport)
            throw new NotSupportedException($"Format '{sourceFormat.DisplayName}' cannot be used as a conversion source.");
        if (!targetFormat.CanExport)
            throw new NotSupportedException($"Format '{targetFormat.DisplayName}' cannot be used as a conversion target.");

        JdiImportResult importResult = await sourceFormat.ImportAsync(request);

        try
        {
            await targetFormat.ExportAsync(importResult, request);
        }
        catch
        {
            // If export fails, we still want to clean up any temporary materialized roots.
            throw;
        }
        finally
        {
            if (importResult.MaterializedRootIsTemporary && importResult.MaterializedRoot is not null
                && Directory.Exists(importResult.MaterializedRoot))
                Directory.Delete(importResult.MaterializedRoot, true);
        }
    }

    private static IJdiFormat ResolveFormat(string formatName)
    {
        IJdiFormat? format = JdiFormatRegistry.GetFormats()
            .FirstOrDefault(f => string.Equals(f.DisplayName, formatName, StringComparison.OrdinalIgnoreCase));

        if (format != null)
            return format;

        throw new NotSupportedException($"No converter registered for format '{formatName}'.");
    }
}