namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record VibrationClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "VibrationClip";
    public string VibrationFilePath { get; set; } = string.Empty;
    public int Loop { get; set; }
    public int DeviceSide { get; set; }
    public int PlayerId { get; set; }
    public int Context { get; set; }
    public int StartTimeOffset { get; set; }
    public float Modulation { get; set; }
}