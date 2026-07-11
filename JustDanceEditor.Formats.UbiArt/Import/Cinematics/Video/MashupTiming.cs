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

        return MusicTrackTiming.GetSecondsAtBeat(markers, beat);
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
}
