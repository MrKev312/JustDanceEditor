using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt;

public class OnlineSongDesc
{
    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public Assets Assets { get; set; } = new();

    [JsonPropertyName("coachCount")]
    public int CoachCount { get; set; }

    [JsonPropertyName("credits")]
    public string Credits { get; set; } = string.Empty;

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; }

    [JsonPropertyName("lyricsColor")]
    public string LyricsColor { get; set; } = string.Empty;

    [JsonPropertyName("lyricsType")]
    public int LyricsType { get; set; }

    [JsonPropertyName("mainCoach")]
    public int MainCoach { get; set; }

    [JsonPropertyName("mapLength")]
    public float MapLength { get; set; }

    [JsonPropertyName("mapName")]
    public string MapName { get; set; } = string.Empty;

    [JsonPropertyName("originalJDVersion")]
    public int OriginalJDVersion { get; set; }

    [JsonPropertyName("parentMapName")]
    public string ParentMapName { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("sweatDifficulty")]
    public int SweatDifficulty { get; set; }

    [JsonPropertyName("tagIds")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("tags")]
    public string Title { get; set; } = string.Empty;

    public static explicit operator SongDesc(OnlineSongDesc onlineSongDesc)
    {
        SongDesc songDesc = new()
        {
            COMPONENTS =
            [
                new()
                {
                    Artist = onlineSongDesc.Artist,
                    NumCoach = onlineSongDesc.CoachCount,
                    MainCoach = onlineSongDesc.MainCoach,
                    Difficulty = (uint)onlineSongDesc.Difficulty,
                    SweatDifficulty = (uint)onlineSongDesc.SweatDifficulty,
                    LyricsType = onlineSongDesc.LyricsType,
                    Title = onlineSongDesc.Title,
                    Credits = onlineSongDesc.Credits,
                    Tags = onlineSongDesc.Tags,
                    Status = onlineSongDesc.Status,
                    OriginalJDVersion = (uint)onlineSongDesc.OriginalJDVersion,
                    MapName = onlineSongDesc.MapName,
                    VideoPreviewPath = onlineSongDesc.Assets.videoPreview_HIGHvp9webm
                }
            ]
        };

        return songDesc;
    }
}

// TODO: check wtf? Is there stuff missing?
public class Assets
{
    public string videoPreview_HIGHvp9webm { get; set; } = string.Empty;
}