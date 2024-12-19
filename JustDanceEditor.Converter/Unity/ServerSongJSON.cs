using JustDanceEditor.Converter.Converters;
using JustDanceEditor.Converter.UbiArt;

namespace JustDanceEditor.Converter.Unity;

// Used for custom servers
public class ServerSongJSON
{
    public Guid SongID { get; set; }
    public string Artist { get; set; } = "";
    public uint CoachCount { get; set; }
    public int[] CoachNamesLocIds { get; set; } = [];
    public string Credits { get; set; } = "";
    public int DanceVersionLocId { get; set; } = 0;
    public uint Difficulty { get; set; }
    public object? DoubleScoringType { get; set; }
    public bool HasSongTitleInCover { get; set; }
    public string LyricsColor { get; set; } = "#FFFFFFFF";
    public float MapLength { get; set; }
    public string MapName { get; set; } = "";
    public uint OriginalJDVersion { get; set; }
    public string ParentMapName { get; set; } = "";
    public uint SweatDifficulty { get; set; }
    public string[] TagIds { get; set; } = [];
    public string[] Tags { get; set; } = [];
    public string Title { get; set; } = "";

    public static explicit operator ServerSongJSON(ConvertUbiArtToUnity convert)
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

        // Create the hex string in the format #RRGGBBAA
        string lyricsColor = $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}";

        // Get startBeat and endBeat
        int startBeat = Math.Abs(convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.startBeat);
        int endBeat = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.endBeat - startBeat;

        if (endBeat >= convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers.Length)
            endBeat = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers.Length - 1;

        // Use markers to get the length of the song
        float startTime = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[startBeat] / 48f / 1000f;
        float endTime = convert.SongData.MusicTrack.COMPONENTS[0].trackData.structure.markers[endBeat] / 48f / 1000f;

        string songTitleLogoPath = convert.FileSystem.OutputFolders.SongTitleLogoFolder;
        bool songTitleLogo = Directory.Exists(songTitleLogoPath) && Directory.GetFiles(songTitleLogoPath).Length > 0;

        List<string> tags = ["Custom", "Main"];

        return new()
        {
            SongID = convert.SongID,
            MapName = info.MapName,
            ParentMapName = info.MapName,
            Title = info.Title,
            Artist = info.Artist,
            Credits = info.Credits,
            LyricsColor = lyricsColor,
            MapLength = endTime - startTime,
            OriginalJDVersion = convert.SongData.JDVersion,
            CoachCount = info.NumCoach,
            Difficulty = info.Difficulty,
            SweatDifficulty = Math.Clamp(info.SweatDifficulty + 1, 1, 3),
            Tags = [.. tags],
            TagIds = [.. info.Tags.Except(tags)],
            CoachNamesLocIds = [],
            HasSongTitleInCover = songTitleLogo
        };
    }
}
