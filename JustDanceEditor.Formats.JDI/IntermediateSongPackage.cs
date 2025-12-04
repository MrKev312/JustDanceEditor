using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Formats.JDI;

public class IntermediateSongPackage
{
    public IntermediateMetadata Metadata { get; init; } = new();

    public TimelineStructureDocument TimelineStructure { get; init; } = new();

    public LyricsTimelineDocument Lyrics { get; init; } = new();

    public PictogramTimelineDocument Pictograms { get; init; } = new();

    public GoldEffectTimelineDocument GoldEffects { get; init; } = new();

    public HideUserInterfaceTimelineDocument HideUserInterface { get; init; } = new();

    public GameplayEventTimelineDocument GameplayEvents { get; init; } = new();

    public VibrationTimelineDocument Vibrations { get; init; } = new();

    public List<CoachTimelineDocument> CoachTimelines { get; init; } = [];

    public List<CoachTimelineDocument> FullBodyCoachTimelines { get; init; } = [];

    public Dictionary<string, CoachMoveDefinition> HandCoachMoves { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CoachMoveDefinition> FullBodyCoachMoves { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
