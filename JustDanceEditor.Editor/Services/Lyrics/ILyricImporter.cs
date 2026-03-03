// File: .\Services\Lyrics\ILyricImporter.cs
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services.Lyrics;

/// <summary>
/// Parses a lyric / subtitle file format into a flat list of <see cref="LyricLine"/>s
/// ready to be inserted into a JDE timeline.
/// </summary>
public interface ILyricImporter
{
    /// <summary>Human-readable name, e.g. "LRC" or "Enhanced LRC".</summary>
    string Name { get; }

    /// <summary>
    /// File extensions (lower-case, without leading dot) that this importer handles,
    /// e.g. <c>["lrc"]</c>.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Returns <c>true</c> when the importer can handle <paramref name="content"/>.
    /// Used to auto-detect the correct importer when multiple formats share the same
    /// extension (e.g. standard vs. enhanced LRC both use <c>.lrc</c>).
    /// </summary>
    bool CanParse(string content);

    /// <summary>
    /// Parses <paramref name="content"/> and returns an ordered list of syllable records.
    /// </summary>
    IReadOnlyList<LyricLine> Parse(string content);
}