using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Services;

public enum UbiArtContainerStyle
{
    Unknown,
    Cooked,
    Uncooked
}

public enum UbiArtEngineVersion
{
    Unknown,
    JD2014,
    JD2015,
    Modern
}

public class UbiArtVersionProfile(UbiArtContainerStyle containerStyle, UbiArtEngineVersion engineVersion, IUbiArtLayout layout, IUbiArtSerializer serializer, IUbiArtDataMapper? mapper = null)
{
    public UbiArtContainerStyle ContainerStyle { get; set; } = containerStyle;
    public UbiArtEngineVersion EngineVersion { get; set; } = engineVersion;
    public IUbiArtLayout Layout { get; set; } = layout;
    public IUbiArtSerializer Serializer { get; set; } = serializer;
    public IUbiArtDataMapper Mapper { get; set; } = mapper ?? new DefaultUbiArtDataMapper();

    // If present, contains the numeric JD engine version found in SongDesc
    public uint? EngineNumericVersion { get; set; }
}