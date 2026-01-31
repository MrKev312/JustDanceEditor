namespace JustDanceEditor.Formats.UbiArt.FileSystem;

public class SystemTempFolderManager(JDI.Services.IFileSystem? io = null) : ITempFolderManager
{
    private const string Root = "JustDanceEditor";
    private readonly JDI.Services.IFileSystem _io = io ?? new JDI.Services.SystemFileSystem();

    public string GetMapFolder(string mapName)
    {
        return _io.Combine(_io.GetTempPath(), Root, mapName ?? string.Empty);
    }

    public string GetAudioFolder(string mapName)
    {
        return _io.Combine(GetMapFolder(mapName), "audio");
    }

    public void CreateMapFolder(string mapName)
    {
        _io.CreateDirectory(GetMapFolder(mapName));
        _io.CreateDirectory(GetAudioFolder(mapName));
    }

    public void DeleteMapFolder(string mapName)
    {
        try
        {
            string folder = GetMapFolder(mapName);
            if (_io.DirectoryExists(folder))
                _io.DeleteDirectory(folder, true);
        }
        catch (IOException)
        {
            // swallow - best effort deletion
        }
    }
}