using JustDanceEditor.Formats.UbiArt.Images;
using JustDanceEditor.Formats.UbiArt.Services.Layouts;
using JustDanceEditor.Formats.UbiArt.Services.Serialization;

using System.Collections.Generic;
using System.Globalization;

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

    /// <summary>
    /// Returns an <see cref="IComparer{string}"/> suitable for pictogram name sorting
    /// depending on the detected engine numeric version.
    /// - JD2014-2018: use existing alphanumeric text-first comparer (preserves current behavior)
    /// - JD2019-2022: use ordinal (case-insensitive) comparer to match engine behavior
    /// </summary>
    public IComparer<string> PictoNameComparer
    {
        get
        {
            uint? v = EngineNumericVersion;
            if (v.HasValue && v.Value >= 2019 && v.Value <= 2022)
            {
                // Use a digits-first alphanumeric comparer for 2019+ to match observed montage ordering
                return AlphanumericDigitsFirstComparer.Instance;
            }

            // Default / fallback: preserve the existing alphanumeric behaviour
            return AlphanumericTextFirstComparer.Instance;
        }
    }
}