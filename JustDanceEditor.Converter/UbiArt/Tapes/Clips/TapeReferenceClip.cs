namespace JustDanceEditor.Converter.UbiArt.Tapes.Clips;

//{
//    "__class": "TapeReferenceClip",
//    "Id": 4250461876,
//    "TrackId": 152415948,
//    "IsActive": 1,
//    "StartTime": 2688,
//    "Duration": 180,
//    "Path": "world/maps/automatonalt/cinematics/automatonalt_vib_chorus.tape",
//    "Loop": 0
//}

public class TapeReferenceClip : IClip
{
    public string __class { get; set; } = "TapeReferenceClip";
    public long Id { get; set; }
    public long TrackId { get; set; }
    public int IsActive { get; set; }
    public int StartTime { get; set; }
    public int Duration { get; set; }
    public string Path { get; set; } = "";
    public int Loop { get; set; }
}
