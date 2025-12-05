using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Formats.Unity.Models;

public sealed class UnityExportData(
    string name,
    ServerSongJSON metadata,
    TimelineStructureDocument structure,
    IReadOnlyList<KaraokeClip> karaokeClips,
    IReadOnlyList<PictogramEntry> pictogramClips,
    IReadOnlyList<(CoachTimelineClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> motionClips,
    IReadOnlyList<GoldEffectTimelineClip> goldEffectClips,
    IReadOnlyList<HideUserInterfaceTimelineClip> hideHudClips)
{
    public string Name { get; } = name;
    public ServerSongJSON Metadata { get; } = metadata;
    public TimelineStructureDocument Structure { get; } = structure;
    public IReadOnlyList<KaraokeClip> KaraokeClips { get; } = karaokeClips;
    public IReadOnlyList<PictogramEntry> PictogramClips { get; } = pictogramClips;
    public IReadOnlyList<(CoachTimelineClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> MotionClips { get; } = motionClips;
    public IReadOnlyList<GoldEffectTimelineClip> GoldEffectClips { get; } = goldEffectClips;
    public IReadOnlyList<HideUserInterfaceTimelineClip> HideHudClips { get; } = hideHudClips;
}
