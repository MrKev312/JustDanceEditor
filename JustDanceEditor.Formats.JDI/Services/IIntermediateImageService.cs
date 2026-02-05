using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.JDI.Services;

/// <summary>
/// Service for retrieving and generating images from an intermediate song package.
/// All images are stored at full resolution in the package; this service handles
/// on-demand retrieval with optional scaling.
/// </summary>
public interface IIntermediateImageService
{
    /// <summary>
    /// Gets or creates the cover image (standard 16:9 aspect ratio).
    /// Full resolution stored is 640x360.
    /// </summary>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The cover image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetCoverAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates the square cover image (1:1 aspect ratio, squashed from standard cover).
    /// Used by UbiArt formats.
    /// </summary>
    /// <param name="width">Target width. If null, returns at stored resolution.</param>
    /// <param name="height">Target height. If null, returns at stored resolution.</param>
    /// <returns>The square cover image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetSquareCoverAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates the album coach composite image (all coaches composited together).
    /// Full resolution stored is 1024x1024.
    /// </summary>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The album coach image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetAlbumCoachAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an individual coach image.
    /// Full resolution stored is 1024x1024.
    /// </summary>
    /// <param name="coachIndex">1-based coach index.</param>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <param name="useFadeEffect">If true, applies a fadeout effect to the bottom 28.125% of the image if the bottom row has any non-transparent pixels.</param>
    /// <returns>The coach image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetCoachAsync(int coachIndex, int? width = null, int? height = null, bool useFadeEffect = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the map background image (used as source for other backgrounds).
    /// Full resolution stored is 2048x1024.
    /// </summary>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The map background image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetMapBackgroundAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the banner image.
    /// Full resolution stored is 1024x512.
    /// </summary>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The banner image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetBannerAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the album background image (center-cropped square from coaches background).
    /// Full resolution stored is 256x256.
    /// </summary>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The album background image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetAlbumBackgroundAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a pictogram image by its identifier.
    /// Full resolution is 512x512 (solo) or 512x354 (duo/trio/quad).
    /// </summary>
    /// <param name="pictogramId">The pictogram identifier (filename without extension).</param>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The pictogram image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetPictogramAsync(string pictogramId, int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all pictogram IDs available in the package.
    /// </summary>
    /// <returns>Collection of pictogram identifiers.</returns>
    IEnumerable<string> GetPictogramIds();

    /// <summary>
    /// Gets the song title logo image.
    /// </summary>
    /// <param name="width">Target width. If null, returns full resolution.</param>
    /// <param name="height">Target height. If null, returns full resolution.</param>
    /// <returns>The song title logo image scaled to the requested dimensions. Returns a magenta placeholder if unavailable.</returns>
    Task<Image<Bgra32>> GetSongTitleLogoAsync(int? width = null, int? height = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates all missing images that can be derived from existing assets.
    /// This ensures the package has all images in full resolution for export.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    Task GenerateAllMissingImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a specific image asset exists in the package.
    /// </summary>
    /// <param name="assetType">The type of asset to check.</param>
    /// <returns>True if the asset exists, false otherwise.</returns>
    bool HasImage(ImageAssetType assetType);
}

/// <summary>
/// Types of image assets that can be stored in an intermediate package.
/// </summary>
public enum ImageAssetType
{
    Cover,
    SquareCover,
    AlbumCoach,
    AlbumBackground,
    Coach,
    MapBackground,
    Banner,
    Pictogram,
    SongTitleLogo
}