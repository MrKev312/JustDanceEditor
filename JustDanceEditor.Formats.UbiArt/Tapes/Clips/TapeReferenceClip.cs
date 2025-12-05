namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record TapeReferenceClip : Clip
{
    public override string __class { get; } = "TapeReferenceClip";
    public string Path { get; set; } = string.Empty;
    public int Loop { get; set; }
}