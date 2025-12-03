using JustDanceEditor.Converter;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Formats.UbiArt.Services;

public interface ISongDataLoader
{
    JDUbiArtSong LoadSongData(ConversionRequest request, FileSystem fileSystem);
}
