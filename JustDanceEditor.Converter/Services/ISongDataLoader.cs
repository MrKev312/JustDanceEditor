using JustDanceEditor.Formats.UbiArt;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Converter.Services;

public interface ISongDataLoader
{
    JDUbiArtSong LoadSongData(ConversionRequest request, FileSystem fileSystem);
}