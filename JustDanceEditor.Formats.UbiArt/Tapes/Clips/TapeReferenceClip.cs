namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record TapeReferenceClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "TapeReferenceClip";
    public string Path { get; set; } = string.Empty;
    public int Loop { get; set; }
}