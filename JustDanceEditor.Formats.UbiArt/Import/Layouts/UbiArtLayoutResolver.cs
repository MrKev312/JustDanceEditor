using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Layouts;

public class UbiArtLayoutResolver : IUbiArtLayout
{
    public virtual string GetMapWorldFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        string engineFolder = Path.Combine("world", "maps");

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