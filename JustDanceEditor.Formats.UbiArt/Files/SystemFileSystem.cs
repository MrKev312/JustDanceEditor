using JustDanceEditor.Formats.JDI.Services;

namespace JustDanceEditor.Formats.UbiArt.Files;

public class SystemFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public string[] GetFiles(string path, string searchPattern = "*") => Directory.GetFiles(path, searchPattern);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public string Combine(params string[] paths) => Path.Combine(paths);
    public string GetTempPath() => Path.GetTempPath();
    public string GetFullPath(string path) => Path.GetFullPath(path);
}