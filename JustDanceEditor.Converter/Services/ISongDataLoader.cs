using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.UbiArt;

namespace JustDanceEditor.Converter.Services;

public interface ISongDataLoader
{
    JDUbiArtSong LoadSongData(ConversionRequest request, FileSystem fileSystem);
}