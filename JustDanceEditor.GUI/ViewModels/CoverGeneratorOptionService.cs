using JustDanceEditor.Conversion.Abstractions;

namespace JustDanceEditor.GUI.ViewModels;

internal static class CoverGeneratorOptionService
{
    public static CoverGeneratorOptionResult Build(CoverGeneratorOptionRequest request)
    {
        bool showSquare = request.Target is not null && CoverRules.TargetUsesSquareCover(request.Target);
        bool showWide = request.Target is not null && CoverRules.TargetUsesWideCover(request.Target);
        bool sourceProvidesSquare = CoverRules.SourceProvidesSquareCover(request.SourceFormatName);
        bool sourceProvidesWide = CoverRules.SourceProvidesWideCover(request.SourceFormatName);
        bool sourceProvidesExactlyOneCover = sourceProvidesSquare != sourceProvidesWide;
        bool squareMissingFromSource = sourceProvidesExactlyOneCover && !sourceProvidesSquare;
        bool wideMissingFromSource = sourceProvidesExactlyOneCover && !sourceProvidesWide;

        List<CoverGeneratorItemViewModel> squareGenerators = [];
        List<CoverGeneratorItemViewModel> wideGenerators = [];

        if (showSquare)
        {
            AddCoverGeneratorOptions(
                squareGenerators,
                CoverVariant.Square,
                request.Inventory,
                addStretchOption: squareMissingFromSource && request.Inventory.HasOriginalCoverVariant(CoverVariant.Wide),
                stretchKind: CoverGeneratorKind.FromWideCover,
                stretchLabel: "Original wide stretched");
        }

        if (showWide)
        {
            AddCoverGeneratorOptions(
                wideGenerators,
                CoverVariant.Wide,
                request.Inventory,
                addStretchOption: wideMissingFromSource && request.Inventory.HasOriginalCoverVariant(CoverVariant.Square),
                stretchKind: CoverGeneratorKind.FromSquareCover,
                stretchLabel: "Original square stretched");
        }

        List<CoverAssetSourceItemViewModel> mapBackgroundSources = BuildAssetSourceOptions(
            request.Inventory.HasWebMapBackground(),
            request.Inventory.HasOriginalMapBackgroundSource());
        List<CoverAssetSourceItemViewModel> albumCoachSources = BuildAssetSourceOptions(
            request.Inventory.HasWebAlbumCoach(),
            hasOriginal: true);

        return new CoverGeneratorOptionResult(
            SquareGenerators: squareGenerators,
            WideGenerators: wideGenerators,
            SelectedSquareGenerator: SelectCoverGenerator(squareGenerators, request.PreviousSquareGenerator),
            SelectedWideGenerator: SelectCoverGenerator(wideGenerators, request.PreviousWideGenerator),
            MapBackgroundSources: mapBackgroundSources,
            AlbumCoachSources: albumCoachSources,
            SelectedMapBackgroundSource: SelectAssetSource(
                mapBackgroundSources,
                request.PreserveMapBackgroundSource ? request.PreviousMapBackgroundSource : null),
            SelectedAlbumCoachSource: SelectAssetSource(
                albumCoachSources,
                request.PreserveAlbumCoachSource ? request.PreviousAlbumCoachSource : null),
            IsSquareCoverGeneratorVisible: showSquare,
            IsWideCoverGeneratorVisible: showWide,
            AreCoverGeneratorOptionsVisible: showSquare || showWide);
    }

    private static void AddCoverGeneratorOptions(
        ICollection<CoverGeneratorItemViewModel> target,
        CoverVariant variant,
        CoverAssetInventory inventory,
        bool addStretchOption,
        CoverGeneratorKind stretchKind,
        string stretchLabel)
    {
        target.Add(new CoverGeneratorItemViewModel(CoverGeneratorKind.Automatic, "Automatic"));

        if (inventory.HasWebCoverVariant(variant))
            target.Add(new CoverGeneratorItemViewModel(CoverGeneratorKind.Web, "Web"));

        if (inventory.HasOriginalCoverVariant(variant))
            target.Add(new CoverGeneratorItemViewModel(CoverGeneratorKind.Original, "Original"));

        if (inventory.HasAnyMapBackgroundSource())
            target.Add(new CoverGeneratorItemViewModel(CoverGeneratorKind.FromMapBackground, "Album coach on map background"));

        if (addStretchOption)
            target.Add(new CoverGeneratorItemViewModel(stretchKind, stretchLabel));
    }

    private static List<CoverAssetSourceItemViewModel> BuildAssetSourceOptions(bool hasWeb, bool hasOriginal)
    {
        List<CoverAssetSourceItemViewModel> result = [];

        if (hasWeb)
            result.Add(new CoverAssetSourceItemViewModel(CoverAssetSourceKind.Web, "Web"));

        if (hasOriginal)
            result.Add(new CoverAssetSourceItemViewModel(CoverAssetSourceKind.Original, "Original"));

        return result;
    }

    private static CoverGeneratorItemViewModel? SelectCoverGenerator(
        IReadOnlyList<CoverGeneratorItemViewModel> generators,
        CoverGeneratorKind? previousKind)
    {
        return previousKind is null
            ? generators.FirstOrDefault()
            : generators.FirstOrDefault(generator => generator.Kind == previousKind) ?? generators.FirstOrDefault();
    }

    private static CoverAssetSourceItemViewModel? SelectAssetSource(
        IReadOnlyList<CoverAssetSourceItemViewModel> sources,
        CoverAssetSourceKind? previousKind)
    {
        return previousKind is null
            ? sources.FirstOrDefault()
            : sources.FirstOrDefault(source => source.Kind == previousKind) ?? sources.FirstOrDefault();
    }
}

internal sealed record CoverGeneratorOptionRequest(
    ConversionTargetDefinition? Target,
    string? SourceFormatName,
    CoverAssetInventory Inventory,
    CoverGeneratorKind? PreviousSquareGenerator,
    CoverGeneratorKind? PreviousWideGenerator,
    CoverAssetSourceKind? PreviousMapBackgroundSource,
    CoverAssetSourceKind? PreviousAlbumCoachSource,
    bool PreserveMapBackgroundSource,
    bool PreserveAlbumCoachSource);

internal sealed record CoverGeneratorOptionResult(
    IReadOnlyList<CoverGeneratorItemViewModel> SquareGenerators,
    IReadOnlyList<CoverGeneratorItemViewModel> WideGenerators,
    CoverGeneratorItemViewModel? SelectedSquareGenerator,
    CoverGeneratorItemViewModel? SelectedWideGenerator,
    IReadOnlyList<CoverAssetSourceItemViewModel> MapBackgroundSources,
    IReadOnlyList<CoverAssetSourceItemViewModel> AlbumCoachSources,
    CoverAssetSourceItemViewModel? SelectedMapBackgroundSource,
    CoverAssetSourceItemViewModel? SelectedAlbumCoachSource,
    bool IsSquareCoverGeneratorVisible,
    bool IsWideCoverGeneratorVisible,
    bool AreCoverGeneratorOptionsVisible);
