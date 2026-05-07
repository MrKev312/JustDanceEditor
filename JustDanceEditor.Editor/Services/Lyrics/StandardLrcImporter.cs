using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Editor.Services.Lyrics;

/// <summary>
/// Parses standard LRC files where each line has the format:
/// <code>[MM:SS.xx] Lyric text here</code>
/// </summary>
public sealed partial class StandardLrcImporter : ILyricImporter
{
    // Matches [MM:SS.xx] or [MM:SS.xxx] – LRC timestamp at the start of a line.
    [GeneratedRegex(@"^\[(\d{1,2}):(\d{2})\.(\d{2,3})\](.*)$", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    // Metadata tags such as [ti:Title] or [ar:Artist] – skip these.
    [GeneratedRegex(@"^\[[a-zA-Z]+:.*\]$", RegexOptions.Compiled)]
    private static partial Regex MetadataRegex();

    public string Name => "LRC";
    public IReadOnlyList<string> SupportedExtensions { get; } = ["lrc"];

    /// <summary>
    /// Returns true when there are no word-level &lt;MM:SS.xx&gt; tags,
    /// distinguishing this from Enhanced LRC.
    /// </summary>
    public bool CanParse(string content) =>
        !content.Contains('<') && content.Contains('[');

    public IReadOnlyList<LyricLine> Parse(string content)
    {
        List<(double Start, string Text)> raw = [];

        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (MetadataRegex().IsMatch(trimmed))
                continue;

            Match m = TimestampRegex().Match(trimmed);
            if (!m.Success)
                continue;

            int minutes = int.Parse(m.Groups[1].Value);
            int seconds = int.Parse(m.Groups[2].Value);
            string fracStr = m.Groups[3].Value;
            double fraction = fracStr.Length == 2
                ? int.Parse(fracStr) / 100.0
                : int.Parse(fracStr) / 1000.0;

            double startSec = (minutes * 60) + seconds + fraction;
            string text = m.Groups[4].Value.Trim();

            raw.Add((startSec, text));
        }

        // Sort by start time (some LRC files are out of order).
        raw.Sort((a, b) => a.Start.CompareTo(b.Start));
        // Drop empty-text entries that aren't markers (except we still want
        // end-of-line markers to be preserved as empty clips if needed).
        // Remove pure metadata lines that slipped through.

        List<LyricLine> result = [];
        for (int i = 0; i < raw.Count; i++)
        {
            (double start, string text) = raw[i];
            bool isLast = i == raw.Count - 1;
            double end = isLast ? start : raw[i + 1].Start;
            result.Add(new LyricLine(start, end, text, IsEndOfLine: true));
        }

        return result;
    }
}