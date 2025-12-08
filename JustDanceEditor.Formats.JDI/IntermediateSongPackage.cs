using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Formats.JDI;

public class IntermediateSongPackage
{
    public IntermediateMetadata Metadata { get; init; } = new();
    public TimelineStructureDocument TimelineStructure { get; init; } = new();
    public Timeline<KaraokeClip> Lyrics { get; init; } = new();
    public Timeline<PictogramClip> Pictograms { get; init; } = new();
    public Timeline<GoldEffectClip> GoldEffects { get; init; } = new();
    public Timeline<HideUserInterfaceClip> HideUserInterface { get; init; } = new();
    public Timeline<VibrationClip> Vibrations { get; init; } = new();
    public List<MoveTimeline> CoachTimelines { get; init; } = [];
    public List<MoveTimeline> FullBodyCoachTimelines { get; init; } = [];
    public Dictionary<string, CoachMoveDefinition> HandCoachMoves { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CoachMoveDefinition> FullBodyCoachMoves { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}