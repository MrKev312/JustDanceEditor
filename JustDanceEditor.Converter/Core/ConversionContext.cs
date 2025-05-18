using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.UbiArt;
namespace JustDanceEditor.Converter.Core;

public class ConversionContext(ConversionRequest? request, FileSystem fileSystem)
{
    public ConversionRequest Request { get; } = request ?? throw new ArgumentNullException(nameof(request));
    public JDUbiArtSong SongData { get; set; } = new();
    public FileSystem FileSystem { get; } = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    public Guid SongID => Request.SongGUID;
}