namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record GameplayEventClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "GameplayEventClip";
    public int[] ActorIndices { get; set; } = [];
    public int EventType { get; set; }
    public string CustomParam { get; set; } = string.Empty;
}