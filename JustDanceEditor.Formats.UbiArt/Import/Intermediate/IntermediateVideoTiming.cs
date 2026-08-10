using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class IntermediateVideoTiming
{
    private const double CinematicDurationPaddingSeconds = 5.0;

    public static double GetMasterDurationSeconds(IntermediateSongPackage package, double fallbackDurationSeconds)
    {
        try
        {
            double durationSeconds = MusicTrackTiming.GetSecondsAtBeat(
                package.TimelineStructure.Markers,
                GetEffectiveEndBeat(package.TimelineStructure)) - package.TimelineStructure.VideoStartOffset;
            if (durationSeconds > 0)
                return durationSeconds;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or NotSupportedException)
        {
        }

        return fallbackDurationSeconds;
    }

    public static double GetCinematicRenderDurationSeconds(IntermediateSongPackage package, double fallbackDurationSeconds) =>
        GetMasterDurationSeconds(package, fallbackDurationSeconds) + CinematicDurationPaddingSeconds;

    private static int GetEffectiveEndBeat(TimelineStructureDocument timeline) =>
        timeline.EndBeat != 0 ? timeline.EndBeat : Math.Max(0, timeline.Markers.Count - 1);
}
