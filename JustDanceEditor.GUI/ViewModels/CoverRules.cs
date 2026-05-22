using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;

namespace JustDanceEditor.GUI.ViewModels;

internal static class CoverRules
{
    private const string WebAssetsFolder = "assets/web";

    public static bool UsesWideCover(ConversionTargetDefinition target) =>
        FormatUsesWideCover(target.FormatName);

    public static bool TargetUsesWideCover(ConversionTargetDefinition target) =>
        IsJdiTarget(target) || UsesWideCover(target);

    public static bool TargetUsesSquareCover(ConversionTargetDefinition target) =>
        IsJdiTarget(target) || !UsesWideCover(target);

    public static bool SourceProvidesWideCover(string? sourceFormatName) =>
        IsJdiSource(sourceFormatName) ||
        (!string.IsNullOrWhiteSpace(sourceFormatName) && FormatUsesWideCover(sourceFormatName));

    public static bool SourceProvidesSquareCover(string? sourceFormatName) =>
        IsJdiSource(sourceFormatName) ||
        (!string.IsNullOrWhiteSpace(sourceFormatName) && !FormatUsesWideCover(sourceFormatName));

    public static string GetCoverVariantRelativePath(CoverVariant variant) =>
        variant == CoverVariant.Square
            ? IntermediatePackageLayout.Assets.SquareCoverFile
            : IntermediatePackageLayout.Assets.CoverFile;

    public static string GetWebAssetRelativePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        const string assetsPrefix = IntermediatePackageLayout.Assets.Root + "/";
        return normalized.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase)
            ? $"{WebAssetsFolder}/{normalized[assetsPrefix.Length..]}"
            : $"{WebAssetsFolder}/{normalized}";
    }

    public static bool SelectedCoverGeneratorsNeedMapBackground(
        ConversionTargetDefinition? target,
        string? sourceFormatName,
        CoverGeneratorKind squareGenerator,
        CoverGeneratorKind wideGenerator)
    {
        if (target is null)
            return false;

        bool squareNeedsComposition =
            squareGenerator == CoverGeneratorKind.FromMapBackground ||
            (squareGenerator == CoverGeneratorKind.Automatic && !SourceProvidesSquareCover(sourceFormatName));

        bool wideNeedsComposition =
            wideGenerator == CoverGeneratorKind.FromMapBackground ||
            (wideGenerator == CoverGeneratorKind.Automatic && !SourceProvidesWideCover(sourceFormatName));

        return (TargetUsesSquareCover(target) && squareNeedsComposition)
            || (TargetUsesWideCover(target) && wideNeedsComposition);
    }

    public static IEnumerable<OnlineAssetDefinition> GetRequiredOnlineAssetsForSelection(
        ConversionTargetDefinition? target,
        CoverGeneratorKind squareGenerator,
        CoverGeneratorKind wideGenerator)
    {
        if (target is null || TargetUsesSquareCover(target))
        {
            foreach (OnlineAssetDefinition asset in GetRequiredOnlineAssetsForGenerator(squareGenerator, CoverVariant.Square))
                yield return asset;
        }

        if (target is not null && TargetUsesWideCover(target))
        {
            foreach (OnlineAssetDefinition asset in GetRequiredOnlineAssetsForGenerator(wideGenerator, CoverVariant.Wide))
                yield return asset;
        }
    }

    private static bool IsJdiTarget(ConversionTargetDefinition target) =>
        target.FormatName.Equals("JDI", StringComparison.OrdinalIgnoreCase) ||
        target.FormatCode.Equals("jdi", StringComparison.OrdinalIgnoreCase) ||
        target.TargetCode.Equals("jdi", StringComparison.OrdinalIgnoreCase);

    private static bool IsJdiSource(string? sourceFormatName) =>
        string.Equals(sourceFormatName, "JDI", StringComparison.OrdinalIgnoreCase);

    private static bool FormatUsesWideCover(string formatName) =>
        formatName.Equals("JDNext PC", StringComparison.OrdinalIgnoreCase) ||
        formatName.Equals("Unity", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<OnlineAssetDefinition> GetRequiredOnlineAssetsForGenerator(
        CoverGeneratorKind generator,
        CoverVariant variant)
    {
        if (generator is CoverGeneratorKind.Automatic or CoverGeneratorKind.Web)
            yield return GetOnlineCoverAssetDefinition(variant);

        if (generator is CoverGeneratorKind.Automatic or CoverGeneratorKind.FromMapBackground)
        {
            yield return GetOnlineAssetDefinition(OnlineAssetKeys.MapBackground);
            yield return GetOnlineAssetDefinition(OnlineAssetKeys.AlbumCoach);
        }
    }

    private static OnlineAssetDefinition GetOnlineCoverAssetDefinition(CoverVariant variant) =>
        variant == CoverVariant.Square
            ? GetOnlineAssetDefinition(OnlineAssetKeys.SquareCover)
            : GetOnlineAssetDefinition(OnlineAssetKeys.Cover);

    private static OnlineAssetDefinition GetOnlineAssetDefinition(string key) =>
        OnlineAssetDownloader.CoverAssetDefinitions.First(asset => asset.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
