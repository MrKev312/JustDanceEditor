using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JustDanceEditor.Editor.Services.Lyrics;

/// <summary>
/// Parses Enhanced LRC files where each line may contain word-level inline timestamps:
/// <code>[MM:SS.xx] &lt;MM:SS.xx&gt;Word1 &lt;MM:SS.xx&gt;Word2 &lt;MM:SS.xx&gt;</code>
/// Falls back to one clip per line when no inline timestamps are present on a line.
/// </summary>
public sealed partial class EnhancedLrcImporter : ILyricImporter
{
    // Line-level LRC timestamp: [MM:SS.xx] or [MM:SS.xxx]
    [GeneratedRegex(@"^\[(\d{1,2}):(\d{2})\.(\d{2,3})\](.*)$", RegexOptions.Compiled)]
    private static partial Regex LineTimestampRegex();

    // Inline word timestamp: <MM:SS.xx> or <MM:SS.xxx>
    [GeneratedRegex(@"<(\d{1,2}):(\d{2})\.(\d{2,3})>([^<]*)", RegexOptions.Compiled)]
    private static partial Regex WordTimestampRegex();

    // Metadata tags: [ti:Title] etc.
    [GeneratedRegex(@"^\[[a-zA-Z]+:.*\]$", RegexOptions.Compiled)]
    private static partial Regex MetadataRegex();

    public string Name => "Enhanced LRC";
    public IReadOnlyList<string> SupportedExtensions { get; } = ["lrc"];

    /// <summary>
    /// Returns true when the content contains word-level &lt;MM:SS.xx&gt; inline tags.
    /// </summary>
    public bool CanParse(string content) =>
        content.Contains('<') && WordTimestampRegex().IsMatch(content);

    public IReadOnlyList<LyricLine> Parse(string content)
    {
        // First pass: collect all lines with their start times and raw content.
        List<(double LineStart, string RawContent)> rawLines = [];

        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;
            if (MetadataRegex().IsMatch(trimmed))
                continue;

            Match m = LineTimestampRegex().Match(trimmed);
            if (!m.Success)
                continue;

            double lineStart = ParseTimestamp(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            rawLines.Add((lineStart, m.Groups[4].Value.Trim()));
        }

        rawLines.Sort((a, b) => a.LineStart.CompareTo(b.LineStart));

        List<LyricLine> result = [];

        for (int li = 0; li < rawLines.Count; li++)
        {
            (double lineStart, string rawContent) = rawLines[li];
            double nextLineStart = li + 1 < rawLines.Count ? rawLines[li + 1].LineStart : lineStart;

            MatchCollection wordMatches = WordTimestampRegex().Matches(rawContent);

            if (wordMatches.Count == 0)
            {
                // No inline tags – behave like standard LRC.
                result.Add(new LyricLine(lineStart, nextLineStart, rawContent, IsEndOfLine: true));
                continue;
            }

            // Build word list: each match gives us a timestamp and the text that follows it.
            List<(double Start, string Text)> words = [];
            foreach (Match wm in wordMatches)
            {
                double wStart = ParseTimestamp(wm.Groups[1].Value, wm.Groups[2].Value, wm.Groups[3].Value);
                string wText = wm.Groups[4].Value.Trim();
                if (!string.IsNullOrEmpty(wText))
                    words.Add((wStart, wText));
            }

            for (int wi = 0; wi < words.Count; wi++)
            {
                bool isLastInLine = wi == words.Count - 1;
                double wEnd = isLastInLine ? nextLineStart : words[wi + 1].Start;
                result.Add(new LyricLine(words[wi].Start, wEnd, words[wi].Text, IsEndOfLine: isLastInLine));
            }
        }

        return result;
    }

    private static double ParseTimestamp(string mm, string ss, string frac)
    {
        int minutes = int.Parse(mm);
        int seconds = int.Parse(ss);
        double fraction = frac.Length == 2
            ? int.Parse(frac) / 100.0
            : int.Parse(frac) / 1000.0;
        return (minutes * 60) + seconds + fraction;
    }
}