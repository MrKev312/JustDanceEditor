namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

public class SoundSetClip : IClip
{
    public string __class { get; set; } = "SoundSetClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string SoundSetPath { get; set; } = "";
    public int SoundChannel { get; set; }
    public int StartOffset { get; set; }
    public int StopsOnEnd { get; set; }
    public int AccountedForDuration { get; set; }
}
