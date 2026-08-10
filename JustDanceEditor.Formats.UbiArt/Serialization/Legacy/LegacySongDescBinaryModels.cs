using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

internal sealed class LegacySongDesc : LegacyResourceFileBinary<LegacySongDescComponent>
{
    public static explicit operator SongDesc(LegacySongDesc value) =>
        (SongDesc)value.Component;
}

internal abstract class LegacySongDescComponent : LegacyResourceComponent
{
    public string MapName { get; set; } = string.Empty;
    public uint RawEngineVersion { get; set; }

    public static explicit operator SongDesc(LegacySongDescComponent value) =>
        value switch
        {
            LegacyJd2014SongDescComponent jd2014 => (SongDesc)jd2014,
            LegacyModernSongDescComponent classic => (SongDesc)classic,
            LegacyGeneratedModernSongDescComponent generated => (SongDesc)generated,
            _ => throw new InvalidDataException($"Unsupported legacy songdesc component '{value.GetType().Name}'.")
        };

}

[LegacyBinaryTypeId(0x8AC2B5C6, MaxEngineVersion = 2014)]
internal sealed class LegacyJd2014SongDescComponent : LegacySongDescComponent
{
    private const int BetaComponentSize = 0x88;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public string[] RelatedAlbums { get; set; } = [];

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown0 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown1 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown2 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public LegacyJd2014SongDescEntry[] SongDescEntries { get; set; } = [];

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public string BetaArtist { get; set; } = string.Empty;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public string BetaTitle { get; set; } = string.Empty;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public string Artist { get; set; } = string.Empty;

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public string Title { get; set; } = string.Empty;

    public uint CoachCount { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaDifficulty { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaDefaultCoachId { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize)]
    public uint BetaUnknown3 { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public uint DefaultCoachId { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public uint Difficulty { get; set; }

    public float TagScale { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public LegacySongDescPreview Preview { get; set; } = new();

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public uint LyricColorIntensity { get; set; }

    [LegacyBinaryCondition(nameof(ComponentSize), BetaComponentSize, Invert = true)]
    public LegacyTypedAbgrColor LyricColor { get; set; } = new();

    public static explicit operator SongDesc(LegacyJd2014SongDescComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new InfoComponent
            {
                Class = "JD_SongDescTemplate",
                MapName = value.MapName,
                JDVersion = 2014,
                OriginalJDVersion = 2014,
                Artist = value.IsBeta ? value.BetaArtist : value.Artist,
                DancerName = string.Empty,
                Title = value.IsBeta ? value.BetaTitle : value.Title,
                NumCoach = checked((int)value.CoachCount),
                MainCoach = value.EffectiveDefaultCoachId == uint.MaxValue ? 0 : checked((int)value.EffectiveDefaultCoachId),
                Difficulty = value.EffectiveDifficulty,
                SweatDifficulty = 0,
                Status = 0,
                LocaleID = 1,
                DefaultColors = new DefaultColors
                {
                    Lyrics = value.IsBeta ? [1f, 1f, 1f, 1f] : value.LyricColor.Color.ToRgba()
                }
            }
        ]
    };

    private bool IsBeta => ComponentSize == BetaComponentSize;
    private uint EffectiveDefaultCoachId => IsBeta ? BetaDefaultCoachId : DefaultCoachId;
    private uint EffectiveDifficulty => IsBeta ? BetaDifficulty : Difficulty;
}

[LegacyBinaryTypeId(0x8AC2B5C6, MinEngineVersion = 2015, MaxEngineVersion = 2015)]
internal sealed class LegacyModernSongDescComponent : LegacySongDescComponent
{
    public uint OriginalVersion { get; set; }
    public int HasBaseMap { get; set; }

    [LegacyBinaryCondition(nameof(HasBaseMap), 1)]
    public string? BaseMapName { get; set; }

    public LegacySongDescTags Tags { get; set; } = new();
    public string Artist { get; set; } = string.Empty;
    public string DancerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public uint CoachCount { get; set; }
    public uint DefaultCoachId { get; set; }
    public uint Difficulty { get; set; }
    public int SweatDifficulty { get; set; }
    public int Status { get; set; }
    public int LocaleId { get; set; }
    public float TagScale { get; set; }
    public LegacySongDescPreview Preview { get; set; } = new();

    public uint LyricColorIntensity { get; set; }
    public LegacyTypedAbgrColor LyricColor { get; set; } = new();

    public static explicit operator SongDesc(LegacyModernSongDescComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new InfoComponent
            {
                Class = "JD_SongDescTemplate",
                MapName = value.MapName,
                BaseMapName = value.BaseMapName ?? string.Empty,
                JDVersion = value.RawEngineVersion,
                OriginalJDVersion = value.OriginalVersion,
                Artist = value.Artist,
                DancerName = value.DancerName,
                Title = value.Title,
                NumCoach = checked((int)value.CoachCount),
                MainCoach = value.DefaultCoachId == uint.MaxValue ? 0 : checked((int)value.DefaultCoachId),
                Difficulty = value.Difficulty,
                SweatDifficulty = value.SweatDifficulty < 0 ? 0u : (uint)value.SweatDifficulty,
                Status = value.Status,
                LocaleID = value.LocaleId,
                DefaultColors = new DefaultColors
                {
                    Lyrics = value.LyricColor.Color.ToRgba()
                }
            }
        ]
    };
}

[LegacyBinaryTypeId(0x8AC2B5C6, MinEngineVersion = 2016)]
internal sealed class LegacyGeneratedModernSongDescComponent : LegacySongDescComponent
{
    public uint OriginalVersion { get; set; }
    public int HasBaseMap { get; set; }

    [LegacyBinaryCondition(nameof(HasBaseMap), 1)]
    public string? BaseMapName { get; set; }

    public LegacyFixedSongDescTags Tags { get; set; } = new();
    public string Artist { get; set; } = string.Empty;
    public string DancerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public uint CoachCount { get; set; }
    public uint DefaultCoachId { get; set; }
    public uint Difficulty { get; set; }
    public int SweatDifficulty { get; set; }
    public int Status { get; set; }
    public int LocaleId { get; set; }
    public float TagScale { get; set; }
    public LegacySongDescPreview Preview { get; set; } = new();
    public LegacySongDescVersionBlock VersionBlock { get; set; } = new();
    public float LyricColorIntensity { get; set; }
    public LegacyTypedAbgrColor LyricColor { get; set; } = new();

    public static explicit operator SongDesc(LegacyGeneratedModernSongDescComponent value) => new()
    {
        Class = "Actor_Template",
        Components =
        [
            new InfoComponent
            {
                Class = "JD_SongDescTemplate",
                MapName = value.MapName,
                BaseMapName = value.BaseMapName ?? string.Empty,
                JDVersion = value.RawEngineVersion,
                OriginalJDVersion = value.OriginalVersion,
                Artist = value.Artist,
                DancerName = value.DancerName,
                Title = value.Title,
                NumCoach = checked((int)value.CoachCount),
                MainCoach = value.DefaultCoachId == uint.MaxValue ? 0 : checked((int)value.DefaultCoachId),
                Difficulty = value.Difficulty,
                SweatDifficulty = value.SweatDifficulty < 0 ? 0u : (uint)value.SweatDifficulty,
                Status = value.Status,
                LocaleID = value.LocaleId,
                DefaultColors = new DefaultColors
                {
                    Lyrics = value.LyricColor.Color.ToRgba()
                }
            }
        ]
    };
}

internal sealed class LegacySongDescTags
{
    public LegacySongDescTagEntry[] Entries { get; set; } = [];
}

internal sealed class LegacySongDescTagEntry
{
    public int SerializedSize { get; set; }
    public int Unknown0 { get; set; }
    public int Unknown1 { get; set; }
    public int Unknown2 { get; set; }
    public uint Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public int Unknown5 { get; set; }
    public int Unknown6 { get; set; }
    public int Unknown7 { get; set; }
    public int Unknown8 { get; set; }
    public uint Unknown9 { get; set; }
    public int Unknown10 { get; set; }
    public int Unknown11 { get; set; }
}

internal sealed class LegacyFixedSongDescTags
{
    public int Version { get; set; }
    public int SerializedSize { get; set; }
    public int Unknown0 { get; set; }
    public int Unknown1 { get; set; }
    public int TagCount { get; set; }
    public uint DefaultTag { get; set; }
    public int LocaleCount { get; set; }
    public int Unknown2 { get; set; }
    public int Unknown3 { get; set; }
    public int Unknown4 { get; set; }
    public int Unknown5 { get; set; }
    public uint DefaultLocale { get; set; }
    public int Unknown6 { get; set; }
    public int Enabled { get; set; }
}

internal sealed class LegacyJd2014SongDescEntry
{
    public uint SerializedSize { get; set; }
    public uint Group { get; set; }
    public uint Value { get; set; }
    public uint Category { get; set; }
    public uint Unknown0 { get; set; }
    public uint HasInlineColor { get; set; }

    [LegacyBinaryCondition(nameof(HasInlineColor), 1)]
    public LegacyTypedAbgrColor? InlineColor { get; set; }

    public uint Unknown1 { get; set; }
    public uint Unknown2 { get; set; }
    public uint InvalidValue { get; set; }
    public uint Unknown3 { get; set; }
    public uint Enabled { get; set; }
}

internal sealed class LegacySongDescPreview
{
    public int PreviewCount { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewEntrySize { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public uint PreviewEntryTypeId { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewEntryBeat { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewEntryPadding { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewLoopSize { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public uint PreviewLoopTypeId { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewLoopStartBeat { get; set; }

    [LegacyBinaryCondition(nameof(PreviewCount), 0, Invert = true)]
    public int PreviewLoopEndBeat { get; set; }
}

internal sealed class LegacySongDescVersionBlock
{
    public int Version { get; set; }
    public uint TypeId { get; set; }
    public float Unknown0 { get; set; }
    public float Unknown1 { get; set; }
    public float Unknown2 { get; set; }
}

internal sealed class LegacyTypedAbgrColor
{
    public LegacyBinaryTypeId<LegacyColorBinary> TypeId { get; set; }
    public LegacyAbgrColorValue Color { get; set; } = new();
}

internal sealed class LegacyAbgrColorValue
{
    public float Alpha { get; set; }
    public float Blue { get; set; }
    public float Green { get; set; }
    public float Red { get; set; }

    public float[] ToRgba() => [Alpha, Red, Green, Blue];
}

