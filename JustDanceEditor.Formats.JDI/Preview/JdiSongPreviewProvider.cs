using JustDanceEditor.Formats.JDI.Serialization;

namespace JustDanceEditor.Formats.JDI.Preview;

public sealed class JdiSongPreviewProvider : ISongPreviewProvider
{
    public string FormatName => "JDI";
    public int Priority => 10;

    public bool CanPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, IntermediatePackageLayout.MetadataFile));
    }

    public Task<SongPreviewResult> LoadPreviewAsync(SongPreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string sourcePath = request.InputPath;
        bool isTemporary = false;
        if (request.DownloadOnlineAssets)
        {
            sourcePath = Path.Combine(request.WorkingRoot, "source");
            CopyDirectory(request.InputPath, sourcePath);
            isTemporary = true;
        }

        IntermediateSongPackage package = IntermediatePackageSerializer.LoadFromFolder(sourcePath);
        return Task.FromResult(new SongPreviewResult(
            package,
            FormatName,
            sourcePath,
            isTemporary));
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (string directory in Directory.GetDirectories(sourceDir))
            CopyDirectory(directory, Path.Combine(destDir, Path.GetFileName(directory)));
    }
}