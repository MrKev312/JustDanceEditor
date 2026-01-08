namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record KaraokeClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "KaraokeClip";
    public float Pitch { get; set; }
    public string Lyrics { get; set; } = string.Empty;
    public int IsEndOfLine { get; set; }
    public int ContentType { get; set; }
    public int StartTimeTolerance { get; set; }
    public int EndTimeTolerance { get; set; }
    public float SemitoneTolerance { get; set; }
}