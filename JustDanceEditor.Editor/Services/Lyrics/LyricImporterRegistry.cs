using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Services.Lyrics;

/// <summary>
/// Central registry of registered <see cref="ILyricImporter"/> implementations.
/// Add new format importers here to make them available throughout the editor.
/// </summary>
public sealed class LyricImporterRegistry
{
    public static LyricImporterRegistry Instance => field ??= new LyricImporterRegistry();

    private readonly List<ILyricImporter> _importers;

    private LyricImporterRegistry()
    {
        // Order matters: Enhanced LRC must be checked before Standard LRC since both use .lrc.
        _importers =
        [
            new EnhancedLrcImporter(),
            new StandardLrcImporter(),
            new UltraStarImporter(),
        ];
    }

    /// <summary>All registered importers in priority order.</summary>
    public IReadOnlyList<ILyricImporter> Importers => _importers;

    /// <summary>
    /// Returns all distinct file extensions supported by any registered importer,
    /// suitable for a file picker filter.
    /// </summary>
    public IReadOnlyList<string> AllSupportedExtensions =>
        _importers
            .SelectMany(i => i.SupportedExtensions)
            .Distinct()
            .ToList();

    /// <summary>
    /// Picks the best importer for <paramref name="content"/> among importers that
    /// declare they support <paramref name="extension"/>.
    /// Returns <c>null</c> when no match is found.
    /// </summary>
    public ILyricImporter? Resolve(string extension, string content)
    {
        string ext = extension.TrimStart('.').ToLowerInvariant();
        return _importers
            .Where(i => i.SupportedExtensions.Contains(ext) && i.CanParse(content))
            .FirstOrDefault()
            // Fallback: any importer that supports the extension, even if CanParse is false.
            ?? _importers.FirstOrDefault(i => i.SupportedExtensions.Contains(ext));
    }
}