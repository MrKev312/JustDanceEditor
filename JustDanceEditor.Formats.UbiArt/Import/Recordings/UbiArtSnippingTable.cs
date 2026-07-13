using JustDanceEditor.Formats.UbiArt.Recordings;

using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Recordings;

internal sealed record UbiArtSnippingRow(
    string RecordingFileName,
    int? CoachId,
    IReadOnlyList<UbiArtSnipSelection> Selections);

internal sealed record UbiArtSnipSelection(
    string MoveId,
    double StartBeat,
    bool IsIncluded,
    long TimelineClipId = 0,
    int MoveOccurrence = 0);

internal static class UbiArtSnippingTable
{
    public static IReadOnlyList<UbiArtSnippingRow> Read(Stream stream, string? tableFileName = null)
    {
        using StreamReader reader = new(stream, leaveOpen: true);
        List<string[]> rows = [];
        while (reader.ReadLine() is { } line)
            rows.Add(line.TrimEnd('\r').Split(';'));

        if (rows.Count < 3)
            return [];

        int? coachId = RecMotionRecordingCodec.InferCoachId(tableFileName);
        return rows[0].Length >= 2 && string.IsNullOrEmpty(rows[0][0]) && string.IsNullOrEmpty(rows[0][1])
            ? ReadAncient(rows, coachId)
            : ReadCurrent(rows, coachId);
    }

    public static void Write(
        Stream stream,
        IReadOnlyList<(string FileName, IReadOnlyList<UbiArtSnipSelection> Selections)> recordings,
        IReadOnlyList<UbiArtSnipSelection> columns)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using StreamWriter writer = new(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);

        writer.WriteLine(";" + string.Join(';', columns.Select(static column => Escape(column.MoveId))));
        writer.WriteLine("Marker;" + string.Join(';', columns.Select(static column => ToMarker(column.StartBeat).ToString(CultureInfo.InvariantCulture))));

        foreach ((string fileName, IReadOnlyList<UbiArtSnipSelection> selections) in recordings)
        {
            string values = string.Join(';', columns.Select((_, index) =>
                index < selections.Count && !selections[index].IsIncluded ? "0" : "1"));
            writer.WriteLine($"{Escape(fileName)};{values}");
        }
    }

    private static IReadOnlyList<UbiArtSnippingRow> ReadCurrent(IReadOnlyList<string[]> rows, int? coachId)
    {
        string[] moveNames = rows[0];
        string[] markers = rows[1];
        List<UbiArtSnippingRow> result = [];

        for (int rowIndex = 2; rowIndex < rows.Count; rowIndex++)
        {
            string[] row = rows[rowIndex];
            if (row.Length == 0 || string.IsNullOrWhiteSpace(row[0]))
                continue;

            List<UbiArtSnipSelection> selections = [];
            int columnCount = Math.Min(row.Length, Math.Min(moveNames.Length, markers.Length));
            for (int column = 1; column < columnCount; column++)
            {
                if (!int.TryParse(markers[column], NumberStyles.Integer, CultureInfo.InvariantCulture, out int marker))
                    continue;
                selections.Add(new UbiArtSnipSelection(
                    moveNames[column],
                    FromMarker(marker),
                    string.Equals(row[column].Trim(), "1", StringComparison.Ordinal)));
            }

            result.Add(new UbiArtSnippingRow(Path.GetFileName(row[0]), coachId, selections));
        }

        return result;
    }

    private static IReadOnlyList<UbiArtSnippingRow> ReadAncient(IReadOnlyList<string[]> rows, int? coachId)
    {
        string[] recordingNames = rows[0];
        List<UbiArtSnippingRow> result = [];

        for (int recordingColumn = 2; recordingColumn < recordingNames.Length; recordingColumn++)
        {
            if (string.IsNullOrWhiteSpace(recordingNames[recordingColumn]))
                continue;

            List<UbiArtSnipSelection> selections = [];
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];
                if (row.Length <= recordingColumn || row.Length < 2 ||
                    !int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int marker) ||
                    string.IsNullOrWhiteSpace(row[1]))
                {
                    continue;
                }

                selections.Add(new UbiArtSnipSelection(
                    row[1],
                    FromMarker(marker),
                    string.Equals(row[recordingColumn].Trim(), "1", StringComparison.Ordinal)));
            }

            result.Add(new UbiArtSnippingRow(Path.GetFileName(recordingNames[recordingColumn]), coachId, selections));
        }

        return result;
    }

    private static string Escape(string value) => value.Replace(';', '_').Replace('\r', ' ').Replace('\n', ' ');

    private static int ToMarker(double beat)
        => checked((int)Math.Round(beat * 24.0, MidpointRounding.AwayFromZero));

    private static double FromMarker(int marker) => marker / 24.0;
}
