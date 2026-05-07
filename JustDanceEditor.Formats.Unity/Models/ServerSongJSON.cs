using JustDanceEditor.Formats.JDI.Metadata;

namespace JustDanceEditor.Formats.Unity.Models;

public class ServerSongJSON
{
    public Guid SongID { get; set; }
    public string Artist { get; set; } = string.Empty;
    public int CoachCount { get; set; }
    public JdiLocId[] CoachNamesLocIds { get; set; } = [];
    public string Credits { get; set; } = string.Empty;
    public JdiLocId DanceVersionLocId { get; set; } = JdiLocId.Zero;
    public uint Difficulty { get; set; }
    public string LyricsColor { get; set; } = "#FFFFFFFF";
    public double MapLength { get; set; }
    public string MapName { get; set; } = string.Empty;
    public uint OriginalJDVersion { get; set; }
    public string ParentMapName { get; set; } = string.Empty;
    public uint SweatDifficulty { get; set; }
    public string[] TagIds { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string Title { get; set; } = string.Empty;

    public ServerSongJSON() { }

    public ServerSongJSON(IntermediateMetadata metadata)
    {
        metadata.Validate();
        metadata.AdditionalMetadata ??= [];
        metadata.AdditionalMetadata.TryGetValue(TagIdsKey, out string? tagIdsRaw);

        SongID = metadata.SongID;
        MapName = metadata.MapName;
        ParentMapName = metadata.ParentMapName;
        Title = metadata.Title;
        Artist = metadata.Artist;
        Credits = metadata.Credits;
        Difficulty = metadata.Difficulty;
        SweatDifficulty = metadata.SweatDifficulty;
        CoachCount = metadata.CoachCount;
        CoachNamesLocIds = metadata.CoachNamesLocIds?.ToArray() ?? [];

        LyricsColor = metadata.LyricsColor;
        Tags = metadata.Tags?.ToArray() ?? [];
        TagIds = SplitCsv(tagIdsRaw);
        OriginalJDVersion = metadata.OriginalJDVersion;
        MapLength = metadata.MapLengthSeconds;
        DanceVersionLocId = metadata.DanceVersionLocId;

        static string[] SplitCsv(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return [];
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    public static explicit operator ServerSongJSON(IntermediateMetadata metadata)
        => new(metadata);

    public static explicit operator IntermediateMetadata(ServerSongJSON json)
    {
        return new IntermediateMetadata()
        {
            SongID = json.SongID,
            MapName = json.MapName,
            ParentMapName = json.ParentMapName,
            Title = json.Title,
            Artist = json.Artist,
            Credits = json.Credits,
            Difficulty = json.Difficulty,
            SweatDifficulty = json.SweatDifficulty,
            CoachCount = json.CoachCount,
            CoachNamesLocIds = json.CoachNamesLocIds.Length == 0 ? null : json.CoachNamesLocIds.ToArray(),
            DanceVersionLocId = json.DanceVersionLocId,
            LyricsColor = json.LyricsColor,
            Tags = [.. json.Tags],
            OriginalJDVersion = json.OriginalJDVersion,
            MapLengthSeconds = json.MapLength,
            AdditionalMetadata = new Dictionary<string, string>
            {
                [TagIdsKey] = string.Join(',', json.TagIds)
            }
        };
    }

    public const string TagIdsKey = "unity.tagIds";
}
