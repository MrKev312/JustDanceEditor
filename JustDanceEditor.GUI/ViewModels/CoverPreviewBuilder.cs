using Avalonia.Media.Imaging;

using JustDanceEditor.Conversion.Abstractions;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Services.Images;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.GUI.ViewModels;

internal sealed class CoverPreviewBuilder(ILogger logger)
{
    public async Task<CoverPreviewData> BuildPreviewDataAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition? target,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        if (importResult.MaterializedRoot is null)
            throw new InvalidOperationException("The import did not produce a materialized JDI package.");

        bool useSquareCover = target is null || CoverRules.TargetUsesSquareCover(target);
        bool useWideCover = target is not null && CoverRules.TargetUsesWideCover(target);
        bool useDualCover = useSquareCover && useWideCover;
        IntermediateImageService imageService = CreateImageService(importResult);

        if (useDualCover)
        {
            using Image<Bgra32> squareImage = await GenerateSquareCoverPreviewAsync(imageService, importResult.MaterializedRoot, options, cancellationToken);
            using Image<Bgra32> wideImage = await GenerateWideCoverPreviewAsync(imageService, importResult.MaterializedRoot, options, cancellationToken);
            Bitmap squareBitmap = await ToBitmapAsync(squareImage, cancellationToken);
            Bitmap wideBitmap = await ToBitmapAsync(wideImage, cancellationToken);
            return new CoverPreviewData(squareBitmap, importResult.Package, 512, 512, wideBitmap, 640, 360);
        }

        using Image<Bgra32> image = useWideCover
            ? await GenerateWideCoverPreviewAsync(imageService, importResult.MaterializedRoot, options, cancellationToken)
            : await GenerateSquareCoverPreviewAsync(imageService, importResult.MaterializedRoot, options, cancellationToken);

        Bitmap bitmap = await ToBitmapAsync(image, cancellationToken);
        return new CoverPreviewData(bitmap, importResult.Package, useWideCover ? 640 : 512, useWideCover ? 360 : 512);
    }

    public async Task ApplySelectedCoverGeneratorsAsync(
        JdiImportResult importResult,
        ConversionTargetDefinition target,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        if (importResult.MaterializedRoot is null)
            return;

        bool needsSquareCover = CoverRules.TargetUsesSquareCover(target);
        bool needsWideCover = CoverRules.TargetUsesWideCover(target);
        IntermediateImageService imageService = CreateImageService(importResult);
        Image<Bgra32>? squareCover = null;
        Image<Bgra32>? wideCover = null;

        try
        {
            if (needsSquareCover)
                squareCover = await GenerateSquareCoverPreviewAsync(imageService, importResult.MaterializedRoot, options, cancellationToken);

            if (needsWideCover)
                wideCover = await GenerateWideCoverPreviewAsync(imageService, importResult.MaterializedRoot, options, cancellationToken);

            if (squareCover is not null)
                await SaveCoverVariantAsync(squareCover, importResult.MaterializedRoot, IntermediatePackageLayout.Assets.SquareCoverFile, cancellationToken);

            if (wideCover is not null)
                await SaveCoverVariantAsync(wideCover, importResult.MaterializedRoot, IntermediatePackageLayout.Assets.CoverFile, cancellationToken);
        }
        finally
        {
            squareCover?.Dispose();
            wideCover?.Dispose();
        }
    }

    private IntermediateImageService CreateImageService(JdiImportResult importResult) =>
        new(
            importResult.MaterializedRoot ?? throw new InvalidOperationException("The import did not produce a materialized JDI package."),
            importResult.Package,
            new SystemFileSystem(),
            null,
            logger);

    private static async Task<Image<Bgra32>> GenerateSquareCoverPreviewAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        return options.SquareGenerator switch
        {
            CoverGeneratorKind.Web => await LoadCoverVariantOrPlaceholderAsync(packageRoot, CoverVariant.Square, CoverAssetSourceKind.Web, cancellationToken),
            CoverGeneratorKind.Original => await LoadCoverVariantOrPlaceholderAsync(packageRoot, CoverVariant.Square, CoverAssetSourceKind.Original, cancellationToken),
            CoverGeneratorKind.FromMapBackground => await ComposeSquareFromMapBackgroundAsync(imageService, packageRoot, options, cancellationToken),
            CoverGeneratorKind.FromWideCover => await CreateSquareFromWideCoverAsync(packageRoot, cancellationToken),
            _ => await GenerateAutomaticCoverAsync(imageService, packageRoot, CoverVariant.Square, options, cancellationToken)
        };
    }

    private static async Task<Image<Bgra32>> GenerateWideCoverPreviewAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        return options.WideGenerator switch
        {
            CoverGeneratorKind.Web => await LoadCoverVariantOrPlaceholderAsync(packageRoot, CoverVariant.Wide, CoverAssetSourceKind.Web, cancellationToken),
            CoverGeneratorKind.Original => await LoadCoverVariantOrPlaceholderAsync(packageRoot, CoverVariant.Wide, CoverAssetSourceKind.Original, cancellationToken),
            CoverGeneratorKind.FromMapBackground => await ComposeWideFromMapBackgroundAsync(imageService, packageRoot, options, cancellationToken),
            CoverGeneratorKind.FromSquareCover => await CreateWideFromSquareCoverAsync(packageRoot, cancellationToken),
            _ => await GenerateAutomaticCoverAsync(imageService, packageRoot, CoverVariant.Wide, options, cancellationToken)
        };
    }

    private static async Task<Image<Bgra32>> GenerateAutomaticCoverAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverVariant variant,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        if (options.DownloadAssetsWhenLoading)
        {
            Image<Bgra32>? webCover = await TryLoadCoverVariantAsync(packageRoot, variant, CoverAssetSourceKind.Web, cancellationToken);
            if (webCover is not null)
                return webCover;
        }

        Image<Bgra32>? originalCover = await TryLoadCoverVariantAsync(packageRoot, variant, CoverAssetSourceKind.Original, cancellationToken);
        if (originalCover is not null)
            return originalCover;

        return variant == CoverVariant.Square
            ? await ComposeSquareFromMapBackgroundAsync(imageService, packageRoot, options, cancellationToken)
            : await ComposeWideFromMapBackgroundAsync(imageService, packageRoot, options, cancellationToken);
    }

    private static async Task<Image<Bgra32>> LoadCoverVariantOrPlaceholderAsync(
        string packageRoot,
        CoverVariant variant,
        CoverAssetSourceKind source,
        CancellationToken cancellationToken)
    {
        Image<Bgra32>? image = await TryLoadCoverVariantAsync(packageRoot, variant, source, cancellationToken);
        if (image is not null)
            return image;

        return variant == CoverVariant.Square
            ? CoverComposer.CreatePlaceholder($"{source} Square Cover", 512, 512)
            : CoverComposer.CreatePlaceholder($"{source} Wide Cover", 640, 360);
    }

    private static async Task<Image<Bgra32>?> TryLoadCoverVariantAsync(
        string packageRoot,
        CoverVariant variant,
        CoverAssetSourceKind source,
        CancellationToken cancellationToken)
    {
        string relativePath = CoverRules.GetCoverVariantRelativePath(variant);
        return await TryLoadAssetImageAsync(
            packageRoot,
            GetRelativePathForAssetSource(relativePath, source),
            variant == CoverVariant.Square ? 512 : 640,
            variant == CoverVariant.Square ? 512 : 360,
            cancellationToken);
    }

    private static async Task<Image<Bgra32>> GetMapBackgroundForCompositionAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        CoverAssetSourceKind source = ResolveMapBackgroundSourceKind(packageRoot, options);
        if (source == CoverAssetSourceKind.Web)
        {
            Image<Bgra32>? webBackground = await TryLoadMapBackgroundFromSourceAsync(packageRoot, CoverAssetSourceKind.Web, cancellationToken);
            if (webBackground is not null)
                return webBackground;
        }

        return await imageService.GetMapBackgroundAsync(cancellationToken: cancellationToken);
    }

    private static async Task<Image<Bgra32>?> GetAlbumCoachForCompositionAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        CoverAssetSourceKind source = ResolveAlbumCoachSourceKind(packageRoot, options);
        if (source == CoverAssetSourceKind.Web)
        {
            Image<Bgra32>? webAlbumCoach = await TryLoadAlbumCoachFromSourceAsync(packageRoot, CoverAssetSourceKind.Web, cancellationToken);
            if (webAlbumCoach is not null)
                return webAlbumCoach;
        }

        return await GetAlbumCoachOrNullAsync(imageService, cancellationToken);
    }

    private static CoverAssetSourceKind ResolveMapBackgroundSourceKind(string packageRoot, CoverPreviewOptions options) =>
        options.MapBackgroundSource ??
        (options.DownloadAssetsWhenLoading && File.Exists(IntermediatePackageLayout.Resolve(packageRoot, CoverRules.GetWebAssetRelativePath(IntermediatePackageLayout.Assets.MapBackgroundFile)))
            ? CoverAssetSourceKind.Web
            : CoverAssetSourceKind.Original);

    private static CoverAssetSourceKind ResolveAlbumCoachSourceKind(string packageRoot, CoverPreviewOptions options) =>
        options.AlbumCoachSource ??
        (options.DownloadAssetsWhenLoading && File.Exists(IntermediatePackageLayout.Resolve(packageRoot, CoverRules.GetWebAssetRelativePath(IntermediatePackageLayout.Assets.AlbumCoachFile)))
            ? CoverAssetSourceKind.Web
            : CoverAssetSourceKind.Original);

    private static Task<Image<Bgra32>?> TryLoadMapBackgroundFromSourceAsync(
        string packageRoot,
        CoverAssetSourceKind source,
        CancellationToken cancellationToken) =>
        TryLoadAssetImageAsync(
            packageRoot,
            GetRelativePathForAssetSource(IntermediatePackageLayout.Assets.MapBackgroundFile, source),
            CoverComposer.BackgroundWidth,
            CoverComposer.BackgroundHeight,
            cancellationToken);

    private static Task<Image<Bgra32>?> TryLoadAlbumCoachFromSourceAsync(
        string packageRoot,
        CoverAssetSourceKind source,
        CancellationToken cancellationToken) =>
        TryLoadAssetImageAsync(
            packageRoot,
            GetRelativePathForAssetSource(IntermediatePackageLayout.Assets.AlbumCoachFile, source),
            CoverComposer.AlbumCoachSize,
            CoverComposer.AlbumCoachSize,
            cancellationToken);

    private static async Task<Image<Bgra32>?> TryLoadAssetImageAsync(
        string packageRoot,
        string relativePath,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken)
    {
        string path = IntermediatePackageLayout.Resolve(packageRoot, relativePath);
        if (!File.Exists(path))
            return null;

        try
        {
            Image<Bgra32> image = await Image.LoadAsync<Bgra32>(path, cancellationToken);
            if (image.Width == targetWidth && image.Height == targetHeight)
                return image;

            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight),
                Mode = ResizeMode.Stretch
            }));
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static string GetRelativePathForAssetSource(string relativePath, CoverAssetSourceKind source) =>
        source switch
        {
            CoverAssetSourceKind.Web => CoverRules.GetWebAssetRelativePath(relativePath),
            CoverAssetSourceKind.Original => relativePath,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown cover asset source.")
        };

    private static async Task<Image<Bgra32>> ComposeSquareFromMapBackgroundAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        using Image<Bgra32> background = await GetMapBackgroundForCompositionAsync(imageService, packageRoot, options, cancellationToken);
        using Image<Bgra32>? albumCoach = await GetAlbumCoachForCompositionAsync(imageService, packageRoot, options, cancellationToken);
        return CoverComposer.ComposeSquareCover(background, albumCoach, 512);
    }

    private static async Task<Image<Bgra32>> ComposeWideFromMapBackgroundAsync(
        IntermediateImageService imageService,
        string packageRoot,
        CoverPreviewOptions options,
        CancellationToken cancellationToken)
    {
        using Image<Bgra32> background = await GetMapBackgroundForCompositionAsync(imageService, packageRoot, options, cancellationToken);
        using Image<Bgra32>? albumCoach = await GetAlbumCoachForCompositionAsync(imageService, packageRoot, options, cancellationToken);
        return CoverComposer.ComposeCover(background, albumCoach, 640, 360);
    }

    private static async Task<Image<Bgra32>> CreateSquareFromWideCoverAsync(
        string packageRoot,
        CancellationToken cancellationToken)
    {
        using Image<Bgra32> wideCover = await LoadCoverVariantOrPlaceholderAsync(packageRoot, CoverVariant.Wide, CoverAssetSourceKind.Original, cancellationToken);
        return CoverComposer.CreateSquareCover(wideCover, 512);
    }

    private static async Task<Image<Bgra32>> CreateWideFromSquareCoverAsync(
        string packageRoot,
        CancellationToken cancellationToken)
    {
        using Image<Bgra32> squareCover = await LoadCoverVariantOrPlaceholderAsync(packageRoot, CoverVariant.Square, CoverAssetSourceKind.Original, cancellationToken);
        return CreateWideFromSquareCover(squareCover);
    }

    private static Image<Bgra32> CreateWideFromSquareCover(Image<Bgra32> squareCover)
    {
        Image<Bgra32> wideCover = squareCover.Clone();
        wideCover.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(640, 360),
            Mode = ResizeMode.Stretch
        }));
        return wideCover;
    }

    private static async Task<Image<Bgra32>?> GetAlbumCoachOrNullAsync(
        IntermediateImageService imageService,
        CancellationToken cancellationToken)
    {
        Image<Bgra32> albumCoach = await imageService.GetAlbumCoachAsync(cancellationToken: cancellationToken);
        if (!IsPlaceholder(albumCoach))
            return albumCoach;

        albumCoach.Dispose();
        return null;
    }

    private static async Task SaveCoverVariantAsync(
        Image<Bgra32> image,
        string packageRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        string destination = IntermediatePackageLayout.Resolve(packageRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException($"Could not resolve output folder for '{destination}'."));
        await WebpImageFormatProvider.Lossless.SaveAsync(image, destination, cancellationToken);
    }

    private static bool IsPlaceholder(Image<Bgra32> image)
    {
        Bgra32 pixel = image[0, 0];
        return pixel.R == 255 && pixel.G == 0 && pixel.B == 255;
    }

    private static async Task<Bitmap> ToBitmapAsync(Image<Bgra32> image, CancellationToken cancellationToken)
    {
        await using MemoryStream stream = new();
        await image.SaveAsync(stream, new PngEncoder(), cancellationToken);
        byte[] bytes = stream.ToArray();
        return new Bitmap(new MemoryStream(bytes));
    }
}

internal sealed record CoverPreviewData(
    Bitmap Bitmap,
    IntermediateSongPackage Package,
    int Width,
    int Height,
    Bitmap? SecondaryBitmap = null,
    int SecondaryWidth = 640,
    int SecondaryHeight = 360);
