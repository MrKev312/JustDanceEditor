namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record GoldEffectClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "GoldEffectClip";
    public int EffectType { get; set; }
}