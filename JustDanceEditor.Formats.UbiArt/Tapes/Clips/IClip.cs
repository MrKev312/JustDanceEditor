namespace JustDanceEditor.Formats.UbiArt.Tapes.Clips;

public interface IClip
{
    string __class { get; set; }
    long Id { get; set; }
    long TrackId { get; set; }
    int IsActive { get; set; }
    int StartTime { get; set; }
    int Duration { get; set; }
}