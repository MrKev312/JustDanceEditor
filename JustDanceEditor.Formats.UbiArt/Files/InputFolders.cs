namespace JustDanceEditor.Formats.UbiArt.Files;

public class InputFolders(FileSystem fileSystem)
{
    public string InputFolder => fileSystem.ConversionRequest.InputPath;

    public string MapWorldFolder => fileSystem.Layout != null
        ? fileSystem.Layout.GetMapWorldFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.ContainerStyle, fileSystem.EngineVersion)
        : Path.Combine("world", "maps", fileSystem.SongName);

    public string MediaFolder => fileSystem.Layout != null
        ? fileSystem.Layout.GetMediaFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.ContainerStyle, fileSystem.EngineVersion)
        : Path.Combine(MapWorldFolder, "media");

    public string AudioFolder => fileSystem.Layout != null
        ? fileSystem.Layout.GetAudioFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.ContainerStyle, fileSystem.EngineVersion)
        : Path.Combine(MapWorldFolder, "audio");

    public string MenuArtFolder => Path.Combine(MapWorldFolder, "menuart", "textures");
    public string TimelineFolder => fileSystem.Layout != null
        ? fileSystem.Layout.GetTimelineFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.ContainerStyle, fileSystem.EngineVersion)
        : Path.Combine(MapWorldFolder, "timeline");
    public string PictosFolder => fileSystem.Layout != null
        ? fileSystem.Layout.GetPictosFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.ContainerStyle, fileSystem.EngineVersion)
        : Path.Combine(TimelineFolder, "pictos");
    public string MovesFolder => fileSystem.Layout != null
        ? fileSystem.Layout.GetMovesFolder(fileSystem.ConversionRequest.InputPath, fileSystem.SongName, fileSystem.ContainerStyle, fileSystem.EngineVersion)
        : Path.Combine(MapWorldFolder, "timeline", "moves", "wiiu");
}