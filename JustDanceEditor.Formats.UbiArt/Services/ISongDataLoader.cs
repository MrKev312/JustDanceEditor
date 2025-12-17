using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Formats.UbiArt.Services;

public interface ISongDataLoader
{
    JDUbiArtSong LoadSongData(UbiArtConversionRequest request, FileSystem fileSystem);
}