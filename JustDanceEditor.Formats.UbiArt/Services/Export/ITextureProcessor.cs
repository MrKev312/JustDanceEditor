using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Services.Export;

public record ProcessedTexture(string Name, Image<Bgra32> Image);

/// <summary>
/// Responsible for preparing images in memory (resizing, compositing, ignoring specific logos) before export.
/// </summary>
public interface ITextureProcessor
{
    /// <summary>
    /// Processes all branding and coach assets from the package.
    /// Handles logic for cover_albumcoach, resizing generic covers, and ignoring logos.
    /// </summary>
    IEnumerable<ProcessedTexture> ProcessAssets(IntermediateSongPackage package, string materializedRoot, IFileSystem io);

    /// <summary>
    /// Specific logic to generate the composite Album Coach image (1-4 coaches).
    /// </summary>
    Image<Bgra32> GenerateAlbumCoach(List<Image<Bgra32>> coaches);
}