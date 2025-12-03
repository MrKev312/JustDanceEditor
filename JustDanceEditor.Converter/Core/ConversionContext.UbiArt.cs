using JustDanceEditor.Formats.UbiArt;

namespace JustDanceEditor.Converter.Core;

public partial class ConversionContext
{
    public JDUbiArtSong SongData { get; set; } = new();

    private partial string? TryGetSongNameFromUbiArt()
        => string.IsNullOrWhiteSpace(SongData?.Name) ? null : SongData.Name;

    private partial int? TryGetCoachCountFromUbiArt()
    {
        if (SongData?.SongDesc?.COMPONENTS?.Length > 0)
            return SongData.SongDesc.COMPONENTS[0].NumCoach;
        return null;
    }
}
