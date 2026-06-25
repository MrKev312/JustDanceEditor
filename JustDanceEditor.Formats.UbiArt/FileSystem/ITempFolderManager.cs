namespace JustDanceEditor.Formats.UbiArt.FileSystem;

public interface ITempFolderManager
{
    string GetMapFolder(string mapName);
    string GetAudioFolder(string mapName);
    void CreateMapFolder(string mapName);
    void DeleteMapFolder(string mapName);
}