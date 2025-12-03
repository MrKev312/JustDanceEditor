using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.JDI.Manifests;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Formats.JDI;

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
