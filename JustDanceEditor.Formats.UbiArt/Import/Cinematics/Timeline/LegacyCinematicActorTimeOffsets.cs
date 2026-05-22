using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class LegacyCinematicActorTimeOffsets
{
    public static double GetClipFrame(
        LegacyCinematicActor actor,
        double frame,
        LegacyCinematicTimeline? timeline,
        int renderStartFrame)
    {
        double offsetSeconds = 0.0;
        foreach (ActorTimeOffset offset in GetClipOffsets())
        {
            if (MatchesSelector(actor, offset.Selector))
                offsetSeconds += offset.OffsetSeconds;
        }

        if (Math.Abs(offsetSeconds) <= 0.000001)
            return frame;

        if (timeline == null)
            return frame;

        double songSeconds = timeline.GetSongSecondsForTapeFrame(frame, renderStartFrame);
        return timeline.GetTapeFrameForSongSeconds(songSeconds + offsetSeconds, renderStartFrame);
    }

    public static bool MatchesSelector(LegacyCinematicActor actor, string selector) =>
        string.Equals(actor.Key, selector, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actor.Name, selector, StringComparison.OrdinalIgnoreCase) ||
        actor.Key.EndsWith($"/{selector}", StringComparison.OrdinalIgnoreCase) ||
        actor.Key.Contains(selector, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ActorTimeOffset> GetClipOffsets() => [];

    private readonly record struct ActorTimeOffset(string Selector, double OffsetSeconds);
}