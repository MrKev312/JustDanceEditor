using System.Text.Json.Serialization;

namespace JustDanceEditor.UI.Converting;

internal static class JDSongJSONBuilder
{
    public static void UpdateSong(JDCacheSong songData, uint cacheNumber)
    {
        songData.AssetFilesDict.CoachesSmall.FilePath = $"/CacheStorage_{cacheNumber}/{songData.SongDatabaseEntry.MapId}/CoachesSmall/{songData.AssetFilesDict.CoachesSmall.Hash}";
        songData.AssetFilesDict.CoachesLarge.FilePath = $"/CacheStorage_{cacheNumber}/{songData.SongDatabaseEntry.MapId}/CoachesLarge/{songData.AssetFilesDict.CoachesLarge.Hash}";
        songData.AssetFilesDict.Audio_opus.FilePath = $"/CacheStorage_{cacheNumber}/{songData.SongDatabaseEntry.MapId}/Audio_opus/{songData.AssetFilesDict.Audio_opus.Hash}";
        songData.AssetFilesDict.Video_HIGH_vp9_webm.FilePath = $"/CacheStorage_{cacheNumber}/{songData.SongDatabaseEntry.MapId}/Video_HIGH_vp9_webm/{songData.AssetFilesDict.Video_HIGH_vp9_webm.Hash}";
        songData.AssetFilesDict.MapPackage.FilePath = $"/CacheStorage_{cacheNumber}/{songData.SongDatabaseEntry.MapId}/MapPackage/{songData.AssetFilesDict.MapPackage.Hash}";
    }

    public static string CacheJson(uint cacheNumber, Guid guid) => GenerateJson(cacheNumber, guid.ToString());

    public static string CacheJson(uint cacheNumber, string path) => GenerateJson(cacheNumber, path);

    public static string MapBaseCacheJson() => GenerateJson(0, "MapBaseCache");

    public static string AddressablesJson() => GenerateJson(0, "Addressables");

    private static string GenerateJson(uint cacheNumber, string path)
    {
        return $$"""
        {
          "$type": "JD.CacheSystem.JDNCache, Ubisoft.JustDance.CacheSystem",
          "totalSize": 0,
          "free": 71568604,
          "journal": 0,
          "cachedStreamsDict": {
            "$type": "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[JD.CacheSystem.CacheWriteJob, Ubisoft.JustDance.CacheSystem]], mscorlib"
          },
          "name": "{{Path.GetFileName(path)}}",
          "path": "/CacheStorage_{{cacheNumber}}/{{path}}",
          "pathNX": "CacheStorage_{{cacheNumber}}:/{{path}}",
          "index": {{cacheNumber}}
        }
        """;
    }
}

internal sealed class JDCacheJSON
{
    [JsonPropertyName("schemaVersion")]
    public uint SchemaVersion { get; set; } = 1;

    [JsonPropertyName("mapsDict")]
    public Dictionary<Guid, JDCacheSong> MapsDict { get; set; } = [];
}

internal sealed class JDCacheSong
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

internal sealed class SongDatabaseEntry
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

internal sealed class AssetFilesDict
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

internal enum AssetType
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

internal sealed class Asset
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

internal sealed class Sizes
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
