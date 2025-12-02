using JustDanceEditor.Formats.Intermediate.Assets;
using JustDanceEditor.Formats.Intermediate.Manifests;
using JustDanceEditor.Formats.Intermediate.Metadata;
using JustDanceEditor.Formats.Intermediate.Timelines;

namespace JustDanceEditor.Formats.Intermediate;

public class IntermediateSongPackage
{
    public IntermediatePackageManifest Manifest { get; init; } = new();

    public IntermediateMetadata Metadata { get; init; } = new();

    public IntermediateAssetCatalog AssetCatalog { get; init; } = new();

    public TimelineStructureDocument TimelineStructure { get; init; } = new();

    public LyricsTimelineDocument Lyrics { get; init; } = new();

    public PictogramTimelineDocument Pictograms { get; init; } = new();

    public EventTimelineDocument Events { get; init; } = new();

    public List<CoachTimelineDocument> CoachTimelines { get; init; } = [];

    public List<CoachTimelineDocument> FullBodyCoachTimelines { get; init; } = [];

    public Dictionary<string, CoachMoveDefinition> HandCoachMoves { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CoachMoveDefinition> FullBodyCoachMoves { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}
