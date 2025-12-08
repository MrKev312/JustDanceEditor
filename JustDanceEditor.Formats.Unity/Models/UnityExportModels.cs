using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Formats.Unity.Models;

public sealed class UnityExportData(
    string name,
    ServerSongJSON metadata,
    TimelineStructureDocument structure,
    IReadOnlyList<KaraokeClip> karaokeClips,
    IReadOnlyList<PictogramClip> pictogramClips,
    IReadOnlyList<(MoveClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> motionClips,
    IReadOnlyList<GoldEffectClip> goldEffectClips,
    IReadOnlyList<HideUserInterfaceClip> hideHudClips)
{
    public string Name { get; } = name;
    public ServerSongJSON Metadata { get; } = metadata;
    public TimelineStructureDocument Structure { get; } = structure;
    public IReadOnlyList<KaraokeClip> KaraokeClips { get; } = karaokeClips;
    public IReadOnlyList<PictogramClip> PictogramClips { get; } = pictogramClips;
    public IReadOnlyList<(MoveClip Clip, int CoachId, long TrackId, int MoveType, int Duration)> MotionClips { get; } = motionClips;
    public IReadOnlyList<GoldEffectClip> GoldEffectClips { get; } = goldEffectClips;
    public IReadOnlyList<HideUserInterfaceClip> HideHudClips { get; } = hideHudClips;
}
