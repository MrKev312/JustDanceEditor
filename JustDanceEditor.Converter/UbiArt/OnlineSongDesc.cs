namespace JustDanceEditor.Converter.UbiArt;

// For JDUnlimited server JSON
internal class OnlineSongDesc
{
    public string artist { get; set; }
    public Assets assets { get; set; }
    public int coachCount { get; set; }
    public string credits { get; set; }
    public int difficulty { get; set; }
    public string lyricsColor { get; set; }
    public int lyricsType { get; set; }
    public int mainCoach { get; set; }
    public float mapLength { get; set; }
    public string mapName { get; set; }
    public int originalJDVersion { get; set; }
    public string parentMapName { get; set; }
    public int status { get; set; }
    public int sweatDifficulty { get; set; }
    public string[] tags { get; set; }
    public string title { get; set; }

    // Allow conversion from OnlineSongDesc to SongDesc
    public static explicit operator SongDesc(OnlineSongDesc onlineSongDesc)
    {
        SongDesc songDesc = new()
        {
            COMPONENTS =
            [
                new()
                {
                    Artist = onlineSongDesc.artist,
                    NumCoach = (uint)onlineSongDesc.coachCount,
                    MainCoach = onlineSongDesc.mainCoach,
                    Difficulty = (uint)onlineSongDesc.difficulty,
                    SweatDifficulty = (uint)onlineSongDesc.sweatDifficulty,
                    LyricsType = onlineSongDesc.lyricsType,
                    Title = onlineSongDesc.title,
                    Credits = onlineSongDesc.credits,
                    Tags = onlineSongDesc.tags,
                    Status = onlineSongDesc.status,
                    OriginalJDVersion = (uint)onlineSongDesc.originalJDVersion,
                    MapName = onlineSongDesc.mapName,
                    VideoPreviewPath = onlineSongDesc.assets.videoPreview_HIGHvp9webm
                }
            ]
        };

        return songDesc;
    }
}
