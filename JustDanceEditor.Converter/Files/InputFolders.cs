namespace JustDanceEditor.Converter.Files;

public class InputFolders
{
    public InputFolders(FileSystem fileSystem)
    {
        this.fileSystem = fileSystem;
    }

    readonly FileSystem fileSystem;
    public string InputFolder => fileSystem.ConversionRequest.InputPath;


    public string MapCacheFolder => Path.Combine("cache", "itf_cooked", fileSystem.PlatformType, "world", "maps", fileSystem.SongName);
    public string MapWorldFolder => Path.Combine("world", "maps", fileSystem.SongName);

    public string MediaFolder => Path.Combine(MapWorldFolder, "media");

    public string AudioFolder => Path.Combine(MapCacheFolder, "audio");
    public string MenuArtFolder => Path.Combine(MapCacheFolder, "menuart", "textures");
    public string TimelineFolder => Path.Combine(MapCacheFolder, "timeline", fileSystem.SongName);
    public string PictosFolder => Path.Combine(TimelineFolder, "pictos");
    // WiiU is hardcoded here, but should be a variable, NX => WiiU, rest idk
    public string MovesFolder => Path.Combine(MapWorldFolder, "timeline", "moves", "wiiu");
}
