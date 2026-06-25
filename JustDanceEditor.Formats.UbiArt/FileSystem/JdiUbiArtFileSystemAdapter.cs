using JustDanceEditor.Formats.JDI.Services;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.FileSystem;

internal sealed class JdiUbiArtFileSystemAdapter(IFileSystem inner) : IUbiArtFileSystem
{
    public bool DirectoryExists(string path) => inner.DirectoryExists(path);
    public bool FileExists(string path) => inner.FileExists(path);
    public string[] GetDirectories(string path) => inner.GetDirectories(path);
    public string[] GetFiles(string path, string searchPattern = "*") => inner.GetFiles(path, searchPattern);
    public string ReadAllText(string path) => inner.ReadAllText(path);
    public byte[] ReadAllBytes(string path) => inner.ReadAllBytes(path);
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) => inner.WriteAllTextAsync(path, contents, cancellationToken);
    public void Copy(string sourcePath, string destinationPath, bool overwrite = false) => inner.Copy(sourcePath, destinationPath, overwrite);
    public void Move(string sourcePath, string destinationPath, bool overwrite = false) => inner.Move(sourcePath, destinationPath, overwrite);
    public void DeleteFile(string path) => inner.DeleteFile(path);
    public void CreateDirectory(string path) => inner.CreateDirectory(path);
    public void DeleteDirectory(string path, bool recursive) => inner.DeleteDirectory(path, recursive);
    public string Combine(params string[] paths) => inner.Combine(paths);
    public string GetTempPath() => inner.GetTempPath();
    public string GetFullPath(string path) => inner.GetFullPath(path);
}