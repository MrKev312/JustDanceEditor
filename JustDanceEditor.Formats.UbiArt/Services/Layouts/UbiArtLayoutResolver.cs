namespace JustDanceEditor.Formats.UbiArt.Services.Layouts;

public class UbiArtLayoutResolver : IUbiArtLayout
{
    public string GetMapWorldFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
    {
        // Engine-specific base path
        string engineFolder = engineVersion switch
        {
            UbiArtEngineVersion.JD2014 => Path.Combine("world", "jd5"),
            UbiArtEngineVersion.JD2015 => Path.Combine("world", "jd2015"),
            UbiArtEngineVersion.Modern => Path.Combine("world", "maps"),
            _ => Path.Combine("world", "maps"),
        };

        // For uncooked Modern layouts use World/Maps structure
        if (containerStyle == UbiArtContainerStyle.Uncooked)
        {
            if (engineVersion == UbiArtEngineVersion.Modern)
            {
                return string.IsNullOrWhiteSpace(songName) ? Path.Combine("World", "Maps") : Path.Combine("World", "Maps", songName);
            }

            // Uncooked JD2014/JD2015 are under world/maps/jdX
            engineFolder = Path.Combine("world", "maps", engineVersion == UbiArtEngineVersion.JD2014 ? "jd5" : "jd2015");
        }

        if (string.IsNullOrWhiteSpace(songName))
            return engineFolder;

        return Path.Combine(engineFolder, songName);
    }

    public string GetMediaFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, containerStyle, engineVersion), "media");

    public string GetAudioFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, containerStyle, engineVersion), "Audio");

    public string GetTimelineFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, containerStyle, engineVersion), "timeline");

    public string GetPictosFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetTimelineFolder(inputPath, songName, containerStyle, engineVersion), "pictos");

    public string GetMovesFolder(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, containerStyle, engineVersion), "timeline", "moves", "WiiU");

    public string GetSongDescRelativePath(string inputPath, string songName, UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, containerStyle, engineVersion), "songdesc.tpl");
}