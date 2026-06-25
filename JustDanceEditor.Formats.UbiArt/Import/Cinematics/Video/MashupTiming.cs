using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;

using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupTiming
{
    public static TimelineStructureDocument CreateRenderTimeline(TimelineStructureDocument timelineStructure)
    {
        ArgumentNullException.ThrowIfNull(timelineStructure);

        return new TimelineStructureDocument
        {
            Markers = [.. timelineStructure.Markers],
            Signatures = [.. timelineStructure.Signatures],
            Sections = [.. timelineStructure.Sections],
            StartBeat = 0,
            EndBeat = timelineStructure.EndBeat,
            VideoStartOffset = 0.0,
            PreviewEntryBeat = timelineStructure.PreviewEntryBeat,
            PreviewLoopStartBeat = timelineStructure.PreviewLoopStartBeat,
            PreviewLoopEndBeat = timelineStructure.PreviewLoopEndBeat,
            PreviewDuration = timelineStructure.PreviewDuration
        };
    }

    internal static int GetLocalTapeFrame(TimelineStructureDocument timeline, int absoluteBeat) =>
        (int)Math.Round(absoluteBeat * CinematicConstants.TapeFramesPerBeat);

    internal static double GetOutputSeconds(TimelineStructureDocument timeline, int absoluteBeat) =>
        GetOutputSeconds(timeline, (double)absoluteBeat);

    internal static double GetOutputSeconds(TimelineStructureDocument timeline, double absoluteBeat)
    {
        double beatSeconds = GetMusicTrackSecondsAtBeat(timeline.Markers, absoluteBeat);
        return Math.Max(0.0, beatSeconds);
    }

    internal static double GetOutputSecondsForTapeFrame(TimelineStructureDocument timeline, int tapeFrame)
    {
        double absoluteBeat = tapeFrame / CinematicConstants.TapeFramesPerBeat;
        double beatSeconds = GetMusicTrackSecondsAtBeat(timeline.Markers, absoluteBeat);
        return Math.Max(0.0, beatSeconds);
    }

    internal static double GetVideoSecondsForBeat(TimelineStructureDocument timeline, int beat) =>
        GetMusicTrackSecondsAtBeat(timeline.Markers, beat) - timeline.VideoStartOffset;

    internal static int SecondsToOutputFrame(double seconds) =>
        (int)Math.Round(seconds * CinematicConstants.OutputFramesPerSecond);

    internal static bool IsConsecutiveBlock(LegacyMashupBlockDescriptor? previous, LegacyMashupBlockDescriptor current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (previous == null)
            return false;

        return TryGetBlockNameBeat(previous.SongName, out int previousNameBeat) &&
            TryGetBlockNameBeat(current.SongName, out int currentNameBeat) &&
            previousNameBeat + previous.DurationBeats == currentNameBeat;
    }

    private static double GetMusicTrackSecondsAtBeat(IReadOnlyList<int> markers, double beat)
    {
        if (markers.Count < 2)
            return beat * 0.5;

        int previousWholeBeat = (int)Math.Floor(beat);
        int firstMarkerPosition = GetMusicTrackBeatSamplePosition(markers, previousWholeBeat);
        int secondMarkerPosition = GetMusicTrackBeatSamplePosition(markers, previousWholeBeat + 1);
        double beatFractionalPart = beat - previousWholeBeat;
        double sampleOffset = firstMarkerPosition + (beatFractionalPart * (secondMarkerPosition - firstMarkerPosition));
        return sampleOffset / 48000.0;
    }

    private static int GetMusicTrackBeatSamplePosition(IReadOnlyList<int> markers, int beat)
    {
        if (beat < 0)
        {
            int averageBeatLength = ComputeAverageMarkerSpacing(markers, 0, Math.Min(4, markers.Count - 1));
            return beat * averageBeatLength;
        }

        if (beat >= markers.Count)
        {
            int endMarker = markers.Count - 1;
            int startMarker = Math.Max(0, endMarker - 4);
            int averageBeatLength = ComputeAverageMarkerSpacing(markers, startMarker, endMarker);
            return markers[endMarker] + ((beat - markers.Count + 1) * averageBeatLength);
        }

        return markers[beat];
    }

    private static bool TryGetBlockNameBeat(string songName, out int beat)
    {
        beat = 0;
        if (string.IsNullOrWhiteSpace(songName))
            return false;

        string[] parts = songName.Split('_');
        return parts.Length > 1 &&
            int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out beat);
    }

    private static int ComputeAverageMarkerSpacing(IReadOnlyList<int> markers, int startMarker, int endMarker)
    {
        if (endMarker <= startMarker)
            return markers.Count >= 2 ? markers[1] - markers[0] : 24000;

        return (markers[endMarker] - markers[startMarker]) / (endMarker - startMarker);
    }
}