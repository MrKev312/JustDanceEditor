using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Cache;

internal static class UnityOfflineCacheExporter
{
    public static void Publish(
        IntermediateSongPackage package,
        string generatedRoot,
        string cacheRoot,
        uint cacheNumber,
        ILogger logger)
    {
        if (cacheNumber == 0)
            throw new ArgumentOutOfRangeException(nameof(cacheNumber), "Offline cache runtime assets must use a cache number greater than 0.");

        Guid mapId = package.Metadata.SongID == Guid.Empty ? Guid.NewGuid() : package.Metadata.SongID;
        string baseSongRoot = Path.Combine(cacheRoot, UnityCacheLayout.GetCacheFolderName(0), "MapBaseCache", mapId.ToString());
        string runtimeSongRoot = Path.Combine(cacheRoot, UnityCacheLayout.GetCacheFolderName(cacheNumber), mapId.ToString());

        ReplaceDirectory(baseSongRoot);
        ReplaceDirectory(runtimeSongRoot);

        CacheAsset cover = CopyRequiredAsset(generatedRoot, "Cover", Path.Combine(baseSongRoot, "Cover"));
        CacheAsset audioPreview = CopyRequiredAsset(generatedRoot, "AudioPreview_opus", Path.Combine(baseSongRoot, "AudioPreview_opus"));
        CacheAsset videoPreview = CopyRequiredAsset(generatedRoot, "videoPreview", Path.Combine(baseSongRoot, "VideoPreview_MID_vp9_webm"), preferLargest: true);
        CacheAsset? songTitleLogo = CopyOptionalAsset(generatedRoot, "songTitleLogo", Path.Combine(baseSongRoot, "songTitleLogo"));

        CacheAsset coachesSmall = CopyRequiredAsset(generatedRoot, "CoachesSmall", Path.Combine(runtimeSongRoot, "CoachesSmall"));
        CacheAsset coachesLarge = CopyRequiredAsset(generatedRoot, "CoachesLarge", Path.Combine(runtimeSongRoot, "CoachesLarge"));
        CacheAsset audio = CopyRequiredAsset(generatedRoot, "Audio_opus", Path.Combine(runtimeSongRoot, "Audio_opus"));
        CacheAsset video = CopyRequiredAsset(generatedRoot, "video", Path.Combine(runtimeSongRoot, "Video_HIGH_vp9_webm"), preferLargest: true);
        CacheAsset mapPackage = CopyRequiredAsset(generatedRoot, "MapPackage", Path.Combine(runtimeSongRoot, "MapPackage"));

        JDCacheSong cacheSong = JDSongJSONBuilder.CreateSong(
            BuildSongDatabaseEntry(package.Metadata, mapId, songTitleLogo != null),
            cacheNumber,
            cover.FileName,
            coachesSmall.FileName,
            coachesLarge.FileName,
            audioPreview.FileName,
            videoPreview.FileName,
            audio.FileName,
            video.FileName,
            mapPackage.FileName,
            songTitleLogo?.FileName,
            mapId);

        ApplySizes(cacheSong, cover, audioPreview, videoPreview, songTitleLogo, coachesSmall, coachesLarge, audio, video, mapPackage);
        WriteRuntimeCacheJson(runtimeSongRoot, cacheNumber, mapId);
        UpsertCachingStatus(cacheRoot, mapId, cacheSong, logger);
        logger.LogInformation("Published '{MapName}' to Unity offline cache '{CacheRoot}'.", package.Metadata.MapName ?? package.Metadata.Title ?? mapId.ToString(), cacheRoot);
    }

    private static SongDatabaseEntry BuildSongDatabaseEntry(IntermediateMetadata metadata, Guid mapId, bool hasSongTitleLogo)
    {
        metadata.AdditionalMetadata ??= [];
        metadata.AdditionalMetadata.TryGetValue(ServerSongJSON.TagIdsKey, out string? tagIdsRaw);

        return new SongDatabaseEntry
        {
            MapId = mapId,
            ParentMapId = string.IsNullOrWhiteSpace(metadata.ParentMapName) ? metadata.MapName ?? string.Empty : metadata.ParentMapName,
            Title = metadata.Title ?? string.Empty,
            Artist = metadata.Artist ?? string.Empty,
            Credits = metadata.Credits ?? string.Empty,
            LyricsColor = string.IsNullOrWhiteSpace(metadata.LyricsColor) ? "#FFFFFFFF" : metadata.LyricsColor,
            MapLength = metadata.MapLengthSeconds,
            OriginalJDVersion = metadata.OriginalJDVersion,
            CoachCount = metadata.CoachCount,
            Difficulty = metadata.Difficulty,
            SweatDifficulty = metadata.SweatDifficulty,
            Tags = metadata.Tags?.ToList() ?? [],
            TagIds = SplitCsv(tagIdsRaw),
            CoachNamesLocIds = ParseLocIds(metadata.CoachNamesLocIds),
            HasSongTitleInCover = hasSongTitleLogo
        };
    }

    private static void UpsertCachingStatus(string cacheRoot, Guid mapId, JDCacheSong cacheSong, ILogger logger)
    {
        string cachingStatusPath = Path.Combine(cacheRoot, UnityCacheLayout.GetCacheFolderName(0), "MapBaseCache", "CachingStatus.json");
        JDCacheJSON cacheJson;

        if (File.Exists(cachingStatusPath))
        {
            using FileStream stream = File.OpenRead(cachingStatusPath);
            cacheJson = JsonSerializer.Deserialize<JDCacheJSON>(stream, UnityCacheJson.Options) ?? new JDCacheJSON();
        }
        else
        {
            cacheJson = new JDCacheJSON();
        }

        cacheJson.MapsDict[mapId] = cacheSong;
        File.WriteAllText(cachingStatusPath, JsonSerializer.Serialize(cacheJson, UnityCacheJson.Options));
        logger.LogInformation("Updated {Path}", cachingStatusPath);
    }

    private static void WriteRuntimeCacheJson(string runtimeSongRoot, uint cacheNumber, Guid mapId)
    {
        Directory.CreateDirectory(runtimeSongRoot);
        File.WriteAllText(Path.Combine(runtimeSongRoot, "json.cache"), JDSongJSONBuilder.CacheJson(cacheNumber, mapId));
    }

    private static CacheAsset CopyRequiredAsset(string generatedRoot, string sourceFolderName, string destinationFolder, bool preferLargest = false)
    {
        CacheAsset? asset = CopyAsset(generatedRoot, sourceFolderName, destinationFolder, preferLargest);
        return asset ?? throw new FileNotFoundException($"Unity offline cache export could not find generated asset folder '{sourceFolderName}'.");
    }

    private static CacheAsset? CopyOptionalAsset(string generatedRoot, string sourceFolderName, string destinationFolder, bool preferLargest = false) =>
        CopyAsset(generatedRoot, sourceFolderName, destinationFolder, preferLargest);

    private static CacheAsset? CopyAsset(string generatedRoot, string sourceFolderName, string destinationFolder, bool preferLargest)
    {
        string sourceFolder = Path.Combine(generatedRoot, sourceFolderName);
        if (!Directory.Exists(sourceFolder))
            return null;

        IEnumerable<string> files = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.TopDirectoryOnly);
        string? source = preferLargest
            ? files.OrderByDescending(file => new FileInfo(file).Length).ThenBy(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

        if (source == null)
            return null;

        Directory.CreateDirectory(destinationFolder);
        string fileName = Path.GetFileName(source);
        string destination = Path.Combine(destinationFolder, fileName);
        File.Copy(source, destination, true);
        return new CacheAsset(fileName, ToUIntSize(new FileInfo(destination).Length));
    }

    private static void ApplySizes(
        JDCacheSong song,
        CacheAsset cover,
        CacheAsset audioPreview,
        CacheAsset videoPreview,
        CacheAsset? songTitleLogo,
        CacheAsset coachesSmall,
        CacheAsset coachesLarge,
        CacheAsset audio,
        CacheAsset video,
        CacheAsset mapPackage)
    {
        SetSize(song.AssetFilesDict.Cover, cover);
        SetSize(song.AssetFilesDict.AudioPreview_opus, audioPreview);
        SetSize(song.AssetFilesDict.VideoPreview_MID_vp9_webm, videoPreview);
        SetSize(song.AssetFilesDict.CoachesSmall, coachesSmall);
        SetSize(song.AssetFilesDict.CoachesLarge, coachesLarge);
        SetSize(song.AssetFilesDict.Audio_opus, audio);
        SetSize(song.AssetFilesDict.Video_HIGH_vp9_webm, video);
        SetSize(song.AssetFilesDict.MapPackage, mapPackage);

        if (songTitleLogo != null && song.AssetFilesDict.SongTitleLogo != null)
            SetSize(song.AssetFilesDict.SongTitleLogo, songTitleLogo);

        uint baseSize = Sum(cover, audioPreview, videoPreview, songTitleLogo);
        uint runtimeSize = Sum(coachesSmall, coachesLarge, audio, video, mapPackage);
        song.Sizes.BaseAssetsSize = baseSize;
        song.Sizes.RuntimeAssetsSize = runtimeSize;
        song.Sizes.RuntimeCacheSize = runtimeSize;
        song.Sizes.CommitSize = Sum(baseSize, runtimeSize);
        song.Sizes.TotalSize = song.Sizes.CommitSize;
    }

    private static void SetSize(Asset asset, CacheAsset cacheAsset) => asset.Size = cacheAsset.Size;

    private static uint Sum(params CacheAsset?[] assets)
    {
        ulong total = 0;
        foreach (CacheAsset? asset in assets)
        {
            if (asset != null)
                total += asset.Size;
        }

        return total > uint.MaxValue ? uint.MaxValue : (uint)total;
    }

    private static uint Sum(uint first, uint second)
    {
        ulong total = (ulong)first + second;
        return total > uint.MaxValue ? uint.MaxValue : (uint)total;
    }

    private static uint ToUIntSize(long size) => size <= 0 ? 0 : size > uint.MaxValue ? uint.MaxValue : (uint)size;

    private static List<string> SplitCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static List<uint> ParseLocIds(JdiLocId[]? values)
    {
        if (values == null || values.Length == 0)
            return [];

        List<uint> locIds = [];
        foreach (JdiLocId value in values)
        {
            if (uint.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint locId))
                locIds.Add(locId);
        }

        return locIds;
    }

    private static void ReplaceDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);

        Directory.CreateDirectory(path);
    }

    private sealed record CacheAsset(string FileName, uint Size);
}
