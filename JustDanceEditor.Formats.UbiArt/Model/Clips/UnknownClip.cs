namespace JustDanceEditor.Formats.UbiArt.Model.Clips;

public sealed record UnknownClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "UnknownClip";
    public string OriginalClass { get; set; } = string.Empty;
    public uint TypeId { get; set; }
    public int SerializedSize { get; set; }
    public byte[] Payload { get; set; } = [];
}
