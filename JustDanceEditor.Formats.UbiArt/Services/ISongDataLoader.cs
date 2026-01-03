using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Files;

namespace JustDanceEditor.Formats.UbiArt.Services;

public interface ISongDataLoader
{
    JDUbiArtSong LoadSongData(UbiArtConversionRequest request, FileSystem fileSystem);

    /// <summary>
    /// Loads only the <see cref="SongDesc"/> for the given request and filesystem. This method
    /// should not perform logging and should throw <see cref="FileNotFoundException"/> if no
    /// SongDesc or jddb.json is available.
    /// </summary>
    SongDesc LoadSongDesc(UbiArtConversionRequest request, FileSystem fileSystem);
}