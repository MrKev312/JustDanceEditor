using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.FileSystem;

public class TempFolders(JustDanceUbiArtFileSystem fileSystem, ILogger<JustDanceUbiArtFileSystem> logger, ITempFolderManager tempManager)
{
    private readonly JustDanceUbiArtFileSystem fileSystem = fileSystem;
    private readonly ILogger<JustDanceUbiArtFileSystem> _logger = logger;
    private readonly ITempFolderManager _tempManager = tempManager;

    public string MapFolder => _tempManager.GetMapFolder(GetScopedMapName(fileSystem.SongName));
    public string AudioFolder => _tempManager.GetAudioFolder(GetScopedMapName(fileSystem.SongName));

    public void CreateTempFolders()
    {
        try
        {
            _logger.LogDebug("Creating temp folders via ITempFolderManager");
            _tempManager.CreateMapFolder(GetScopedMapName(fileSystem.SongName));
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
            _tempManager.DeleteMapFolder(GetScopedMapName(mapName));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp folder for '{MapName}': {Message}", mapName, ex.Message);
        }
    }

    private string GetScopedMapName(string mapName)
    {
        string name = string.IsNullOrWhiteSpace(mapName) ? "map" : mapName;
        Span<char> buffer = stackalloc char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];
            buffer[i] = char.IsLetterOrDigit(current) || current is '-' or '_' or '.'
                ? current
                : '_';
        }

        return $"{new string(buffer)}_{fileSystem.ConversionRequest.TempSessionId[..12]}";
    }
}