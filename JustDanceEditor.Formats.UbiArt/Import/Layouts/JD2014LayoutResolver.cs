using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Layouts;

public sealed class JD2014LayoutResolver : UbiArtLayoutResolver
{
    public override string GetMapWorldFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        string engineFolder = Path.Combine("world", "jd5");

        if (string.IsNullOrWhiteSpace(songName))
            return engineFolder;

        return Path.Combine(engineFolder, songName);
    }
}
