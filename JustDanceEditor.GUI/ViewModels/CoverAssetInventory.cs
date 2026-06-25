using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.GUI.ViewModels;

internal sealed class CoverAssetInventory(string? packageRoot, bool downloadAssetsWhenLoading)
{
    public bool HasOriginalCoverVariant(CoverVariant variant) =>
        HasOriginalAsset(CoverRules.GetCoverVariantRelativePath(variant));

    public bool HasWebCoverVariant(CoverVariant variant) =>
        HasWebAsset(CoverRules.GetCoverVariantRelativePath(variant));

    public bool HasAnyMapBackgroundSource() =>
        HasWebMapBackground() || HasOriginalMapBackgroundSource();

    public bool HasOriginalMapBackgroundSource() =>
        HasOriginalAsset(IntermediatePackageLayout.Assets.MapBackgroundFile);

    public bool HasWebMapBackground() =>
        HasWebAsset(IntermediatePackageLayout.Assets.MapBackgroundFile);

    public bool HasWebAlbumCoach() =>
        HasWebAsset(IntermediatePackageLayout.Assets.AlbumCoachFile);

    private bool HasOriginalAsset(string relativePath)
    {
        if (packageRoot is null)
            return false;

        return File.Exists(IntermediatePackageLayout.Resolve(packageRoot, relativePath));
    }

    private bool HasWebAsset(string relativePath)
    {
        if (!downloadAssetsWhenLoading || packageRoot is null)
            return false;

        return File.Exists(IntermediatePackageLayout.Resolve(packageRoot, CoverRules.GetWebAssetRelativePath(relativePath)));
    }
}