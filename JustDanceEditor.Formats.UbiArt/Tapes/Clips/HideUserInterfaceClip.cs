namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record HideUserInterfaceClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "HideUserInterfaceClip";
    public int EventType { get; set; }
    public string CustomParam { get; set; } = string.Empty;
}