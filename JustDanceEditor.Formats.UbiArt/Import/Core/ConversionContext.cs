using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Import.Core;

public class ConversionContext(UbiArtConversionRequest? request, JustDanceUbiArtFileSystem fileSystem)
{
    public UbiArtConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public IntermediateSongPackage IntermediatePackage { get; set; } = new();
    public JustDanceUbiArtFileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public JDUbiArtSong SongData { get; set; } = new();
}