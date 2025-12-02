namespace JustDanceEditor.Formats.Unity;

public class SongDBSongData
{
    public string artist { get; set; } = string.Empty;
    public string audioPreviewData { get; set; } = string.Empty;
    public uint coachCount { get; set; }
    public string credits { get; set; } = string.Empty;
    public uint difficulty { get; set; }
    public string lyricsColor { get; set; } = string.Empty;
    public double mapLength { get; set; }
    public string mapName { get; set; } = string.Empty;
    public uint originalJDVersion { get; set; }
    public string parentMapName { get; set; } = string.Empty;
    public uint sweatDifficulty { get; set; }
    public string title { get; set; } = string.Empty;
}
