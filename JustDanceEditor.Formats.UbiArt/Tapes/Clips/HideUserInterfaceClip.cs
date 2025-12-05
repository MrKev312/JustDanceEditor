namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record HideUserInterfaceClip : Clip
{
    public override string __class { get; } = "HideUserInterfaceClip";
    public int EventType { get; set; }
    public string CustomParam { get; set; } = string.Empty;
}