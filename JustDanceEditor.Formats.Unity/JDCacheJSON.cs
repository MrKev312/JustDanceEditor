using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.Unity;

public class JDCacheJSON
{
    [JsonPropertyName("schemaVersion")]
    public uint SchemaVersion { get; set; } = 1;

    [JsonPropertyName("mapsDict")]
    public Dictionary<Guid, JDSong> MapsDict { get; set; } = [];
}

public class JDSong
{
    [JsonPropertyName("songDatabaseEntry")]
    public SongDatabaseEntry SongDatabaseEntry { get; set; } = new();

    [JsonPropertyName("audioPreviewTrk")]
    public string AudioPreviewTrk { get; set; } = string.Empty;

    [JsonPropertyName("assetFilesDict")]
    public AssetFilesDict AssetFilesDict { get; set; } = new();

    [JsonPropertyName("sizes")]
    public Sizes Sizes { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hasSongTitleInCover")]
    public bool? HasSongTitleInCover { get; set; }
}

public class SongDatabaseEntry
{
    public Guid MapId { get; set; }
    public string ParentMapId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Credits { get; set; } = string.Empty;
    public string LyricsColor { get; set; } = string.Empty;
    public double MapLength { get; set; }
    public uint OriginalJDVersion { get; set; }
    public int CoachCount { get; set; }
    public uint Difficulty { get; set; }
    public uint SweatDifficulty { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> TagIds { get; set; } = [];
    public List<uint> SearchTagsLocIds { get; set; } = [];
    public List<uint> CoachNamesLocIds { get; set; } = [];

    [JsonPropertyName("hasSongTitleInCover")]
    public bool HasSongTitleInCover { get; set; }
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
    public Asset? SongTitleLogo { get; set; }

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
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    [JsonPropertyName("size")]
    public uint Size { get; set; }

    [JsonPropertyName("category")]
    public uint Category { get; set; }

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;
}

public class Sizes
{
    [JsonPropertyName("totalSize")]
    public uint TotalSize { get; set; }

    [JsonPropertyName("commitSize")]
    public uint CommitSize { get; set; }

    [JsonPropertyName("baseAssetsSize")]
    public uint BaseAssetsSize { get; set; }

    [JsonPropertyName("runtimeAssetsSize")]
    public uint RuntimeAssetsSize { get; set; }

    [JsonPropertyName("runtimeCacheSize")]
    public uint RuntimeCacheSize { get; set; }
}
