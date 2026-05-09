using JustDanceEditor.Formats.UbiArt.Import.AssetExtraction;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

namespace JustDanceEditor.Formats.UbiArt.Import;

public enum UbiArtPlatform
{
    Uncooked,
    Wii,
    WiiU,
    NX,
    PC,
    X360,
    Durango
}

public static class UbiArtPlatformExtensions
{
    public static string GetCookedFolderName(this UbiArtPlatform platform) => platform switch
    {
        UbiArtPlatform.Uncooked => string.Empty,
        UbiArtPlatform.Wii => "wii",
        UbiArtPlatform.WiiU => "wiiu",
        UbiArtPlatform.NX => "nx",
        UbiArtPlatform.PC => "pc",
        UbiArtPlatform.X360 => "x360",
        UbiArtPlatform.Durango => "durango",
        _ => platform.ToString().ToLowerInvariant()
    };
}

public enum UbiArtEngineVersion
{
    Unknown,
    JD2014 = 2014,
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
    public string PlatformFolder => Platform.GetCookedFolderName();

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
