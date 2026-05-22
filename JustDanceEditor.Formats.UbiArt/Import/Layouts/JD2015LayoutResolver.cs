using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Layouts;

public sealed class JD2015LayoutResolver : UbiArtLayoutResolver
{
    public override string GetMapWorldFolder(string inputPath, string songName, UbiArtPlatform platform, UbiArtEngineVersion engineVersion)
    {
        string engineFolder = platform == UbiArtPlatform.Uncooked
            ? Path.Combine("world", "maps", "jd2015")
            : Path.Combine("world", "jd2015");

        if (string.IsNullOrWhiteSpace(songName))
            return engineFolder;

        return Path.Combine(engineFolder, songName);
    }
}