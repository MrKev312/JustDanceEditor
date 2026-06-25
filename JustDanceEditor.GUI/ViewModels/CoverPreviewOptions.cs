namespace JustDanceEditor.GUI.ViewModels;

internal sealed record CoverPreviewOptions(
    bool DownloadAssetsWhenLoading,
    CoverGeneratorKind SquareGenerator,
    CoverGeneratorKind WideGenerator,
    CoverAssetSourceKind? MapBackgroundSource,
    CoverAssetSourceKind? AlbumCoachSource);