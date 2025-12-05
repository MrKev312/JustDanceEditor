namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record GoldEffectClip : Clip
{
    public override string __class { get; } = "GoldEffectClip";
    public int EffectType { get; set; }
}