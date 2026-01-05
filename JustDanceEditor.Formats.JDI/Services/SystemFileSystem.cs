namespace JustDanceEditor.Formats.JDI.Services;

public class SystemFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public string[] GetDirectories(string path) => Directory.GetDirectories(path);
    public string[] GetFiles(string path, string searchPattern = "*") => Directory.GetFiles(path, searchPattern);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) => File.WriteAllTextAsync(path, contents, cancellationToken);
    public void Copy(string sourcePath, string destinationPath, bool overwrite = false) => File.Copy(sourcePath, destinationPath, overwrite);
    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        if (overwrite && File.Exists(destinationPath))
            File.Delete(destinationPath);
        File.Move(sourcePath, destinationPath);
    }

    public void DeleteFile(string path) => File.Delete(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
    public string Combine(params string[] paths) => Path.Combine(paths);
    public string GetTempPath() => Path.GetTempPath();
    public string GetFullPath(string path) => Path.GetFullPath(path);
}