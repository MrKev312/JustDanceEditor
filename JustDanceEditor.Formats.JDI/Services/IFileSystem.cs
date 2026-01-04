namespace JustDanceEditor.Formats.JDI.Services;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    string[] GetDirectories(string path);
    string[] GetFiles(string path, string searchPattern = "*");
    string ReadAllText(string path);
    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive);
    string Combine(params string[] paths);
    string GetTempPath();
    string GetFullPath(string path);
}