namespace JustDanceEditor.Formats.UbiArt.Import.Layouts;

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
                return string.IsNullOrWhiteSpace(songName) ? Path.Combine("world", "maps") : Path.Combine("world", "maps", songName);
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
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "audio");

    public string GetTimelineFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "timeline");

    public string GetPictosFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetTimelineFolder(inputPath, songName, platform, engineVersion), "pictos");

    public string GetMovesFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        string movesPlatform = platform == UbiArtPlatform.Uncooked ? "wiiu" : platform.GetCookedFolderName();
        return Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "timeline", "moves", movesPlatform);
    }

    public string GetSongDescRelativePath(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
        => Path.Combine(GetMapWorldFolder(inputPath, songName, platform, engineVersion), "songdesc.tpl");
}
