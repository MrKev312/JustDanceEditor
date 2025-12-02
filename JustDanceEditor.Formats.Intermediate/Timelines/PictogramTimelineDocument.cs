using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Intermediate.Timelines;

public class PictogramTimelineDocument
{
    [JsonPropertyName("entries")]
    public List<PictogramEntry> Entries { get; set; } = [];
}

public class PictogramEntry
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("startBeat")]
    public int StartTime { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("pictogramId")]
    public string PictogramId { get; set; } = string.Empty;

    [JsonPropertyName("coachCount")]
    public int CoachCount { get; set; }
}
