namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record GameplayEventClip : Clip
{
    public override string __class { get; } = "GameplayEventClip";
    public int[] ActorIndices { get; set; } = [];
    public int EventType { get; set; }
    public string CustomParam { get; set; } = string.Empty;
}