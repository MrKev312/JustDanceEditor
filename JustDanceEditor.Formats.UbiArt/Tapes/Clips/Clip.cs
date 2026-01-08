using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public abstract record Clip
{
    [JsonPropertyName("__class")]
    public abstract string Class { get; }
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
}