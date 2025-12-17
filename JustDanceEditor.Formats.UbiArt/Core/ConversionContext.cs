using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Formats.UbiArt.Core;

public class ConversionContext(UbiArtConversionRequest? request, FileSystem fileSystem)
{
    public UbiArtConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public IntermediateSongPackage IntermediatePackage { get; set; } = new();
    public FileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public JDUbiArtSong SongData { get; set; } = new();
}