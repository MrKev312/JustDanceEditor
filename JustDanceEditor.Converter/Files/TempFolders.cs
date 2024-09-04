using JustDanceEditor.Logging;

namespace JustDanceEditor.Converter.Files;

public class TempFolders(FileSystem fileSystem)
{
    readonly FileSystem filseSystem = fileSystem;

    // Temporary folders
    public string MapFolder => Path.Combine(Path.GetTempPath(), "JustDanceEditor", filseSystem.SongName);
    public string PictoFolder => Path.Combine(MapFolder, "pictos");
    public string PictoAtlasFolder => Path.Combine(PictoFolder, "Atlas");
    public string MenuArtFolder => Path.Combine(MapFolder, "menuart");
    public string AudioFolder => Path.Combine(MapFolder, "audio");
    public string VideoFolder => Path.Combine(MapFolder, "video");

    public void CreateTempFolders()
    {
        // Delete the old temp folder
        if (Directory.Exists(MapFolder))
        {
            Logger.Log("Deleting the old temp folder", LogLevel.Debug);
            Directory.Delete(MapFolder, true);
        }

        // Create the folders
        Logger.Log("Creating temp folders", LogLevel.Debug);
        Directory.CreateDirectory(MapFolder);
        Directory.CreateDirectory(PictoFolder);
        Directory.CreateDirectory(PictoAtlasFolder);
        Directory.CreateDirectory(MenuArtFolder);
        Directory.CreateDirectory(AudioFolder);
        Directory.CreateDirectory(VideoFolder);
    }

    public void Delete()
    {
        // Delete the temp folder
        Logger.Log("Deleting the temp folder", LogLevel.Debug);
        Directory.Delete(MapFolder, true);
    }
}
