using System.Reflection;

namespace JustDanceEditor.Formats.JDI;

public sealed record JdiImportResult(
    IntermediateSongPackage Package,
    string SourceFormat,
    string? MaterializedRoot = null,
    bool MaterializedRootIsTemporary = false,
    string? SuggestedOutputFolder = null,
    object? SourceMetadata = null);

public interface IJdiFormat
{
    string DisplayName { get; }
    bool CanImport { get; }
    bool CanExport { get; }
    Task<JdiImportResult> ImportAsync(ConversionRequest request, CancellationToken cancellationToken = default);
    Task ExportAsync(JdiImportResult importResult, ConversionRequest request, CancellationToken cancellationToken = default);
}

public static class JdiFormatRegistry
{
    public static IReadOnlyCollection<IJdiFormat> GetFormats()
    {
        LoadFormatAssemblies();

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IJdiFormat).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            .Select(type => (IJdiFormat)Activator.CreateInstance(type)!)
            .ToList()
            .AsReadOnly();
    }

    private static void LoadFormatAssemblies()
    {
        try
        {
            string path = AppDomain.CurrentDomain.BaseDirectory;
            foreach (string dll in Directory.GetFiles(path, "JustDanceEditor.Formats.*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(dll);
                }
                catch
                {
                    // Ignore load failures
                }
            }
        }
        catch
        {
            // Ignore directory access failures
        }
    }
}
