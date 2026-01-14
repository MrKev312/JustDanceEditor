using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

namespace JustDanceEditor.Formats.UbiArt.Services;

public interface IUbiArtAssetWriter
{
    /// <summary>
    /// Exports a song package to UbiArt format.
    /// Routes to uncooked (Lua) or cooked (JSON + .ckd) based on platform.
    /// </summary>
    Task ExportAsync(
        IntermediateSongPackage package,
        string? materializedRoot,
        string outputFolder,
        UbiArtPlatform platform,
        UbiArtEngineVersion engineVersion,
        IUbiArtLayout? layout = null,
        IFileSystem? io = null);
}