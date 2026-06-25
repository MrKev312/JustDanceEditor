using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt;

/// <summary>
/// Specifies whether the UbiArt content is cooked (platform-specific) or uncooked (raw).
/// </summary>
public enum CookedType
{
    /// <summary>
    /// Content has been processed for a specific platform (e.g., Wii, WiiU, NX, PC).
    /// </summary>
    Cooked,

    /// <summary>
    /// Content is in raw/unprocessed format.
    /// </summary>
    Uncooked
}

/// <summary>
/// Conversion request for UbiArt format operations.
/// </summary>
/// <remarks>
/// Creates a new UbiArt conversion request.
/// </remarks>
/// <param name="inputPath">Input folder or IPK path.</param>
/// <param name="outputPath">Output folder path.</param>
/// <param name="songName">Optional song name to select when multiple songs are present.</param>
public class UbiArtConversionRequest(string inputPath, string outputPath, string? songName = null) : ConversionRequestBase(inputPath, outputPath)
{
    internal string TempSessionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Song name to disambiguate when the input contains multiple maps.
    /// If null or empty, and multiple songs are found, <see cref="SelectSongAsync"/> will be invoked.
    /// </summary>
    public string? SongName { get; set; } = songName;

    /// <summary>
    /// Whether the input/output content is cooked or uncooked.
    /// </summary>
    public CookedType Type { get; set; } = CookedType.Cooked;

    /// <summary>
    /// For IMPORT: which platform we are importing FROM (auto-detected during import if null).
    /// </summary>
    public UbiArtPlatform? ImportPlatform { get; set; }

    /// <summary>
    /// For IMPORT: which engine version we are importing FROM (auto-detected during import if null).
    /// </summary>
    public UbiArtEngineVersion? ImportEngineVersion { get; set; }

    /// <summary>
    /// For EXPORT: which platform to export TO.
    /// </summary>
    public UbiArtPlatform ExportPlatform { get; set; } = UbiArtPlatform.Uncooked;

    /// <summary>
    /// For EXPORT: which engine version to export TO.
    /// </summary>
    public UbiArtEngineVersion ExportEngineVersion { get; set; } = UbiArtEngineVersion.JD2022;

    /// <summary>
    /// Render cinematic videos and discard the raw frame stream instead of encoding a video.
    /// </summary>
    public bool RenderVideoSpeedTest { get; set; }

    /// <summary>
    /// Optional delegate for UI to select a song when multiple songs are present in the input.
    /// Receives an array of available song names and should return the selected name, or null to cancel.
    /// </summary>
    public Func<string[], Task<string?>>? SelectSongAsync { get; set; }

}