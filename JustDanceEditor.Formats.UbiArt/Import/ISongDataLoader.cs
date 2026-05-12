using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Import;

public interface ISongDataLoader
{
    JDUbiArtSong LoadSongData(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem);

    /// <summary>
    /// Loads only the <see cref="SongDesc"/> for the given request and filesystem. This method
    /// should not perform logging and should throw <see cref="FileNotFoundException"/> if no
    /// SongDesc or jddb.json is available.
    /// </summary>
    SongDesc LoadSongDesc(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem);
}