// File: .\Services\Lyrics\UltraStarImporter.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace JustDanceEditor.Editor.Services.Lyrics;

/// <summary>
/// Parses UltraStar TXT lyric files.
/// </summary>
/// <remarks>
/// Supports both absolute and relative (#RELATIVE:yes) timestamp modes.
/// Note types <c>:</c> (normal), <c>*</c> (golden) and <c>F</c> (freestyle/rap)
/// are all treated as audible syllables. Line-break (<c>-</c>) records mark the
/// last syllable of a line as <see cref="LyricLine.IsEndOfLine"/>.
/// </remarks>
public sealed class UltraStarImporter : ILyricImporter
{
    private static readonly char[] SplitChars = [' ', '\t'];

    public string Name => "UltraStar";
    public IReadOnlyList<string> SupportedExtensions { get; } = ["txt"];

    public bool CanParse(string content) =>
        content.Contains("#BPM:", StringComparison.OrdinalIgnoreCase) &&
        content.Contains("#GAP:", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<LyricLine> Parse(string content)
    {
        double bpm = 0;
        double gapMs = 0;
        bool isRelative = false;

        // Phase 1: collect raw notes (no tilde processing yet).
        List<(double StartSec, double EndSec, string Text, bool IsEol)> raw = [];

        List<(double StartSec, double EndSec, string Text)> currentLine = [];
        double lineOffsetSec = 0;

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').TrimStart();
            if (string.IsNullOrEmpty(line))
                continue;

            // Header metadata.
            if (line.StartsWith('#'))
            {
                int colon = line.IndexOf(':');
                if (colon < 0)
                    continue;

                string key = line[1..colon].Trim().ToUpperInvariant();
                string value = line[(colon + 1)..].Trim();

                switch (key)
                {
                    case "BPM":
                        bpm = double.Parse(value.Replace(',', '.'), CultureInfo.InvariantCulture);
                        break;
                    case "GAP":
                        gapMs = double.Parse(value.Replace(',', '.'), CultureInfo.InvariantCulture);
                        break;
                    case "RELATIVE":
                        isRelative = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                        break;
                }

                continue;
            }

            if (bpm <= 0)
                continue;

            // End marker.
            if (line.StartsWith('E'))
            {
                FlushLine(currentLine, raw);
                currentLine.Clear();
                break;
            }

            // Line break.
            if (line.StartsWith('-'))
            {
                string[] parts = line[1..].Trim().Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
                if (isRelative && parts.Length >= 1 &&
                    double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double lineBreakBeat))
                {
                    lineOffsetSec += BeatsToSeconds(lineBreakBeat, bpm);
                }

                FlushLine(currentLine, raw);
                currentLine.Clear();
                continue;
            }

            // Note line.
            char noteType = line[0];
            if (noteType is not ':' and not '*' and not 'F')
                continue;

            string[] allTokens = line[1..].Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
            if (allTokens.Length < 3)
                continue;

            if (!double.TryParse(allTokens[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double startBeat))
                continue;
            if (!double.TryParse(allTokens[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double durationBeats))
                continue;

            string syllable = ExtractSyllable(line, 3);

            double offsetSec = (gapMs / 1000.0) + (isRelative ? lineOffsetSec : 0.0);
            double startSec = offsetSec + BeatsToSeconds(startBeat, bpm);
            double endSec = startSec + BeatsToSeconds(durationBeats, bpm);

            currentLine.Add((startSec, endSec, syllable));
        }

        FlushLine(currentLine, raw);

        // Phase 2: resolve tilde runs.
        // A "tilde note" is one whose trimmed text starts with exactly one ~ (not ~~).
        // First ~ in any run: split the last char off previous note proportionally
        //   (that char becomes "long"), then append any text after the ~.
        // Each subsequent ~ in the run: add "-{last char}" with the tilde note's own timing.
        // "~~..." (literal double-tilde): kept verbatim.
        List<(double StartSec, double EndSec, string Text, bool IsEol)> results = [with(raw.Count)];

        for (int i = 0; i < raw.Count; i++)
        {
            (double startSec, double endSec, string text, bool isEol) note = raw[i];
            string trimmed = note.text.TrimStart();
            string leading = note.text.Length > trimmed.Length ? " " : string.Empty;

            if (!trimmed.StartsWith('~') || trimmed.StartsWith("~~"))
            {
                // Normal note or literal "~~..." – add as-is.
                results.Add(note);
                continue;
            }

            // Count how many consecutive tilde notes follow (including this one).
            int runLen = 1;
            while (i + runLen < raw.Count)
            {
                string nextTrimmed = raw[i + runLen].Text.TrimStart();
                if (nextTrimmed.StartsWith('~') && !nextTrimmed.StartsWith("~~"))
                    runLen++;
                else
                    break;
            }

            string prevText = results.Count > 0 ? results[^1].Text : string.Empty;
            char repeatChar = prevText.Length > 0 ? prevText[^1] : '\0';
            string afterFirstTilde = trimmed[1..]; // text after the leading ~

            // First tilde: split the last char off the previous note proportionally.
            if (prevText.Length > 1 && results.Count > 0)
            {
                (double StartSec, double EndSec, string Text, bool IsEol) = results[^1];
                double splitPoint = StartSec + ((EndSec - StartSec)
                    * (prevText.Length - 1) / prevText.Length);

                results[^1] = (StartSec, splitPoint, prevText[..^1], IsEol);
                results.Add((splitPoint, note.endSec, leading + repeatChar + afterFirstTilde, note.isEol));
            }
            else
            {
                // Single-char previous or no previous – just reuse the char without splitting.
                results.Add((note.startSec, note.endSec, leading + prevText + afterFirstTilde, note.isEol));
            }

            // Remaining tildes in the run: each becomes "-{char}" with the note's own timing.
            for (int j = 1; j < runLen; j++)
            {
                (double StartSec, double EndSec, string Text, bool IsEol) = raw[i + j];
                string curTrimmed = Text.TrimStart();
                string curLeading = Text.Length > curTrimmed.Length ? " " : string.Empty;
                string curAfter = curTrimmed[1..];
                string syllable = repeatChar != '\0'
                    ? curLeading + "-" + repeatChar + curAfter
                    : curLeading + curAfter;
                results.Add((StartSec, EndSec, syllable, IsEol));
            }

            i += runLen - 1; // outer loop will i++ once more
        }

        // Phase 3: normalize space placement – spaces always belong at the END of a syllable.
        // If a syllable has a leading space, move it to the trailing position of the previous
        // syllable. If the previous already ends with a space, discard the duplicate.
        for (int i = 0; i < results.Count; i++)
        {
            string text = results[i].Text;
            if (text.Length > 0 && text[0] == ' ')
            {
                if (i > 0)
                {
                    (double StartSec, double EndSec, string Text, bool IsEol) = results[i - 1];
                    if (!Text.EndsWith(' '))
                        results[i - 1] = (StartSec, EndSec, Text + ' ', IsEol);
                }

                results[i] = (results[i].StartSec, results[i].EndSec, text.TrimStart(), results[i].IsEol);
            }
        }

        List<LyricLine> output = [with(results.Count)];
        foreach ((double startSec, double endSec, string? text, bool isEol) in results)
            output.Add(new LyricLine(startSec, endSec, text, isEol));

        return output;
    }

    /// <summary>
    /// Converts UltraStar inner beats to seconds.
    /// UltraStar BPM counts quarter-notes, and the internal beat resolution is × 4,
    /// so: <c>seconds = beats / (BPM × 4) × 60</c>.
    /// </summary>
    private static double BeatsToSeconds(double beats, double bpm) =>
        beats * 60.0 / (bpm * 4.0);

    /// <summary>
    /// Extracts the syllable text from a raw note line by skipping the note-type character
    /// and <paramref name="numericFields"/> whitespace-separated numeric fields, then returning
    /// the remainder verbatim (preserving any leading space that signals a new word).
    /// </summary>
    private static string ExtractSyllable(string line, int numericFields)
    {
        int pos = 1; // skip note-type char
        int fieldsSkipped = 0;
        while (pos < line.Length && fieldsSkipped < numericFields)
        {
            // skip whitespace
            while (pos < line.Length && char.IsWhiteSpace(line[pos]))
                pos++;
            // skip non-whitespace token
            while (pos < line.Length && !char.IsWhiteSpace(line[pos]))
                pos++;
            fieldsSkipped++;
        }
        // pos now points at the whitespace separator before the syllable.
        // Skip exactly one space so a leading space on the syllable is preserved.
        if (pos < line.Length && line[pos] == ' ')
            pos++;
        return pos < line.Length ? line[pos..] : string.Empty;
    }

    /// <summary>
    /// Marks the last note of <paramref name="line"/> as end-of-line and appends
    /// all notes to <paramref name="results"/>.
    /// </summary>
    private static void FlushLine(
        List<(double StartSec, double EndSec, string Text)> line,
        List<(double StartSec, double EndSec, string Text, bool IsEol)> results)
    {
        if (line.Count == 0)
            return;

        for (int i = 0; i < line.Count; i++)
        {
            bool isEol = i == line.Count - 1;
            results.Add((line[i].StartSec, line[i].EndSec, line[i].Text, isEol));
        }
    }
}
