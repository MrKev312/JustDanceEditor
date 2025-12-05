namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record PictogramClip : Clip
{
    public override string __class { get; } = "PictogramClip";
    public string PictoPath { get; set; } = string.Empty;
    public long CoachCount { get; set; }
}