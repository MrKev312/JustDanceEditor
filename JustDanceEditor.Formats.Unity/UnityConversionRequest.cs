using JustDanceEditor.Conversion.Abstractions.Prompts;
using JustDanceEditor.Formats.JDI;

namespace JustDanceEditor.Formats.Unity;

/// <summary>
/// Specifies the export type for Unity conversions.
/// </summary>
public enum ExportType
{
    /// <summary>
    /// Export for offline cache mode (requires cache number).
    /// </summary>
    OfflineCache,

    /// <summary>
    /// Export for custom server mode.
    /// </summary>
    CustomServer
}

/// <summary>
/// Conversion request for Unity format operations.
/// </summary>
/// <remarks>
/// Creates a new Unity conversion request.
/// </remarks>
/// <param name="inputPath">Input folder path (JDI package or Unity song folder).</param>
/// <param name="outputPath">Output folder path.</param>
public class UnityConversionRequest(string inputPath, string outputPath) : ConversionRequestBase(inputPath, outputPath)
{
    /// <summary>
    /// The export type (OfflineCache or CustomServer).
    /// </summary>
    public ExportType ExportType { get; set; } = ExportType.CustomServer;

    /// <summary>
    /// The cache number to use when <see cref="ExportType"/> is <see cref="ExportType.OfflineCache"/>.
    /// </summary>
    public uint? CacheNumber { get; set; }

    /// <summary>
    /// Optional interaction channel used by exports that need a late prompt after inspecting the output path.
    /// </summary>
    public IConversionInteraction? Interaction { get; set; }

    /// <summary>
    /// Headless override for creating a new cache setup when no SD_Cache folder can be found.
    /// </summary>
    public bool? GenerateCacheIfMissing { get; set; }
}