using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Manifests;

public class IntermediatePackageManifest
{
    public const string CurrentSchemaVersion = "1.0.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("metadataFile")]
    public string MetadataFile { get; set; } = "metadata.json";

    [JsonPropertyName("assetsFile")]
    public string AssetsFile { get; set; } = "assets.json";

    [JsonPropertyName("timelines")]
    public TimelineManifest Timelines { get; set; } = new();
}

public class TimelineManifest
{
    [JsonPropertyName("folder")]
    public string Folder { get; set; } = "timelines";

    [JsonPropertyName("structureFile")]
    public string StructureFile { get; set; } = "timelines/structure.json";

    [JsonPropertyName("lyricsFile")]
    public string LyricsFile { get; set; } = "timelines/lyrics.json";

    [JsonPropertyName("pictogramsFile")]
    public string PictogramsFile { get; set; } = "timelines/pictograms.json";

    [JsonPropertyName("eventsFile")]
    public string EventsFile { get; set; } = "timelines/events.json";

    [JsonPropertyName("coachTimelines")]
    public List<CoachTimelinePointer> CoachTimelines { get; set; } = [];

    [JsonPropertyName("fullBodyCoachTimelines")]
    public List<CoachTimelinePointer> FullBodyCoachTimelines { get; set; } = [];

    [JsonPropertyName("handMovesFile")]
    public string? HandMovesFile { get; set; }

    [JsonPropertyName("fullBodyMovesFile")]
    public string? FullBodyMovesFile { get; set; }
}

public class CoachTimelinePointer
{
    [JsonPropertyName("coachId")]
    public int CoachId { get; set; }

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;
}
