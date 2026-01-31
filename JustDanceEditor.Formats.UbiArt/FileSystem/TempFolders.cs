using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.FileSystem;

public class TempFolders(LayeredFileSystem fileSystem, ILogger<LayeredFileSystem> logger, ITempFolderManager tempManager)
{
    private readonly LayeredFileSystem fileSystem = fileSystem;
    private readonly ILogger<LayeredFileSystem> _logger = logger;
    private readonly ITempFolderManager _tempManager = tempManager;

    public string MapFolder => _tempManager.GetMapFolder(fileSystem.SongName);
    public string AudioFolder => _tempManager.GetAudioFolder(fileSystem.SongName);

    public void CreateTempFolders()
    {
        try
        {
            _logger.LogDebug("Creating temp folders via ITempFolderManager");
            _tempManager.CreateMapFolder(fileSystem.SongName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create temp folders for '{MapName}': {Message}", fileSystem.SongName, ex.Message);
        }
    }

    public void Delete()
    {
        DeleteMap(fileSystem.SongName);
    }

    public void DeleteMap(string mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            return;

        try
        {
            _logger.LogDebug("Deleting the temp folder for map '{MapName}' via ITempFolderManager", mapName);
            _tempManager.DeleteMapFolder(mapName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp folder for '{MapName}': {Message}", mapName, ex.Message);
        }
    }
}