namespace JustDanceEditor.Formats.Unity.Models;

public class ServerSongJSON
{
    public Guid SongID { get; set; }
    public string Artist { get; set; } = string.Empty;
    public int CoachCount { get; set; }
    public int[] CoachNamesLocIds { get; set; } = [];
    public string Credits { get; set; } = string.Empty;
    public int DanceVersionLocId { get; set; }
    public uint Difficulty { get; set; }
    public object? DoubleScoringType { get; set; }
    public bool HasSongTitleInCover { get; set; }
    public string LyricsColor { get; set; } = "#FFFFFFFF";
    public double MapLength { get; set; }
    public string MapName { get; set; } = string.Empty;
    public uint OriginalJDVersion { get; set; }
    public string ParentMapName { get; set; } = string.Empty;
    public uint SweatDifficulty { get; set; }
    public string[] TagIds { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string Title { get; set; } = string.Empty;
}