using JustDanceEditor.Formats.JDI.Metadata;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Unity.Models;

public class ServerSongJSON
{
    public Guid SongID { get; set; }
    public string Artist { get; set; } = string.Empty;
    public int CoachCount { get; set; }
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public string[] CoachNamesLocIds { get; set; } = [];
    public string Credits { get; set; } = string.Empty;
    public int DanceVersionLocId { get; set; }
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
        metadata.AdditionalMetadata.TryGetValue(CoachNamesLocIdsKey, out string? namesLocRaw);
        metadata.AdditionalMetadata.TryGetValue(DanceVersionLocIdKey, out string? danceLocRaw);

        SongID = metadata.SongID;
        MapName = metadata.MapName;
        ParentMapName = metadata.ParentMapName;
        Title = metadata.Title;
        Artist = metadata.Artist;
        Credits = metadata.Credits;
        Difficulty = metadata.Difficulty;
        SweatDifficulty = metadata.SweatDifficulty;
        CoachCount = metadata.CoachCount;
        CoachNamesLocIds = SplitCsv(namesLocRaw);
        LyricsColor = metadata.LyricsColor;
        Tags = metadata.Tags?.ToArray() ?? [];
        TagIds = SplitCsv(tagIdsRaw);
        OriginalJDVersion = metadata.OriginalJDVersion;
        MapLength = metadata.MapLengthSeconds;
        DanceVersionLocId = int.TryParse(danceLocRaw, out int parsed) ? parsed : 0;

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
            LyricsColor = json.LyricsColor,
            Tags = [.. json.Tags],
            OriginalJDVersion = json.OriginalJDVersion,
            MapLengthSeconds = json.MapLength,
            AdditionalMetadata = new Dictionary<string, string>
            {
                [TagIdsKey] = string.Join(',', json.TagIds),
                [CoachNamesLocIdsKey] = string.Join(',', json.CoachNamesLocIds),
                [DanceVersionLocIdKey] = json.DanceVersionLocId.ToString()
            }
        };
    }

    public const string TagIdsKey = "unity.tagIds";
    public const string CoachNamesLocIdsKey = "unity.coachNamesLocIds";
    public const string DanceVersionLocIdKey = "unity.danceVersionLocId";
}

public sealed class FlexibleStringListConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            List<string> list = [];
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    break;
                else if (reader.TokenType == JsonTokenType.String)
                    list.Add(reader.GetString()!);
                else if (reader.TokenType == JsonTokenType.Number)
                    list.Add(reader.GetInt32().ToString());
                else
                {
                    throw new JsonException("Expected string value in array.");
                }
            }

            return [.. list];
        }
        else
        {
            return reader.TokenType == JsonTokenType.String
                ? [reader.GetString()!]
                : [reader.GetInt32().ToString()];
        }
    }
    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string str in value)
        {
            writer.WriteStringValue(str);
        }

        writer.WriteEndArray();
    }
}