namespace JustDanceEditor.Formats.JDI.Preview;

public sealed record SongPreviewRequest(
    string InputPath,
    string WorkingRoot,
    string? SongName = null,
    bool DownloadOnlineAssets = false);

public sealed record SongPreviewResult(
    IntermediateSongPackage Package,
    string FormatName,
    string MaterializedRoot,
    bool MaterializedRootIsTemporary,
    Task? AssetWarmupTask = null);

public interface ISongPreviewProvider
{
    string FormatName { get; }
    int Priority => 100;
    bool CanPreview(string path);
    Task<SongPreviewResult> LoadPreviewAsync(SongPreviewRequest request, CancellationToken cancellationToken = default);
}