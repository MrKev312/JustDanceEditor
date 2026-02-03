using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Import;

public interface IUbiArtDataMapper
{
    JDUbiArtSong Map(JDUbiArtSong songData);
}