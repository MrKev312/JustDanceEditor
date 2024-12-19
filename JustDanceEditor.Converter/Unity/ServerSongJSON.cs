namespace JustDanceEditor.Converter.Unity;

// Used for custom servers
public class ServerSongJSON
{
    public Guid SongID { get; set; }
    public string Artist { get; set; } = "";
    public int CoachCount { get; set; }
    public int[] CoachNamesLocIds { get; set; } = [];
    public string Credits { get; set; } = "";
    public int DanceVersionLocId { get; set; }
    public int Difficulty { get; set; }
    public object? DoubleScoringType { get; set; }
    public bool HasSongTitleInCover { get; set; }
    public string LyricsColor { get; set; } = "#FFFFFFFF";
    public float MapLength { get; set; }
    public string MapName { get; set; } = "";
    public int OriginalJDVersion { get; set; }
    public string ParentMapName { get; set; } = "";
    public int SweatDifficulty { get; set; }
    public string[] TagIds { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string Title { get; set; } = "";
}
