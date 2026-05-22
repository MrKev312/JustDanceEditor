using JustDanceEditor.Formats.JDI.Services.Images;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.JDI.Services;

/// <summary>
/// Implementation of <see cref="IIntermediateImageService"/> that works with
/// a package root folder on the file system.
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="IntermediateImageService"/>.
/// </remarks>
/// <param name="packageRoot">Root folder path of the intermediate package.</param>
/// <param name="package">The song package metadata.</param>
/// <param name="fileSystem">File system abstraction.</param>
/// <param name="imageFormat">Image format provider for saving images.</param>
/// <param name="logger">Optional logger.</param>
public sealed class IntermediateImageService(
    string packageRoot,
    IntermediateSongPackage package,
    IFileSystem fileSystem,
    IImageFormatProvider? imageFormat = null,
    ILogger? logger = null) : IIntermediateImageService
{
    // Default resolutions for each asset type
    private const int CoverWidth = 640;
    private const int CoverHeight = 360;
    private const int SquareCoverSize = 512;
    private const int AlbumCoachSize = 1024;
    private const int CoachSize = 1024;
    private const int BackgroundWidth = 2048;
    private const int BackgroundHeight = 1024;
    private const int BannerWidth = 1024;
    private const int BannerHeight = 512;
    private const int AlbumBackgroundSize = 256;
    private const int PictogramSize = 512;
    private const int SongTitleWidth = 512;
    private const int SongTitleHeight = 256;

    private readonly string _packageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
    private readonly IntermediateSongPackage _package = package ?? throw new ArgumentNullException(nameof(package));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IImageFormatProvider _imageFormat = imageFormat ?? WebpImageFormatProvider.Lossless;

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetCoverAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetWidth = width ?? CoverWidth;
        int targetHeight = height ?? CoverHeight;
        string coverPath = ResolvePath(IntermediatePackageLayout.Assets.CoverFile);

        if (!_fileSystem.FileExists(coverPath))
        {
            logger?.LogDebug("Cover image not found at {Path}, attempting to generate", coverPath);
            Image<Bgra32>? generated = await GenerateCoverAsync(cancellationToken);
            if (generated != null)
            {
                await SaveImageAsync(generated, coverPath, cancellationToken);
                return ScaleImage(generated, width, height);
            }

            return CoverComposer.CreatePlaceholder("Cover", targetWidth, targetHeight);
        }

        return await LoadAndScaleImageAsync(coverPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder("Cover", targetWidth, targetHeight);
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetSquareCoverAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetSize = width ?? height ?? SquareCoverSize;
        string squareCoverPath = ResolvePath(IntermediatePackageLayout.Assets.SquareCoverFile);

        // If square cover doesn't exist, generate it from regular cover
        if (!_fileSystem.FileExists(squareCoverPath))
        {
            logger?.LogDebug("Square cover not found, attempting to generate from cover");
            Image<Bgra32> cover = await GetCoverAsync(cancellationToken: cancellationToken);

            // If cover is a placeholder, return a square placeholder
            if (IsPlaceholder(cover))
            {
                cover.Dispose();
                return CoverComposer.CreatePlaceholder("Square Cover", targetSize, targetSize);
            }

            using (cover)
            {
                // Create square version by squashing the cover
                int squareSize = Math.Min(cover.Width, cover.Height);
                Image<Bgra32> squareCover = cover.Clone();
                squareCover.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(squareSize, squareSize),
                    Mode = ResizeMode.Stretch
                }));

                // Save the full-resolution square cover
                await SaveImageAsync(squareCover, squareCoverPath, cancellationToken);

                // Return scaled version if requested
                return ScaleImage(squareCover, width, height);
            }
        }

        return await LoadAndScaleImageAsync(squareCoverPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder("Square Cover", targetSize, targetSize);
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetAlbumCoachAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetSize = width ?? height ?? AlbumCoachSize;
        string albumCoachPath = ResolvePath(IntermediatePackageLayout.Assets.AlbumCoachFile);

        if (!_fileSystem.FileExists(albumCoachPath))
        {
            logger?.LogDebug("Album coach image not found at {Path}, attempting to generate", albumCoachPath);
            // Try to generate from individual coaches
            Image<Bgra32>? generated = await GenerateAlbumCoachAsync(cancellationToken);
            if (generated != null)
            {
                await SaveImageAsync(generated, albumCoachPath, cancellationToken);
                return ScaleImage(generated, width, height);
            }

            return CoverComposer.CreatePlaceholder("Album Coach", targetSize, targetSize);
        }

        return await LoadAndScaleImageAsync(albumCoachPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder("Album Coach", targetSize, targetSize);
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetCoachAsync(int coachIndex, int? width = null, int? height = null, bool useFadeEffect = false, CancellationToken cancellationToken = default)
    {
        if (coachIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(coachIndex), "Coach index must be 1-based.");

        int targetSize = width ?? height ?? CoachSize;
        string coachPath = ResolvePath(IntermediatePackageLayout.Assets.CoachFile(coachIndex));

        if (!_fileSystem.FileExists(coachPath))
        {
            logger?.LogDebug("Coach {Index} image not found at {Path}, returning placeholder", coachIndex, coachPath);
            return CoverComposer.CreatePlaceholder($"Coach {coachIndex}", targetSize, targetSize);
        }

        Image<Bgra32> image = await LoadAndScaleImageAsync(coachPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder($"Coach {coachIndex}", targetSize, targetSize);

        if (useFadeEffect)
        {
            ApplyCoachFadeEffect(image);
        }

        return image;
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetMapBackgroundAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetWidth = width ?? BackgroundWidth;
        int targetHeight = height ?? BackgroundHeight;
        string bgPath = ResolvePath(IntermediatePackageLayout.Assets.MapBackgroundFile);

        if (!_fileSystem.FileExists(bgPath))
        {
            logger?.LogDebug("Map background not found, attempting to generate from coaches background");
            Image<Bgra32>? generated = await GenerateMapBackgroundAsync(cancellationToken);
            if (generated != null)
            {
                await SaveImageAsync(generated, bgPath, cancellationToken);
                return ScaleImage(generated, width, height);
            }

            logger?.LogInformation("Map background could not be generated from existing assets; creating missing-background fallback");
            Image<Bgra32> fallback = CoverComposer.CreateMissingBackground();
            await SaveImageAsync(fallback, bgPath, cancellationToken);
            return ScaleImage(fallback, width, height);
        }

        return await LoadAndScaleImageAsync(bgPath, width, height, cancellationToken)
            ?? CoverComposer.CreateMissingBackground(targetWidth, targetHeight);
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetBannerAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetWidth = width ?? BannerWidth;
        int targetHeight = height ?? BannerHeight;
        string bannerPath = ResolvePath(IntermediatePackageLayout.Assets.BannerFile);

        if (!_fileSystem.FileExists(bannerPath))
        {
            logger?.LogDebug("Banner not found, attempting to generate from coaches background");
            Image<Bgra32>? generated = await GenerateBannerAsync(cancellationToken);
            if (generated != null)
            {
                await SaveImageAsync(generated, bannerPath, cancellationToken);
                return ScaleImage(generated, width, height);
            }

            return CoverComposer.CreatePlaceholder("Banner", targetWidth, targetHeight);
        }

        return await LoadAndScaleImageAsync(bannerPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder("Banner", targetWidth, targetHeight);
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetAlbumBackgroundAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetSize = width ?? height ?? AlbumBackgroundSize;
        string albumBgPath = ResolvePath(IntermediatePackageLayout.Assets.AlbumBackgroundFile);

        if (!_fileSystem.FileExists(albumBgPath))
        {
            logger?.LogDebug("Album background not found, attempting to generate from coaches background");
            Image<Bgra32>? generated = await GenerateAlbumBackgroundAsync(cancellationToken);
            if (generated != null)
            {
                await SaveImageAsync(generated, albumBgPath, cancellationToken);
                return ScaleImage(generated, width, height);
            }

            return CoverComposer.CreatePlaceholder("Album Background", targetSize, targetSize);
        }

        return await LoadAndScaleImageAsync(albumBgPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder("Album Background", targetSize, targetSize);
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetPictogramAsync(string pictogramId, int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pictogramId);

        int targetSize = width ?? height ?? PictogramSize;
        string pictoPath = ResolvePath(IntermediatePackageLayout.Assets.PictogramFile(pictogramId));

        if (!_fileSystem.FileExists(pictoPath))
        {
            logger?.LogDebug("Pictogram {Id} not found at {Path}, returning placeholder", pictogramId, pictoPath);
            return CoverComposer.CreatePlaceholder($"Pictogram {pictogramId}", targetSize, targetSize);
        }

        return await LoadAndScaleImageAsync(pictoPath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder($"Pictogram {pictogramId}", targetSize, targetSize);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetPictogramIds()
    {
        string pictoFolder = ResolvePath(IntermediatePackageLayout.Assets.PictogramsFolder);

        if (!_fileSystem.DirectoryExists(pictoFolder))
            return [];

        return _fileSystem.GetFiles(pictoFolder, $"*.{_imageFormat.FileExtension}")
            .Select(path => Path.GetFileNameWithoutExtension(path));
    }

    /// <inheritdoc />
    public async Task<Image<Bgra32>> GetSongTitleLogoAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default)
    {
        int targetWidth = width ?? SongTitleWidth;
        int targetHeight = height ?? SongTitleHeight;
        string titlePath = ResolvePath(IntermediatePackageLayout.Assets.SongTitleFile);

        if (!_fileSystem.FileExists(titlePath))
        {
            logger?.LogDebug("Song title logo not found at {Path}, returning placeholder", titlePath);
            return CoverComposer.CreatePlaceholder("Song Title", targetWidth, targetHeight);
        }

        return await LoadAndScaleImageAsync(titlePath, width, height, cancellationToken)
            ?? CoverComposer.CreatePlaceholder("Song Title", targetWidth, targetHeight);
    }

    /// <inheritdoc />
    public async Task GenerateAllMissingImagesAsync(CancellationToken cancellationToken = default)
    {
        logger?.LogInformation("Generating all missing images for package at {Root}", _packageRoot);

        // Generate square cover if missing
        if (!HasImage(ImageAssetType.SquareCover))
        {
            logger?.LogDebug("Generating square cover");
            using Image<Bgra32> squareCover = await GetSquareCoverAsync(cancellationToken: cancellationToken);
            // GetSquareCoverAsync already saves it
        }

        // Generate wide cover if missing
        if (!HasImage(ImageAssetType.Cover))
        {
            logger?.LogDebug("Generating cover");
            using Image<Bgra32> cover = await GetCoverAsync(cancellationToken: cancellationToken);
            // GetCoverAsync already saves it
        }

        // Generate album coach composite if missing
        if (!HasImage(ImageAssetType.AlbumCoach))
        {
            logger?.LogDebug("Generating album coach composite");
            using Image<Bgra32> albumCoach = await GetAlbumCoachAsync(cancellationToken: cancellationToken);
            // GetAlbumCoachAsync already saves it if generated
        }

        // Generate map background if missing
        if (!HasImage(ImageAssetType.MapBackground))
        {
            logger?.LogDebug("Generating map background");
            using Image<Bgra32> mapBg = await GetMapBackgroundAsync(cancellationToken: cancellationToken);
            // GetMapBackgroundAsync already saves it if generated
        }

        // Generate banner if missing (from map background)
        if (!HasImage(ImageAssetType.Banner) && HasImage(ImageAssetType.MapBackground))
        {
            logger?.LogDebug("Generating banner");
            using Image<Bgra32> banner = await GetBannerAsync(cancellationToken: cancellationToken);
            // GetBannerAsync already saves it if generated
        }

        // Generate album background if missing (from map background)
        if (!HasImage(ImageAssetType.AlbumBackground) && HasImage(ImageAssetType.MapBackground))
        {
            logger?.LogDebug("Generating album background");
            using Image<Bgra32> albumBg = await GetAlbumBackgroundAsync(cancellationToken: cancellationToken);
            // GetAlbumBackgroundAsync already saves it if generated
        }

        logger?.LogInformation("Finished generating missing images");
    }

    /// <inheritdoc />
    public bool HasImage(ImageAssetType assetType)
    {
        string path = assetType switch
        {
            ImageAssetType.Cover => ResolvePath(IntermediatePackageLayout.Assets.CoverFile),
            ImageAssetType.SquareCover => ResolvePath(IntermediatePackageLayout.Assets.SquareCoverFile),
            ImageAssetType.AlbumCoach => ResolvePath(IntermediatePackageLayout.Assets.AlbumCoachFile),
            ImageAssetType.AlbumBackground => ResolvePath(IntermediatePackageLayout.Assets.AlbumBackgroundFile),
            ImageAssetType.MapBackground => ResolvePath(IntermediatePackageLayout.Assets.MapBackgroundFile),
            ImageAssetType.Banner => ResolvePath(IntermediatePackageLayout.Assets.BannerFile),
            ImageAssetType.SongTitleLogo => ResolvePath(IntermediatePackageLayout.Assets.SongTitleFile),
            _ => throw new ArgumentOutOfRangeException(nameof(assetType))
        };

        return _fileSystem.FileExists(path);
    }

    /// <summary>
    /// Stores an image at the specified asset path within the package.
    /// Used by format importers to populate the package with images.
    /// </summary>
    /// <param name="image">The image to store.</param>
    /// <param name="assetType">The type of asset being stored.</param>
    /// <param name="coachIndexOrPictogramId">For coach images, the 1-based index. For pictograms, the ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StoreImageAsync(
        Image<Bgra32> image,
        ImageAssetType assetType,
        object? coachIndexOrPictogramId = null,
        CancellationToken cancellationToken = default)
    {
        string relativePath = assetType switch
        {
            ImageAssetType.Cover => IntermediatePackageLayout.Assets.CoverFile,
            ImageAssetType.SquareCover => IntermediatePackageLayout.Assets.SquareCoverFile,
            ImageAssetType.AlbumCoach => IntermediatePackageLayout.Assets.AlbumCoachFile,
            ImageAssetType.AlbumBackground => IntermediatePackageLayout.Assets.AlbumBackgroundFile,
            ImageAssetType.MapBackground => IntermediatePackageLayout.Assets.MapBackgroundFile,
            ImageAssetType.Banner => IntermediatePackageLayout.Assets.BannerFile,
            ImageAssetType.SongTitleLogo => IntermediatePackageLayout.Assets.SongTitleFile,
            ImageAssetType.Coach when coachIndexOrPictogramId is int index =>
                IntermediatePackageLayout.Assets.CoachFile(index),
            ImageAssetType.Pictogram when coachIndexOrPictogramId is string id =>
                IntermediatePackageLayout.Assets.PictogramFile(id),
            _ => throw new ArgumentException("Invalid asset type or missing required parameter", nameof(assetType))
        };

        string fullPath = ResolvePath(relativePath);
        await SaveImageAsync(image, fullPath, cancellationToken);
    }

    private string ResolvePath(string relativePath)
    {
        return IntermediatePackageLayout.Resolve(_packageRoot, relativePath);
    }

    private async Task<Image<Bgra32>?> LoadAndScaleImageAsync(string path, int? width, int? height, CancellationToken cancellationToken)
    {
        try
        {
            Image<Bgra32> image = await Image.LoadAsync<Bgra32>(path, cancellationToken);
            return ScaleImage(image, width, height);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load image from {Path}", path);
            return null;
        }
    }

    private static Image<Bgra32> ScaleImage(Image<Bgra32> image, int? width, int? height)
    {
        if (width == null && height == null)
            return image;

        int targetWidth = width ?? image.Width;
        int targetHeight = height ?? image.Height;

        if (image.Width == targetWidth && image.Height == targetHeight)
            return image;

        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = ResizeMode.Stretch
        }));

        return image;
    }

    private async Task SaveImageAsync(Image<Bgra32> image, string path, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
            _fileSystem.CreateDirectory(directory);

        await _imageFormat.SaveAsync(image, path, cancellationToken);
        logger?.LogDebug("Saved image to {Path}", path);
    }

    private async Task<Image<Bgra32>?> GenerateCoverAsync(CancellationToken cancellationToken)
    {
        // Need a map background to compose a cover
        Image<Bgra32> background = await GetMapBackgroundAsync(cancellationToken: cancellationToken);
        if (IsPlaceholder(background))
        {
            background.Dispose();
            logger?.LogDebug("Cannot generate cover: no map background available");
            return null;
        }

        // Album coach is optional for cover composition
        Image<Bgra32>? albumCoach = null;
        try
        {
            Image<Bgra32> coach = await GetAlbumCoachAsync(cancellationToken: cancellationToken);
            if (!IsPlaceholder(coach))
                albumCoach = coach;
            else
                coach.Dispose();
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not load album coach for cover composition, proceeding without it");
        }

        try
        {
            using (background)
            using (albumCoach)
            {
                return CoverComposer.ComposeCover(background, albumCoach);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to generate cover from map background and album coach");
            return null;
        }
    }

    private async Task<Image<Bgra32>?> GenerateAlbumCoachAsync(CancellationToken cancellationToken)
    {
        int coachCount = _package.Metadata.CoachCount;
        if (coachCount <= 0)
        {
            logger?.LogWarning("Cannot generate album coach: CoachCount is {Count}", coachCount);
            return null;
        }

        // Check if any real coach images exist (not placeholders)
        List<Image<Bgra32>> coachImages = [];
        try
        {
            for (int i = 1; i <= coachCount; i++)
            {
                string coachPath = ResolvePath(IntermediatePackageLayout.Assets.CoachFile(i));
                if (!_fileSystem.FileExists(coachPath))
                    continue;

                Image<Bgra32> coach = await GetCoachAsync(i, cancellationToken: cancellationToken);
                if (!IsPlaceholder(coach))
                    coachImages.Add(coach);
                else
                    coach.Dispose();
            }

            if (coachImages.Count == 0)
            {
                logger?.LogWarning("Cannot generate album coach: No individual coach images found");
                return null;
            }

            // Use sophisticated collision-aware composition
            return CoverComposer.ComposeAlbumCoach(coachImages);
        }
        finally
        {
            foreach (Image<Bgra32> coach in coachImages)
                coach.Dispose();
        }
    }

    private async Task<Image<Bgra32>?> GenerateMapBackgroundAsync(CancellationToken cancellationToken)
    {
        // Check if we have a banner to generate from
        string bannerPath = ResolvePath(IntermediatePackageLayout.Assets.BannerFile);
        if (!_fileSystem.FileExists(bannerPath))
        {
            logger?.LogDebug("Cannot generate map background: no banner available");
            return null;
        }

        try
        {
            // Load banner directly (not through getter to avoid potential infinite recursion)
            Image<Bgra32>? banner = await LoadAndScaleImageAsync(bannerPath, width: null, height: null, cancellationToken);
            if (banner == null || IsPlaceholder(banner))
            {
                banner?.Dispose();
                logger?.LogDebug("Banner is placeholder or failed to load, cannot generate map background");
                return null;
            }

            using (banner)
            {
                // Get song colors from metadata (with fallback to white)
                string songColor1A = _package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_1a", "#FFFFFFFF");
                string songColor1B = _package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_1b", "#FFFFFFFF");
                string songColor2A = _package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_2a", "#FFFFFFFF");
                string songColor2B = _package.Metadata.AdditionalMetadata.GetValueOrDefault("songcolor_2b", "#FFFFFFFF");

                // Determine color mode based on JD version
                // Pre-JD2019 uses Main mode (1a/1b only), JD2019+ uses Gradient mode
                BannerColorMode colorMode = BannerColorMode.Main;
                if (_package.Metadata.OriginalJDVersion >= 2019)
                {
                    colorMode = BannerColorMode.Gradient;
                }

                logger?.LogInformation("Generating map background from banner using {ColorMode} mode", colorMode);
                return CoverComposer.GenerateMapBackgroundFromBanner(
                    banner,
                    songColor1A,
                    songColor1B,
                    songColor2A,
                    songColor2B,
                    colorMode);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to generate map background from banner");
            return null;
        }
    }

    private async Task<Image<Bgra32>?> GenerateBannerAsync(CancellationToken cancellationToken)
    {
        // Check if we have a real map background file
        if (!HasImage(ImageAssetType.MapBackground))
        {
            logger?.LogWarning("Cannot generate banner: map background not found");
            return null;
        }

        Image<Bgra32> mapBackground = await GetMapBackgroundAsync(cancellationToken: cancellationToken);
        using (mapBackground)
        {
            return CoverComposer.GenerateBanner(mapBackground);
        }
    }

    private async Task<Image<Bgra32>?> GenerateAlbumBackgroundAsync(CancellationToken cancellationToken)
    {
        // Check if we have a real map background file
        if (!HasImage(ImageAssetType.MapBackground))
        {
            logger?.LogWarning("Cannot generate album background: map background not found");
            return null;
        }

        Image<Bgra32> mapBackground = await GetMapBackgroundAsync(cancellationToken: cancellationToken);
        using (mapBackground)
        {
            return CoverComposer.GenerateAlbumBackground(mapBackground);
        }
    }

    /// <summary>
    /// Checks if an image appears to be a placeholder (solid magenta).
    /// </summary>
    private static bool IsPlaceholder(Image<Bgra32> image)
    {
        // Check top-left corner pixel for magenta
        Bgra32 pixel = image[0, 0];
        return pixel.R == 255 && pixel.G == 0 && pixel.B == 255;
    }

    /// <summary>
    /// Applies a fadeout effect to the bottom 28.125% of the coach image if the bottom row has any non-transparent pixels.
    /// </summary>
    private static void ApplyCoachFadeEffect(Image<Bgra32> image)
    {
        // Check if bottom row has any non-transparent pixels
        bool hasOpaquePixels = false;
        int lastRow = image.Height - 1;

        for (int x = 0; x < image.Width; x++)
        {
            if (image[x, lastRow].A > 0)
            {
                hasOpaquePixels = true;
                break;
            }
        }

        if (!hasOpaquePixels)
            return;

        // Apply fadeout to bottom 28.125% of height (9/32)
        int fadeHeight = (int)(image.Height * 0.28125);
        int fadeStartY = image.Height - fadeHeight;

        for (int y = fadeStartY; y < image.Height; y++)
        {
            int distFromBottom = image.Height - y;
            float fadeAlpha = (float)distFromBottom / fadeHeight;

            for (int x = 0; x < image.Width; x++)
            {
                Bgra32 pixel = image[x, y];
                pixel.A = (byte)(pixel.A * fadeAlpha);
                image[x, y] = pixel;
            }
        }
    }
}