using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

/// <summary>
/// Handles how assets are written to disk for a specific platform (Cooked vs Uncooked, directory structure, binary wrapping).
/// </summary>
public interface IPlatformExporter
{
    UbiArtPlatform Platform { get; }

    /// <summary>
    /// Writes a generated engine resource file (SongDesc, Tapes, ISCs).
    /// Handles platform-specific post-processing (e.g. null-termination for text-based platforms, .ckd extensions).
    /// </summary>
    Task WriteEngineResourceAsync(ExportContext context, string relativePath, byte[] content);

    /// <summary>
    /// Writes a raw binary file (e.g. specific pre-compiled actors, binary buffers).
    /// Handles platform-specific extensions (e.g. .ckd).
    /// </summary>
    Task WriteBinaryFileAsync(ExportContext context, string relativePath, byte[] data);

    /// <summary>
    /// Writes a texture. Handles resizing verification, format conversion (TGA vs XTX vs GTX), and headers.
    /// </summary>
    Task WriteTextureAsync(ExportContext context, string relativePath, Image<Bgra32> image);

    /// <summary>
    /// Writes an audio file. Handles format conversion (WAV vs RAKI/Opus) and trimming.
    /// </summary>
    Task WriteAudioAsync(ExportContext context, string relativePath, string sourcePath, List<int>? markers = null);

    /// <summary>
    /// Gets the root folder relative to the output for this platform (e.g., "cache/itf_cooked/nx" or "world/maps").
    /// </summary>
    string GetPlatformRootFolder(string mapName);
}