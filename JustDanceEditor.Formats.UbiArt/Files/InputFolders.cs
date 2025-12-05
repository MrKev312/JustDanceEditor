namespace JustDanceEditor.Formats.UbiArt.Files;

public class InputFolders(FileSystem fileSystem)
{
    public string InputFolder => fileSystem.ConversionRequest.InputPath;

    public string MapWorldFolder => Path.Combine("world", "maps", fileSystem.SongName);

    public string MediaFolder => Path.Combine(MapWorldFolder, "media");

    public string AudioFolder => Path.Combine(MapWorldFolder, "audio");
    public string MenuArtFolder => Path.Combine(MapWorldFolder, "menuart", "textures");
    public string TimelineFolder => Path.Combine(MapWorldFolder, "timeline");
    public string PictosFolder => Path.Combine(TimelineFolder, "pictos");
    public string MovesFolder => Path.Combine(MapWorldFolder, "timeline", "moves", "wiiu");
}