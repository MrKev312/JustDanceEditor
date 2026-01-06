using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Services;

public interface IUbiArtAssetWriter
{
    Task ExportToUncookedAsync(
        IntermediateSongPackage package,
        string? materializedRoot,
        string outputFolder,
        IUbiArtLayout? layout = null,
        UbiArtContainerStyle containerStyle = UbiArtContainerStyle.Uncooked,
        UbiArtEngineVersion engineVersion = UbiArtEngineVersion.Modern,
        IFileSystem? io = null);
}