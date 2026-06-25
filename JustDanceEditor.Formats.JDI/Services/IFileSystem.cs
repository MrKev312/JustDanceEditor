namespace JustDanceEditor.Formats.JDI.Services;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    string[] GetDirectories(string path);
    string[] GetFiles(string path, string searchPattern = "*");
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);
    void Copy(string sourcePath, string destinationPath, bool overwrite = false);
    void Move(string sourcePath, string destinationPath, bool overwrite = false);
    void DeleteFile(string path);
    void CreateDirectory(string path);
    void DeleteDirectory(string path, bool recursive);
    string Combine(params string[] paths);
    string GetTempPath();
    string GetFullPath(string path);
}