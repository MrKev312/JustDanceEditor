using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class TempFolders(FileSystem fileSystem, ILogger<FileSystem> logger)
{
    private readonly FileSystem fileSystem = fileSystem;
    private readonly ILogger<FileSystem> _logger = logger;

    public string MapFolder => Path.Combine(Path.GetTempPath(), "JustDanceEditor", fileSystem.SongName);
    public string AudioFolder => Path.Combine(MapFolder, "audio");

    public void CreateTempFolders()
    {
        if (Directory.Exists(MapFolder))
        {
            _logger.LogDebug("Deleting the old temp folder");
            Directory.Delete(MapFolder, true);
        }

        _logger.LogDebug("Creating temp folders");
        Directory.CreateDirectory(MapFolder);
        Directory.CreateDirectory(AudioFolder);
    }

    public void Delete()
    {
        _logger.LogDebug("Deleting the temp folder");
        Directory.Delete(MapFolder, true);
    }
}