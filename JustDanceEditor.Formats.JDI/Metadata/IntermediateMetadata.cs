using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Metadata;

public class IntermediateMetadata
{
    [JsonPropertyName("songId")]
    public Guid SongId { get; set; }

    [JsonPropertyName("mapName")]
    public string MapName { get; set; } = string.Empty;

    [JsonPropertyName("parentMapName")]
    public string ParentMapName { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("credits")]
    public string Credits { get; set; } = string.Empty;

    [JsonPropertyName("lyricsColor")]
    public string LyricsColor { get; set; } = "#FFFFFFFF";

    [JsonPropertyName("mapLengthSeconds")]
    public double MapLengthSeconds { get; set; }

    [JsonPropertyName("engineVersion")]
    public uint EngineVersion { get; set; }

    [JsonPropertyName("originalJdVersion")]
    public uint OriginalJdVersion { get; set; }

    [JsonPropertyName("coachCount")]
    public int CoachCount { get; set; }

    [JsonPropertyName("coachNames")]
    public string[]? CoachNames { get; set; }

    [JsonPropertyName("difficulty")]
    public uint Difficulty { get; set; }

    [JsonPropertyName("sweatDifficulty")]
    public uint SweatDifficulty { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("status")]
    public float Status { get; set; }

    [JsonPropertyName("hasSongTitleInCover")]
    public bool HasSongTitleInCover { get; set; }

    [JsonPropertyName("mojoValue")]
    public int MojoValue { get; set; }

    [JsonPropertyName("countInProgression")]
    public int CountInProgression { get; set; }

    [JsonPropertyName("additionalMetadata")]
    public Dictionary<string, string> AdditionalMetadata { get; set; } = new();

    public void Validate()
    {
        if (CoachCount < 0)
            throw new InvalidOperationException("CoachCount cannot be negative.");

        if (CoachNames == null)
            return;

        if (CoachNames.Length != CoachCount)
            throw new InvalidOperationException("CoachNames must contain an entry for every coach or be null.");

        if (CoachNames.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("CoachNames cannot contain blank values.");
    }
}
