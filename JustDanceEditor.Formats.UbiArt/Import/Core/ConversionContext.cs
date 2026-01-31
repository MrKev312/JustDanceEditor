using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Core;

public class ConversionContext(UbiArtConversionRequest? request, LayeredFileSystem fileSystem)
{
    public UbiArtConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public IntermediateSongPackage IntermediatePackage { get; set; } = new();
    public LayeredFileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public JDUbiArtSong SongData { get; set; } = new();
}