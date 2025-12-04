using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class EventTimelineDocument
{
    [JsonPropertyName("events")]
    public List<TimelineEvent> Events { get; set; } = [];
}

public class TimelineEvent
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("startBeat")]
    public double StartTime { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("eventName")]
    public string? EventName { get; set; }
}
