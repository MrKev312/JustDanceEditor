using JustDanceEditor.Converter.Core;
using JustDanceEditor.Formats.Unity;

namespace JustDanceEditor.Converter.Unity;

public static class UnityFormatMapper
{
    public static SongDatabaseEntry BuildSongDatabaseEntry(ConversionContext context)
    {
        UnityExportData song = context.RequireUnityData();
        UnityExportMetadata meta = song.Metadata;
        string songTitleLogoPath = context.FileSystem.OutputFolders.SongTitleLogoFolder;
        bool songTitleLogo = Directory.Exists(songTitleLogoPath) && Directory.GetFiles(songTitleLogoPath).Length > 0;
        double mapLength = ResolveMapLength(song);

        return new SongDatabaseEntry
        {
            MapId = context.SongID,
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
            Tags = meta.Tags.ToList(),
            TagIds = [],
            SearchTagsLocIds = [],
            CoachNamesLocIds = [],
            HasSongTitleInCover = songTitleLogo
        };
    }

    public static ServerSongJSON BuildServerSong(ConversionContext context)
    {
        UnityExportData song = context.RequireUnityData();
        UnityExportMetadata meta = song.Metadata;
        double mapLength = ResolveMapLength(song);
        string songTitleLogoPath = context.FileSystem.OutputFolders.SongTitleLogoFolder;
        bool songTitleLogo = Directory.Exists(songTitleLogoPath) && Directory.GetFiles(songTitleLogoPath).Length > 0;

        List<string> tags = ["Custom", "Main"];
        IEnumerable<string> additionalTags = meta.Tags.Where(t => !tags.Contains(t, StringComparer.OrdinalIgnoreCase));

        return new ServerSongJSON
        {
            SongID = context.SongID,
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
            Tags = tags.ToArray(),
            TagIds = additionalTags.ToArray(),
            CoachNamesLocIds = [],
            HasSongTitleInCover = songTitleLogo
        };
    }

    private static double ResolveMapLength(UnityExportData data)
    {
        if (data.Metadata.MapLengthSeconds > 0)
            return data.Metadata.MapLengthSeconds;

        int[] markers = data.Structure.markers ?? Array.Empty<int>();
        if (markers.Length < 2)
            return Math.Max(0, data.Structure.endBeat - data.Structure.startBeat);

        double startSeconds = markers[0] / (48d * 1000d);
        double endSeconds = markers[^1] / (48d * 1000d);
        double length = endSeconds - startSeconds;
        return length > 0 ? length : (data.Structure.endBeat - data.Structure.startBeat);
    }
}
