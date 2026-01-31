namespace JustDanceEditor.Formats.UbiArt.Model.Clips;

public sealed record SoundSetClip : Clip
{
    [System.Text.Json.Serialization.JsonPropertyName("__class")]
    public override string Class { get; } = "SoundSetClip";
    public string SoundSetPath { get; set; } = string.Empty;
    public int SoundChannel { get; set; }
    public int StartOffset { get; set; }
    public int StopsOnEnd { get; set; }
    public int AccountedForDuration { get; set; }
}