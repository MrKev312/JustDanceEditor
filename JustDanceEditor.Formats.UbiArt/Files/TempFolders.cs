using JustDanceEditor.Logging;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class TempFolders(FileSystem fileSystem)
{
    private readonly FileSystem fileSystem = fileSystem;

    public string MapFolder => Path.Combine(Path.GetTempPath(), "JustDanceEditor", fileSystem.SongName);
    public string AudioFolder => Path.Combine(MapFolder, "audio");

    public void CreateTempFolders()
    {
        if (Directory.Exists(MapFolder))
        {
            Logger.Log("Deleting the old temp folder", LogLevel.Debug);
            Directory.Delete(MapFolder, true);
        }

        Logger.Log("Creating temp folders", LogLevel.Debug);
        Directory.CreateDirectory(MapFolder);
        Directory.CreateDirectory(AudioFolder);
    }

    public void Delete()
    {
        Logger.Log("Deleting the temp folder", LogLevel.Debug);
        Directory.Delete(MapFolder, true);
    }
}