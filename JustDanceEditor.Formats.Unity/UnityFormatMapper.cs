using JustDanceEditor.Formats.Unity.Models;

namespace JustDanceEditor.Formats.Unity;

public static class UnityFormatMapper
{
    public static SongDatabaseEntry BuildCacheSong(UnityExportData song, Guid songId, string songTitleLogoFolder)
    {
        UnityExportMetadata meta = song.Metadata;
        bool songTitleLogo = ContainsAssets(songTitleLogoFolder);
        double mapLength = ResolveMapLength(song);

        return new SongDatabaseEntry
        {
            MapId = songId,
            ParentMapId = meta.MapName,
            Title = meta.Title,
            Artist = meta.Artist,
            Credits = meta.Credits,
            LyricsColor = meta.LyricsColor,
            MapLength = mapLength,
            OriginalJDVersion = meta.OriginalJdVersion,
            CoachCount = meta.CoachCount,
            Difficulty = meta.Difficulty,
            SweatDifficulty = (uint)Math.Clamp((int)meta.SweatDifficulty + 1, 1, 3),
            Tags = [.. meta.Tags],
            TagIds = [],
            SearchTagsLocIds = [],
            CoachNamesLocIds = [],
            HasSongTitleInCover = songTitleLogo
        };
    }

    public static ServerSongJSON BuildServerSong(UnityExportData song, Guid songId, string songTitleLogoFolder)
    {
        UnityExportMetadata meta = song.Metadata;
        double mapLength = ResolveMapLength(song);
        bool songTitleLogo = ContainsAssets(songTitleLogoFolder);

        List<string> tags = ["Custom", "Main"];
        IEnumerable<string> additionalTags = meta.Tags.Where(t => !tags.Contains(t, StringComparer.OrdinalIgnoreCase));

        return new ServerSongJSON
        {
            SongID = songId,
            MapName = meta.MapName,
            ParentMapName = meta.MapName,
            Title = meta.Title,
            Artist = meta.Artist,
            Credits = meta.Credits,
            LyricsColor = meta.LyricsColor,
            MapLength = (float)mapLength,
            OriginalJDVersion = meta.OriginalJdVersion,
            CoachCount = meta.CoachCount,
            Difficulty = meta.Difficulty,
            SweatDifficulty = (uint)Math.Clamp((int)meta.SweatDifficulty + 1, 1, 3),
            Tags = [.. tags],
            TagIds = [.. additionalTags],
            CoachNamesLocIds = [],
            HasSongTitleInCover = songTitleLogo
        };
    }

    private static double ResolveMapLength(UnityExportData data)
    {
        if (data.Metadata.MapLength > 0)
            return data.Metadata.MapLength;

        int[] markers = data.Structure.markers ?? [];
        if (markers.Length < 2)
            return Math.Max(0, data.Structure.endBeat - data.Structure.startBeat);

        double startSeconds = markers[0] / (48d * 1000d);
        double endSeconds = markers[^1] / (48d * 1000d);
        double length = endSeconds - startSeconds;
        return length > 0 ? length : (data.Structure.endBeat - data.Structure.startBeat);
    }

    private static bool ContainsAssets(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return false;
        if (!Directory.Exists(folder))
            return false;
        return Directory.EnumerateFiles(folder).Any();
    }
}
