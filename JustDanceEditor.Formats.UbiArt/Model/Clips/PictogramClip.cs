namespace JustDanceEditor.Formats.UbiArt.Model.Clips;

public sealed record PictogramClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "PictogramClip";
    public string PictoPath { get; set; } = string.Empty;
    public long CoachCount { get; set; }
}