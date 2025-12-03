using JustDanceEditor.Converter;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace JustDanceEditor.Formats.JDI;

public sealed record JdiImportResult(
    IntermediateSongPackage Package,
    JdiFormatKind SourceFormat,
    string? MaterializedRoot = null,
    bool MaterializedRootIsTemporary = false,
    string? SuggestedOutputFolder = null,
    object? SourceMetadata = null);

public interface IJdiFormat
{
    string DisplayName { get; }
    JdiFormatKind Kind { get; }
    bool CanImport { get; }
    bool CanExport { get; }
    Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default);
    Task ExportAsync(JdiImportResult importResult, ConversionRequest request, CancellationToken cancellationToken = default);
}

public static class JdiFormatRegistry
{
    private static readonly ConcurrentDictionary<JdiFormatKind, IJdiFormat> Formats = new();

    public static void RegisterFormat(IJdiFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!Formats.TryAdd(format.Kind, format))
            Formats[format.Kind] = format;
    }

    public static bool TryGetFormat(JdiFormatKind kind, out IJdiFormat format)
        => Formats.TryGetValue(kind, out format!);

    public static IReadOnlyCollection<IJdiFormat> GetFormats()
        => new ReadOnlyCollection<IJdiFormat>(Formats.Values.ToList());
}
