using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class PictogramTimelineDocument
{
    [JsonPropertyName("entries")]
    public List<PictogramEntry> Entries { get; set; } = [];
}

public class PictogramEntry : TimelineClipBase
{
    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("pictogramId")]
    public string PictogramId { get; set; } = string.Empty;

    [JsonPropertyName("coachCount")]
    public int CoachCount { get; set; }
}
