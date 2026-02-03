using JustDanceEditor.Formats.UbiArt.Model;

namespace JustDanceEditor.Formats.UbiArt.Import;

public class DefaultUbiArtDataMapper : IUbiArtDataMapper
{
    public JDUbiArtSong Map(JDUbiArtSong songData) => songData;
}