using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Export;

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