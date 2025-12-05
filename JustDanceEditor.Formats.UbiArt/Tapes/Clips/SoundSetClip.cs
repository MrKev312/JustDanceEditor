namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public sealed record SoundSetClip : Clip
{
    public override string __class { get; } = "SoundSetClip";
    public string SoundSetPath { get; set; } = string.Empty;
    public int SoundChannel { get; set; }
    public int StartOffset { get; set; }
    public int StopsOnEnd { get; set; }
    public int AccountedForDuration { get; set; }
}