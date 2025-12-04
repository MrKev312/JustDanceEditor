using JustDanceEditor.Formats.Unity.Models;

namespace JustDanceEditor.Formats.Unity;

public readonly record struct OfflineCacheAssets(
    string CoverName,
    string CoachesSmallName,
    string CoachesLargeName,
    string AudioPreviewName,
    string VideoPreviewName,
    string AudioName,
    string VideoName,
    string MapPackageName,
    string? SongTitleLogoName);

public static class UnityCacheBuilder
{
    public static Dictionary<Guid, JDSong> BuildOfflineCache(UnityExportData songData, Guid songId, uint cacheNumber, OfflineCacheAssets assets, string songTitleLogoFolder)
    {
        SongDatabaseEntry songEntry = UnityFormatMapper.BuildCacheSong(songData, songId, songTitleLogoFolder);
        JDSong jdSong = JDSongFactory.CreateSong(
            songEntry,
            cacheNumber,
            assets.CoverName,
            assets.CoachesSmallName,
            assets.CoachesLargeName,
            assets.AudioPreviewName,
            assets.VideoPreviewName,
            assets.AudioName,
            assets.VideoName,
            assets.MapPackageName,
            assets.SongTitleLogoName,
            songId);

        return new Dictionary<Guid, JDSong>
        {
            { songId, jdSong }
        };
    }

    public static ServerSongJSON BuildServerCache(UnityExportData songData, Guid songId, string songTitleLogoFolder)
    {
        return UnityFormatMapper.BuildServerSong(songData, songId, songTitleLogoFolder);
    }
}
