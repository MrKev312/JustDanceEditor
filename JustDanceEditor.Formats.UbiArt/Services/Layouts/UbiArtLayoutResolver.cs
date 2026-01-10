namespace JustDanceEditor.Formats.UbiArt.Services.Layouts;

public class UbiArtLayoutResolver : IUbiArtLayout
{
    public string GetMapWorldFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        // Engine-specific base path
        string engineFolder = engineVersion switch
        {
            UbiArtEngineVersion.JD2014 => Path.Combine("world", "jd5"),
            UbiArtEngineVersion.JD2015 => Path.Combine("world", "jd2015"),
            _ => Path.Combine("world", "maps"),
        };

        // For uncooked layouts use World/Maps structure
        if (platform == UbiArtPlatform.Uncooked)
        {
            if (engineVersion >= UbiArtEngineVersion.JD2016)
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

    public string GetMediaFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "media");

    public string GetAudioFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "Audio");

    public string GetTimelineFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "timeline");

    public string GetPictosFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetTimelineFolder(inputPath, songName, platform, engineVersion), "pictos");

    public string GetMovesFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "timeline", "moves", "WiiU");

    public string GetSongDescRelativePath(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "songdesc.tpl");
}