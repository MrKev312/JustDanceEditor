using JustDanceEditor.Formats.UbiArt.Images;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Services;

public enum UbiArtPlatform
{
    Uncooked,
    WiiU,
    NX,
    PC
}

public enum UbiArtEngineVersion
{
    Unknown,
    JD2014,
    JD2015,
    JD2016,
    JD2017,
    JD2018,
    JD2019,
    JD2020,
    JD2021,
    JD2022
}

public class UbiArtVersionProfile(UbiArtPlatform platform, UbiArtEngineVersion engineVersion, IUbiArtLayout layout, IUbiArtSerializer serializer, IUbiArtDataMapper? mapper = null)
{
    public UbiArtPlatform Platform { get; set; } = platform;
    public UbiArtEngineVersion EngineVersion { get; set; } = engineVersion;
    public IUbiArtLayout Layout { get; set; } = layout;
    public IUbiArtSerializer Serializer { get; set; } = serializer;
    public IUbiArtDataMapper Mapper { get; set; } = mapper ?? new DefaultUbiArtDataMapper();

    /// <summary>
    /// Returns an <see cref="IComparer{string}"/> suitable for pictogram name sorting
    /// depending on the engine version.
    /// - JD2014-2018: use existing alphanumeric text-first comparer (preserves current behavior)
    /// - JD2019-2022: use ordinal (case-insensitive) comparer to match engine behavior
    /// </summary>
    public IComparer<string> PictoNameComparer
    {
        get
        {
            if (EngineVersion is >= UbiArtEngineVersion.JD2019 and <= UbiArtEngineVersion.JD2022)
            {
                // Use a digits-first alphanumeric comparer for 2019+ to match observed montage ordering
                return AlphanumericDigitsFirstComparer.Instance;
            }

            // Default / fallback: preserve the existing alphanumeric behaviour
            return AlphanumericTextFirstComparer.Instance;
        }
    }
}