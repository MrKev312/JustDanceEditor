using System.Text.Json.Serialization;

using JustDanceEditor.Converter.Converters;
using JustDanceEditor.Converter.UbiArt;

namespace JustDanceEditor.Converter.Unity;

public class JDCacheJSON
{
    [JsonPropertyName("schemaVersion")]
    public uint SchemaVersion { get; set; } = 1;

    [JsonPropertyName("mapsDict")]
    // String is the map ID
    public required Dictionary<string, JDSong> MapsDict { get; set; }
}

public class JDSong
{
    [JsonPropertyName("songDatabaseEntry")]
    public required SongDatabaseEntry SongDatabaseEntry { get; set; }

    [JsonPropertyName("audioPreviewTrk")]
    public required string AudioPreviewTrk { get; set; }

    [JsonPropertyName("assetFilesDict")]
    public required AssetFilesDict AssetFilesDict { get; set; }

    [JsonPropertyName("sizes")]
    public required Sizes Sizes { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hasSongTitleInCover")]
    public bool? HasSongTitleInCover { get; set; } = null;
}

public class SongDatabaseEntry
{
    // Must be a version 4 UUID
    public string MapId { get; set; } = "";
    public string ParentMapId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Credits { get; set; } = "";
    // Format: "#RRGGBB"
    public string LyricsColor { get; set; } = "";
    // Song length in seconds
    public double MapLength { get; set; }
    public uint OriginalJDVersion { get; set; }
    // Must be between 1 and 4
    public uint CoachCount { get; set; }
    // Must be between 1 and 5
    public uint Difficulty { get; set; }
    public uint SweatDifficulty { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> TagIds { get; set; } = [];
    // Seems to always be empty
    public List<uint> SearchTagsLocIds { get; set; } = [];
    // Can be empty, causes all names to be blank
    public List<uint> CoachNamesLocIds { get; set; } = [];

    [JsonPropertyName("hasSongTitleInCover")]
    // Seems to always be false, set the one in JDSong instead
    public bool HasSongTitleInCover { get; set; } = false;

    // Allow conversion from JDNextUbiMapData
    public static explicit operator SongDatabaseEntry(JDNextUbiMapData mapData)
    {
        return new()
        {
            MapId = mapData.mapName,
            ParentMapId = mapData.parentMapName,
            Title = mapData.title,
            Artist = mapData.artist,
            Credits = mapData.credits,
            LyricsColor = mapData.lyricsColor,
            MapLength = mapData.mapLength,
            OriginalJDVersion = mapData.originalJDVersion,
            CoachCount = mapData.coachCount,
            Difficulty = mapData.difficulty,
            SweatDifficulty = mapData.sweatDifficulty,
            Tags = [.. mapData.tags],
            TagIds = [.. mapData.tagIds],
            SearchTagsLocIds = [],
            CoachNamesLocIds = [],
            HasSongTitleInCover = mapData.hasSongTitleInCover
        };
    }

    public static explicit operator SongDatabaseEntry(ConvertUbiArtToUnity convert)
    {
        SongDesc mapData = convert.SongData.SongDesc;

        if (mapData.COMPONENTS.Length == 0)
            throw new ArgumentException("COMPONENTS must have at least one element");

        InfoComponent info = mapData.COMPONENTS[0];

        float[] lyricsColorUbi = info.DefaultColors.lyrics;

        // Convert each component from float (0-1 range) to byte (0-255 range)
        int alpha = (int)(lyricsColorUbi[0] * 255);
        int red = (int)(lyricsColorUbi[1] * 255);
        int green = (int)(lyricsColorUbi[2] * 255);
        int blue = (int)(lyricsColorUbi[3] * 255);

        // Create the hex string in the format #RRGGBB
        string lyricsColor = $"#{alpha:X2}{red:X2}{green:X2}{blue:X2}";

        // Get startBeat and endBeat
        int startBeat = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.startBeat;
        int endBeat = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.endBeat;
        // Use markers to get the length of the song
        float startTime = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[-startBeat] / 48f / 1000f;
        float endTime = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[endBeat + startBeat] / 48f / 1000f;

        string songTitleLogoPath = Path.Combine(convert.Output0Folder, "songTitleLogo");
        bool songTitleLogo = Directory.Exists(songTitleLogoPath) && Directory.GetFiles(Path.Combine(convert.Output0Folder, "songTitleLogo")).Length > 0;

        return new()
        {
            MapId = convert.SongID,
            ParentMapId = info.MapName,
            Title = info.Title,
            Artist = info.Artist,
            Credits = info.Credits,
            LyricsColor = lyricsColor,
            MapLength = endTime - startTime,
            OriginalJDVersion = info.OriginalJDVersion,
            CoachCount = info.NumCoach,
            Difficulty = info.Difficulty,
            SweatDifficulty = info.SweatDifficulty,
            Tags = [.. info.Tags],
            TagIds = [],
            SearchTagsLocIds = [],
            CoachNamesLocIds = [],
            HasSongTitleInCover = songTitleLogo
        };
    }
}

public class AssetFilesDict
{
    public Asset Cover { get; set; } = new();
    public Asset CoachesSmall { get; set; } = new();
    public Asset CoachesLarge { get; set; } = new();
    public Asset AudioPreview_opus { get; set; } = new();
    public Asset VideoPreview_MID_vp9_webm { get; set; } = new();
    [JsonPropertyName("songTitleLogo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Asset? SongTitleLogo { get; set; } = null;
    public Asset Audio_opus { get; set; } = new();
    public Asset Video_HIGH_vp9_webm { get; set; } = new();
    public Asset MapPackage { get; set; } = new();
}

public enum AssetType
{
    Cover = 1,
    CoachesSmall = 12,
    CoachesLarge = 13,
    AudioPreview_opus = 14,
    VideoPreview_MID_vp9_webm = 18,
    Audio_opus = 23,
    Video_HIGH_vp9_webm = 31,
    MapPackage = 36,
    SongTitleLogo = 37
}

public class Asset
{
    [JsonPropertyName("assetType")]
    public AssetType AssetType { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";
    [JsonPropertyName("ready")]
    public bool Ready { get; set; }
    [JsonPropertyName("size")]
    public uint Size { get; set; }
    [JsonPropertyName("category")]
    public uint Category { get; set; }
    [JsonPropertyName("filePath")]
    // If this is set, change the hash to the file's MD5 hash
    public string FilePath { get; set; } = "";
}

// Everything in this class seems to always be 0
public class Sizes
{
    [JsonPropertyName("totalSize")]
    public uint TotalSize { get; set; } = 0;
    [JsonPropertyName("commitSize")]
    public uint CommitSize { get; set; } = 0;
    [JsonPropertyName("baseAssetsSize")]
    public uint BaseAssetsSize { get; set; } = 0;
    [JsonPropertyName("runtimeAssetsSize")]
    public uint RuntimeAssetsSize { get; set; } = 0;
    [JsonPropertyName("runtimeCacheSize")]
    public uint RuntimeCacheSize { get; set; } = 0;
}
