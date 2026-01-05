using System.IO;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class SystemTempFolderManager : ITempFolderManager
{
    private const string Root = "JustDanceEditor";

    public string GetMapFolder(string mapName)
    {
        return Path.Combine(Path.GetTempPath(), Root, mapName ?? string.Empty);
    }

    public string GetAudioFolder(string mapName)
    {
        return Path.Combine(GetMapFolder(mapName), "audio");
    }

    public void CreateMapFolder(string mapName)
    {
        Directory.CreateDirectory(GetMapFolder(mapName));
        Directory.CreateDirectory(GetAudioFolder(mapName));
    }

    public void DeleteMapFolder(string mapName)
    {
        try
        {
            var folder = GetMapFolder(mapName);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch (IOException)
        {
            // swallow - best effort deletion
        }
    }
}