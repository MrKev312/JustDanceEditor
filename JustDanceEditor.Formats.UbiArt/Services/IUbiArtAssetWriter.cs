using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;

namespace JustDanceEditor.Formats.UbiArt.Services;

public interface IUbiArtAssetWriter
{
    Task ExportToUncookedAsync(
        IntermediateSongPackage package,
        string? materializedRoot,
        string outputFolder,
        IUbiArtLayout? layout = null,
        UbiArtContainerStyle containerStyle = UbiArtContainerStyle.Uncooked,
        UbiArtEngineVersion engineVersion = UbiArtEngineVersion.JD2022,
        IFileSystem? io = null);
}