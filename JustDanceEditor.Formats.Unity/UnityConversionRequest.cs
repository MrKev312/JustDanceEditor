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
public class UnityConversionRequest : ConversionRequestBase
{
    /// <summary>
    /// Creates a new Unity conversion request.
    /// </summary>
    /// <param name="inputPath">Input folder path (JDI package or Unity song folder).</param>
    /// <param name="outputPath">Output folder path.</param>
    /// <param name="templatePath">Path to the Unity template folder containing bundle templates.</param>
    public UnityConversionRequest(string inputPath, string outputPath, string templatePath)
        : base(inputPath, outputPath)
    {
        TemplatePath = templatePath;
    }

    /// <summary>
    /// Path to the Unity template folder containing bundle templates.
    /// Required for export operations.
    /// </summary>
    public string TemplatePath { get; set; }

    /// <summary>
    /// Whether to attempt to download cover/title images from online sources if not found locally.
    /// </summary>
    public bool OnlineCover { get; set; }

    /// <summary>
    /// The export type (OfflineCache or CustomServer).
    /// </summary>
    public ExportType ExportType { get; set; } = ExportType.CustomServer;

    /// <summary>
    /// The cache number to use when <see cref="ExportType"/> is <see cref="ExportType.OfflineCache"/>.
    /// </summary>
    public uint? CacheNumber { get; set; }
}