namespace JustDanceEditor.Formats.UbiArt.FileSystem;

public class InputFolders(LayeredFileSystem fileSystem)
{
    public string InputFolder => fileSystem.ConversionRequest.InputPath;

    public string MapWorldFolder => fileSystem.VersionProfile.Layout != null
        ? fileSystem.VersionProfile.Layout.GetMapWorldFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.VersionProfile.Platform, fileSystem.VersionProfile.EngineVersion)
        : Path.Combine("world", "maps", fileSystem.SongName);

    public string MediaFolder => fileSystem.VersionProfile.Layout != null
        ? fileSystem.VersionProfile.Layout.GetMediaFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.VersionProfile.Platform, fileSystem.VersionProfile.EngineVersion)
        : Path.Combine(MapWorldFolder, "media");

    public string AudioFolder => fileSystem.VersionProfile.Layout != null
        ? fileSystem.VersionProfile.Layout.GetAudioFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.VersionProfile.Platform, fileSystem.VersionProfile.EngineVersion)
        : Path.Combine(MapWorldFolder, "audio");

    public string MenuArtFolder => Path.Combine(MapWorldFolder, "menuart", "textures");
    public string TimelineFolder => fileSystem.VersionProfile.Layout != null
        ? fileSystem.VersionProfile.Layout.GetTimelineFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.VersionProfile.Platform, fileSystem.VersionProfile.EngineVersion)
        : Path.Combine(MapWorldFolder, "timeline");
    public string PictosFolder => fileSystem.VersionProfile.Layout != null
        ? fileSystem.VersionProfile.Layout.GetPictosFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.VersionProfile.Platform, fileSystem.VersionProfile.EngineVersion)
        : Path.Combine(TimelineFolder, "pictos");
    public string MovesFolder => fileSystem.VersionProfile.Layout != null
        ? fileSystem.VersionProfile.Layout.GetMovesFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.VersionProfile.Platform, fileSystem.VersionProfile.EngineVersion)
        : Path.Combine(MapWorldFolder, "timeline", "moves", "wiiu");
}