using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

internal sealed class LegacyResourceHeader(
    int serializedSize,
    uint componentTypeId,
    uint componentSize,
    uint baseTypeSize = 0x6C)
{
    public uint Version => 1;

    public int SerializedSize => serializedSize;

    public LegacyBinaryTypeId<LegacyResourceBaseBinary> BaseTypeId => new();

    public uint BaseTypeSize => baseTypeSize;

    public LegacyPadding Reserved => LegacyBinary.Padding(28);

    public int ComponentCount => 1;

    public uint ComponentTypeId => componentTypeId;

    public uint ComponentSize => componentSize;
}

internal sealed class LegacySongDescFile(
    string mapName,
    UbiArtEngineVersion engineVersion,
    uint originalJustDanceVersion,
    string artist,
    string title,
    int coachCount,
    uint difficulty,
    int previewEntryBeat,
    int previewLoopStartBeat,
    int previewLoopEndBeat,
    LegacyAbgrColor lyricColor)
{
    public LegacyResourceHeader Header => new(
        0x15A5,
        0x8AC2B5C6u,
        (int)engineVersion >= 2016 ? 0xF4u : 0xE0u);

    public string MapName => mapName;

    public uint EngineVersion => (uint)engineVersion;

    public uint OriginalJustDanceVersion => originalJustDanceVersion;

    public int Unknown0 => 0;

    public LegacySongDescTags Tags { get; } = new();

    public string Artist => artist;

    public string DancerName => "Unknown Dancer";

    public string Title => title;

    public uint CoachCount => (uint)coachCount;

    public uint DefaultCoachId => uint.MaxValue;

    public uint Difficulty => difficulty;

    public int SweatDifficulty => 0;

    public int Status => 0;

    public int LocaleId => 1;

    public float TagScale => 0.5f;

    public int PreviewCount => 2;

    public int PreviewEntrySize => 0x10;

    public uint PreviewEntryTypeId => 0x6F4037D0;

    public int PreviewEntryBeat => previewEntryBeat;

    public int PreviewEntryPadding => 0;

    public int PreviewLoopSize => 0x10;

    public uint PreviewLoopTypeId => 0xB11FC1B6;

    public int PreviewLoopStartBeat => previewLoopStartBeat;

    public int PreviewLoopEndBeat => previewLoopEndBeat;

    public LegacySongDescVersionBlock? VersionBlock => (int)engineVersion >= 2016 ? new() : null;

    public float LyricColorIntensity => 1.0f;

    public uint LyricColorTypeId => 0x31D3B347;

    public LegacyAbgrColor LyricColor => lyricColor;

    public LegacySongDescCoachDefaults CoachDefaults { get; } = new();
}

internal sealed class LegacyJd2014SongDescFile(
    string mapName,
    string artist,
    string title,
    int coachCount,
    uint difficulty,
    int previewEntryBeat,
    int previewLoopStartBeat,
    int previewLoopEndBeat,
    LegacyAbgrColor lyricColor)
{
    public LegacyResourceHeader Header => new(0x2E0, 0x8AC2B5C6u, 0x104, 0xAC);

    public string MapName => mapName;

    public uint EngineVersion => 5;

    public uint RelatedAlbumCount => 0;

    public LegacyJd2014SongDescEntryList SongDescEntries { get; } = new();

    public string Artist => artist;

    public string Title => title;

    public uint CoachCount => (uint)coachCount;

    public uint DefaultCoachId => 1;

    public uint Difficulty => difficulty;

    public float TagScale => 0.5f;

    public int PreviewCount => 2;

    public int PreviewEntrySize => 0x10;

    public uint PreviewEntryTypeId => 0x6F4037D0;

    public int PreviewEntryBeat => previewEntryBeat;

    public int PreviewEntryEndBeat => previewEntryBeat;

    public int PreviewLoopSize => 0x10;

    public uint PreviewLoopTypeId => 0xB11FC1B6;

    public int PreviewLoopStartBeat => previewLoopStartBeat;

    public int PreviewLoopEndBeat => previewLoopEndBeat;

    public int LyricColorIntensity => 2;

    public uint LyricColorTypeId => 0x31D3B347;

    public LegacyAbgrColor LyricColor => lyricColor;

    public LegacyJd2014SongDescCoachDefaults CoachDefaults { get; } = new();
}

internal sealed class LegacySongDescTags
{
    public int Version => 1;

    public int SerializedSize => 0x58;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int TagCount => 7;

    public uint DefaultTag => uint.MaxValue;

    public int LocaleCount => 3;

    public int Unknown2 => 0;

    public int Unknown3 => 0;

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public uint DefaultLocale => uint.MaxValue;

    public int Unknown6 => 0;

    public int Enabled => 1;
}

internal sealed class LegacyJd2014SongDescEntryList
{
    public int EntryCount => 1;

    public LegacyJd2014SongDescEntry DefaultEntry { get; } = new();
}

internal sealed class LegacyJd2014SongDescEntry
{
    public int SerializedSize => 0x9C;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int LocaleCount => 3;

    public int Unknown2 => 0;

    public int HasInlineColor => 0;

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public uint InvalidValue => uint.MaxValue;

    public int Unknown6 => 0;

    public int Enabled => 1;
}

internal sealed class LegacySongDescVersionBlock
{
    public int Version => 6;

    public uint TypeId => 0x24A808D7;

    public float Unknown0 => LegacyFloat.FromBits(0x3E50D0D2u);

    public float Unknown1 => LegacyFloat.FromBits(0x3F57D7D9u);

    public float Unknown2 => 1.0f;
}

internal sealed class LegacySongDescCoachDefaults
{
    public uint CoachGroupTypeId => 0x9CD90BCB;

    public float ColorAlpha => 1.0f;

    public float ColorBlue => 1.0f;

    public float ColorGreen => 1.0f;

    public float ColorRed => 1.0f;

    public uint CoachLayoutTypeId => 0xA292C8C4;

    public float Unknown0 => LegacyFloat.FromBits(0x3F55D5D7u);

    public float Unknown1 => LegacyFloat.FromBits(0x3F119192u);

    public float Unknown2 => LegacyFloat.FromBits(0x3DF0F0F2u);

    public float Unknown3 => 1.0f;

    public float Unknown4 => LegacyFloat.FromBits(0xBE0B9923u);

    public float Unknown5 => LegacyFloat.FromBits(0x3F09898Au);

    public int Unknown6 => 0;

    public float Unknown7 => LegacyFloat.FromBits(0x3F028283u);

    public float Unknown8 => 1.0f;

    public uint CoachLayoutTypeId2 => 0xF5825C67;

    public float Unknown9 => LegacyFloat.FromBits(0x3F0F8F90u);

    public float Unknown10 => LegacyFloat.FromBits(0x3E28A8A9u);

    public float Unknown11 => LegacyFloat.FromBits(0x3DA0A0A1u);

    public float Unknown12 => 1.0f;

    public int Unknown13 => 0;

    public int Unknown14 => 0;
}

internal sealed class LegacyJd2014SongDescCoachDefaults
{
    public uint CoachGroupTypeId => 0x9CD90BCB;

    public float ColorAlpha => 1.0f;

    public float ColorBlue => 1.0f;

    public float ColorGreen => 1.0f;

    public float ColorRed => 1.0f;

    public int Footer => 0;
}