using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

internal sealed class LegacyTapeFile(string mapName, UbiArtEngineVersion engineVersion, IEnumerable<LegacyTapeClip> clips)
{
    private IReadOnlyList<LegacyTapeClip> ClipList { get; } = [.. clips];

    [LegacyBinaryField(0)]
    public uint Version => 1;

    [LegacyBinaryField(1)]
    public int TapeVersion => (224 * ClipList.Count) + 166;

    [LegacyBinaryField(2)]
    public uint TypeId => 0x9E845460;

    [LegacyBinaryField(3)]
    public uint TypeSize => (int)engineVersion == 2015 ? 0x8Cu : 0x9Cu;

    [LegacyBinaryField(4)]
    public int ClipCount => ClipList.Count;

    [LegacyBinaryField(5)]
    public IReadOnlyList<LegacyTapeClip> Clips => ClipList;

    [LegacyBinaryField(6)]
    public LegacyPadding FooterPadding => LegacyBinary.Padding((int)engineVersion == 2015 ? 8 : 12);

    [LegacyBinaryField(7)]
    public int TapeClock => 0;

    [LegacyBinaryField(8)]
    public int TapeBarCount => 1;

    [LegacyBinaryField(9)]
    public int FreeResourcesAfterPlay => 0;

    [LegacyBinaryField(10)]
    public string MapName => mapName;
}

internal abstract class LegacyTapeClip
{
    [LegacyBinaryField(0)]
    public abstract uint TypeId { get; }

    [LegacyBinaryField(1)]
    public abstract int SerializedSize { get; }

    [LegacyBinaryField(2)]
    public uint Id { get; init; }

    [LegacyBinaryField(3)]
    public uint TrackId { get; init; }

    [LegacyBinaryField(4)]
    public int IsActive { get; init; } = 1;

    [LegacyBinaryField(5)]
    public int StartTime { get; init; }

    [LegacyBinaryField(6)]
    public int Duration { get; init; }
}

internal sealed class LegacyMotionClip : LegacyTapeClip
{
    public override uint TypeId => 0x955384A1;
    public override int SerializedSize => 0x70;

    [LegacyBinaryField(7)]
    public LegacyUbiArtPath ClassifierPath { get; init; }

    [LegacyBinaryField(8)]
    public int Unknown0 => 0;

    [LegacyBinaryField(9)]
    public int GoldMove { get; init; }

    [LegacyBinaryField(10)]
    public int CoachId { get; init; }

    [LegacyBinaryField(11)]
    public int MoveType { get; init; }

    [LegacyBinaryField(12)]
    public LegacyAbgrColor Color { get; init; } = LegacyAbgrColor.White;

    [LegacyBinaryField(13)]
    public LegacyMotionPlatformSpecifics MotionPlatformSpecifics { get; } = new();
}

internal sealed class LegacyPictogramClip : LegacyTapeClip
{
    public override uint TypeId => 0x52EC8962;
    public override int SerializedSize => 0x38;

    [LegacyBinaryField(7)]
    public LegacyUbiArtPath PictoPath { get; init; }

    [LegacyBinaryField(8)]
    public int Unknown0 => 0;

    [LegacyBinaryField(9)]
    public uint CoachCount { get; init; } = uint.MaxValue;
}

internal sealed class LegacyGoldEffectClip : LegacyTapeClip
{
    public override uint TypeId => 0xFD69B110;
    public override int SerializedSize => 0x1C;

    [LegacyBinaryField(7)]
    public int EffectType { get; init; }
}

internal sealed class LegacyKaraokeClip : LegacyTapeClip
{
    public override uint TypeId => 0x68552A41;
    public override int SerializedSize => 0x50;

    [LegacyBinaryField(7)]
    public float Pitch { get; init; }

    [LegacyBinaryField(8)]
    public string Lyrics { get; init; } = string.Empty;

    [LegacyBinaryField(9)]
    public int IsEndOfLine { get; init; }

    [LegacyBinaryField(10)]
    public int ContentType { get; init; }

    [LegacyBinaryField(11)]
    public int StartTimeTolerance { get; init; } = 4;

    [LegacyBinaryField(12)]
    public int EndTimeTolerance { get; init; } = 4;

    [LegacyBinaryField(13)]
    public float SemitoneTolerance { get; init; } = 5;
}

internal sealed class LegacySoundSetClip : LegacyTapeClip
{
    public override uint TypeId => 0x2D8C885B;
    public override int SerializedSize => 0x40;

    [LegacyBinaryField(7)]
    public LegacyUbiArtPath SoundSetPath { get; init; }

    [LegacyBinaryField(8)]
    public int SoundChannel => 0;

    [LegacyBinaryField(9)]
    public int StopsOnEnd => 0;

    [LegacyBinaryField(10)]
    public int AccountedForDuration => 0;

    [LegacyBinaryField(11)]
    public int Padding => 0;
}

internal sealed class LegacyHideUserInterfaceClip(UbiArtEngineVersion engineVersion) : LegacyTapeClip
{
    public override uint TypeId => 0x52E06A9A;
    public override int SerializedSize => 0x48;

    [LegacyBinaryField(7)]
    public LegacyPadding VersionPadding => LegacyBinary.Padding((int)engineVersion == 2015 ? 4 : 8);

    [LegacyBinaryField(8)]
    public int EventType => 1;

    [LegacyBinaryField(9)]
    public int Padding => 0;
}

internal sealed class LegacyAbgrColor(float alpha, float blue, float green, float red)
{
    public static LegacyAbgrColor White { get; } = new(1, 1, 1, 1);

    [LegacyBinaryField(0)]
    public float Alpha => alpha;

    [LegacyBinaryField(1)]
    public float Blue => blue;

    [LegacyBinaryField(2)]
    public float Green => green;

    [LegacyBinaryField(3)]
    public float Red => red;
}

internal sealed class LegacyMotionPlatformSpecifics
{
    [LegacyBinaryField(0)]
    public IReadOnlyList<LegacyCameraScoringDefaults> Platforms { get; } =
    [
        new(),
        new(),
        new()
    ];

    [LegacyBinaryField(1)]
    public LegacyPadding Terminator => LegacyBinary.Padding(16);
}

internal sealed class LegacyCameraScoringDefaults
{
    [LegacyBinaryField(0)]
    public int Platform => 3;

    [LegacyBinaryField(1)]
    public int ScoreScale => 1;

    [LegacyBinaryField(2)]
    public int ScoreSmoothing => 0;

    [LegacyBinaryField(3)]
    public int Thresholds => 0;
}

internal sealed class LegacyResourceHeader(
    int serializedSize,
    uint componentTypeId,
    uint componentSize)
{
    [LegacyBinaryField(0)]
    public uint Version => 1;

    [LegacyBinaryField(1)]
    public int SerializedSize => serializedSize;

    [LegacyBinaryField(2)]
    public uint BaseTypeId => 0x1B857BCE;

    [LegacyBinaryField(3)]
    public uint BaseTypeSize => 0x6C;

    [LegacyBinaryField(4)]
    public LegacyPadding Reserved => LegacyBinary.Padding(28);

    [LegacyBinaryField(5)]
    public int ComponentCount => 1;

    [LegacyBinaryField(6)]
    public uint ComponentTypeId => componentTypeId;

    [LegacyBinaryField(7)]
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
    [LegacyBinaryField(0)]
    public LegacyResourceHeader Header => new(0x15A5, 0x8AC2B5C6u, 0xF4);

    [LegacyBinaryField(1)]
    public string MapName => mapName;

    [LegacyBinaryField(2)]
    public uint EngineVersion => (uint)engineVersion;

    [LegacyBinaryField(3)]
    public uint OriginalJustDanceVersion => originalJustDanceVersion;

    [LegacyBinaryField(4)]
    public int Unknown0 => 0;

    [LegacyBinaryField(5)]
    public LegacySongDescTags Tags { get; } = new();

    [LegacyBinaryField(6)]
    public string Artist => artist;

    [LegacyBinaryField(7)]
    public string DancerName => "Unknown Dancer";

    [LegacyBinaryField(8)]
    public string Title => title;

    [LegacyBinaryField(9)]
    public uint CoachCount => (uint)coachCount;

    [LegacyBinaryField(10)]
    public uint DefaultCoachId => uint.MaxValue;

    [LegacyBinaryField(11)]
    public uint Difficulty => difficulty;

    [LegacyBinaryField(12)]
    public int SweatDifficulty => 0;

    [LegacyBinaryField(13)]
    public int Status => 0;

    [LegacyBinaryField(14)]
    public int LocaleId => 1;

    [LegacyBinaryField(15)]
    public float TagScale => 0.5f;

    [LegacyBinaryField(16)]
    public int PreviewCount => 2;

    [LegacyBinaryField(17)]
    public int PreviewEntrySize => 0x10;

    [LegacyBinaryField(18)]
    public uint PreviewEntryTypeId => 0x6F4037D0;

    [LegacyBinaryField(19)]
    public int PreviewEntryBeat => previewEntryBeat;

    [LegacyBinaryField(20)]
    public int PreviewEntryPadding => 0;

    [LegacyBinaryField(21)]
    public int PreviewLoopSize => 0x10;

    [LegacyBinaryField(22)]
    public uint PreviewLoopTypeId => 0xB11FC1B6;

    [LegacyBinaryField(23)]
    public int PreviewLoopStartBeat => previewLoopStartBeat;

    [LegacyBinaryField(24)]
    public int PreviewLoopEndBeat => previewLoopEndBeat;

    [LegacyBinaryField(25)]
    public LegacySongDescVersionBlock? VersionBlock => (int)engineVersion >= 2015 ? new() : null;

    [LegacyBinaryField(26)]
    public float LyricColorIntensity => 1.0f;

    [LegacyBinaryField(27)]
    public uint LyricColorTypeId => 0x31D3B347;

    [LegacyBinaryField(28)]
    public LegacyAbgrColor LyricColor => lyricColor;

    [LegacyBinaryField(29)]
    public LegacySongDescCoachDefaults CoachDefaults { get; } = new();
}

internal sealed class LegacySongDescTags
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int SerializedSize => 0x58;

    [LegacyBinaryField(2)]
    public int Unknown0 => 0;

    [LegacyBinaryField(3)]
    public int Unknown1 => 0;

    [LegacyBinaryField(4)]
    public int TagCount => 7;

    [LegacyBinaryField(5)]
    public uint DefaultTag => uint.MaxValue;

    [LegacyBinaryField(6)]
    public int LocaleCount => 3;

    [LegacyBinaryField(7)]
    public int Unknown2 => 0;

    [LegacyBinaryField(8)]
    public int Unknown3 => 0;

    [LegacyBinaryField(9)]
    public int Unknown4 => 0;

    [LegacyBinaryField(10)]
    public int Unknown5 => 0;

    [LegacyBinaryField(11)]
    public uint DefaultLocale => uint.MaxValue;

    [LegacyBinaryField(12)]
    public int Unknown6 => 0;

    [LegacyBinaryField(13)]
    public int Enabled => 1;
}

internal sealed class LegacySongDescVersionBlock
{
    [LegacyBinaryField(0)]
    public int Version => 6;

    [LegacyBinaryField(1)]
    public uint TypeId => 0x24A808D7;

    [LegacyBinaryField(2)]
    public float Unknown0 => LegacyFloat.FromBits(0x3E50D0D2u);

    [LegacyBinaryField(3)]
    public float Unknown1 => LegacyFloat.FromBits(0x3F57D7D9u);

    [LegacyBinaryField(4)]
    public float Unknown2 => 1.0f;
}

internal sealed class LegacySongDescCoachDefaults
{
    [LegacyBinaryField(0)]
    public uint CoachGroupTypeId => 0x9CD90BCB;

    [LegacyBinaryField(1)]
    public float ColorAlpha => 1.0f;

    [LegacyBinaryField(2)]
    public float ColorBlue => 1.0f;

    [LegacyBinaryField(3)]
    public float ColorGreen => 1.0f;

    [LegacyBinaryField(4)]
    public float ColorRed => 1.0f;

    [LegacyBinaryField(5)]
    public uint CoachLayoutTypeId => 0xA292C8C4;

    [LegacyBinaryField(6)]
    public float Unknown0 => LegacyFloat.FromBits(0x3F55D5D7u);

    [LegacyBinaryField(7)]
    public float Unknown1 => LegacyFloat.FromBits(0x3F119192u);

    [LegacyBinaryField(8)]
    public float Unknown2 => LegacyFloat.FromBits(0x3DF0F0F2u);

    [LegacyBinaryField(9)]
    public float Unknown3 => 1.0f;

    [LegacyBinaryField(10)]
    public float Unknown4 => LegacyFloat.FromBits(0xBE0B9923u);

    [LegacyBinaryField(11)]
    public float Unknown5 => LegacyFloat.FromBits(0x3F09898Au);

    [LegacyBinaryField(12)]
    public int Unknown6 => 0;

    [LegacyBinaryField(13)]
    public float Unknown7 => LegacyFloat.FromBits(0x3F028283u);

    [LegacyBinaryField(14)]
    public float Unknown8 => 1.0f;

    [LegacyBinaryField(15)]
    public uint CoachLayoutTypeId2 => 0xF5825C67;

    [LegacyBinaryField(16)]
    public float Unknown9 => LegacyFloat.FromBits(0x3F0F8F90u);

    [LegacyBinaryField(17)]
    public float Unknown10 => LegacyFloat.FromBits(0x3E28A8A9u);

    [LegacyBinaryField(18)]
    public float Unknown11 => LegacyFloat.FromBits(0x3DA0A0A1u);

    [LegacyBinaryField(19)]
    public float Unknown12 => 1.0f;

    [LegacyBinaryField(20)]
    public int Unknown13 => 0;

    [LegacyBinaryField(21)]
    public int Unknown14 => 0;
}

internal sealed class LegacyTapeCaseFile(string mapName, string tapeType)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string Extension => tapeType == "dance" ? "dtape" : "ktape";

    [LegacyBinaryField(0)]
    public LegacyResourceHeader Header => new(tapeType == "dance" ? 0xFE : 0x100, 0x8229ABC3, 0x40);

    [LegacyBinaryField(1)]
    public int One0 => 1;

    [LegacyBinaryField(2)]
    public int StructSize => 0x10;

    [LegacyBinaryField(3)]
    public int One1 => 1;

    [LegacyBinaryField(4)]
    public int TapeReferenceSize => 0x28;

    [LegacyBinaryField(5)]
    public uint TapeReferenceTypeId => tapeType == "dance" ? 0x24A37BF0u : 0xFD4547ACu;

    [LegacyBinaryField(6)]
    public LegacyUbiArtPath TapePath => LegacyBinary.Path($"{MapNameLower}_tml_{tapeType}.{Extension}", $"world/maps/{MapNameLower}/timeline/");

    [LegacyBinaryField(7)]
    public int Padding => 0;
}

internal sealed class LegacySequenceTplFile
{
    [LegacyBinaryField(0)]
    public LegacyResourceHeader Header => new(0xB0, 0x8229ABC3u, 0x40);

    [LegacyBinaryField(1)]
    public int Padding => 0;
}

internal sealed class LegacySoundTapeFile(string mapName)
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int SerializedSize => 0xA2;

    [LegacyBinaryField(2)]
    public uint TapeTypeId => 0x9E845460;

    [LegacyBinaryField(3)]
    public int TapeTypeSize => 0x9C;

    [LegacyBinaryField(4)]
    public LegacyPadding Reserved => LegacyBinary.Padding(20);

    [LegacyBinaryField(5)]
    public int ClipCount => 1;

    [LegacyBinaryField(6)]
    public int TapeClock => 0;

    [LegacyBinaryField(7)]
    public string MapName => mapName;
}

internal sealed class LegacyAmbTplFile(string mapName)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string AmbName => $"amb_{MapNameLower}_intro";

    [LegacyBinaryField(0)]
    public LegacyResourceHeader Header => new(0x2B0, 0xD94D6C53u, 0x118);

    [LegacyBinaryField(1)]
    public int SoundCount => 1;

    [LegacyBinaryField(2)]
    public int SoundDescriptorSize => 0xF8;

    [LegacyBinaryField(3)]
    public uint SoundDescriptorTypeId => 0xFEC0F184;

    [LegacyBinaryField(4)]
    public float Volume => -3.0f;

    [LegacyBinaryField(5)]
    public uint SoundParamsTypeId => 0xEB537A60;

    [LegacyBinaryField(6)]
    public uint LimitMode => uint.MaxValue;

    [LegacyBinaryField(7)]
    public int Category => 0;

    [LegacyBinaryField(8)]
    public uint BusId => uint.MaxValue;

    [LegacyBinaryField(9)]
    public int FadeInTime => 0;

    [LegacyBinaryField(10)]
    public int FadeOutTime => 0;

    [LegacyBinaryField(11)]
    public int FileCount => 1;

    [LegacyBinaryField(12)]
    public LegacyUbiArtPath AudioPath => LegacyBinary.Path($"{AmbName}.wav", $"world/maps/{MapNameLower}/audio/amb/");

    [LegacyBinaryField(13)]
    public LegacyPadding FilePadding => LegacyBinary.Padding(24);

    [LegacyBinaryField(14)]
    public int ParamsSize => 0x60;

    [LegacyBinaryField(15)]
    public int NumChannels => 2;

    [LegacyBinaryField(16)]
    public int Loop => 0;

    [LegacyBinaryField(17)]
    public int PlayMode => 1;

    [LegacyBinaryField(18)]
    public uint RandomMode => uint.MaxValue;

    [LegacyBinaryField(19)]
    public int RandomVolMin => 0;

    [LegacyBinaryField(20)]
    public int RandomVolMax => 0;

    [LegacyBinaryField(21)]
    public int RandomPitchMin => 0;

    [LegacyBinaryField(22)]
    public int RandomPitchMax => 0;

    [LegacyBinaryField(23)]
    public float VolumeMultiplierMin => 1.0f;

    [LegacyBinaryField(24)]
    public float VolumeMultiplierMax => 1.0f;

    [LegacyBinaryField(25)]
    public int LowPass => 0;

    [LegacyBinaryField(26)]
    public int HighPass => 0;

    [LegacyBinaryField(27)]
    public int Reverb => 0;

    [LegacyBinaryField(28)]
    public int ChannelMode => 2;

    [LegacyBinaryField(29)]
    public uint Unknown0 => uint.MaxValue;

    [LegacyBinaryField(30)]
    public uint Unknown1 => uint.MaxValue;

    [LegacyBinaryField(31)]
    public int Unknown2 => 0;

    [LegacyBinaryField(32)]
    public int Unknown3 => 0;

    [LegacyBinaryField(33)]
    public int Unknown4 => 0;

    [LegacyBinaryField(34)]
    public int Unknown5 => 0;

    [LegacyBinaryField(35)]
    public uint Unknown6 => uint.MaxValue;

    [LegacyBinaryField(36)]
    public int Footer => 0;
}

internal sealed class LegacyMainSequenceTplFile(string mapName)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    [LegacyBinaryField(0)]
    public LegacyResourceHeader Header => new(0x101, 0x0C736497u, 0x40);

    [LegacyBinaryField(1)]
    public int One0 => 1;

    [LegacyBinaryField(2)]
    public int StructSize => 0x10;

    [LegacyBinaryField(3)]
    public int One1 => 1;

    [LegacyBinaryField(4)]
    public int TapeReferenceSize => 0x28;

    [LegacyBinaryField(5)]
    public uint TapeReferenceTypeId => 0xABF3773E;

    [LegacyBinaryField(6)]
    public LegacyUbiArtPath TapePath => LegacyBinary.Path($"{MapNameLower}_mainsequence.tape", $"world/maps/{MapNameLower}/cinematics/");

    [LegacyBinaryField(7)]
    public int Padding => 0;
}

internal sealed class LegacySgsFile
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public uint TypeId => 0xCE018EDB;

    [LegacyBinaryField(2)]
    public int Unknown0 => 0;

    [LegacyBinaryField(3)]
    public int GraphCount => 6;

    [LegacyBinaryField(4)]
    public int Unknown1 => 1;

    [LegacyBinaryField(5)]
    public int Unknown2 => 2;

    [LegacyBinaryField(6)]
    public int Unknown3 => 0;

    [LegacyBinaryField(7)]
    public int Unknown4 => 0;

    [LegacyBinaryField(8)]
    public int Unknown5 => 0;
}

internal sealed class LegacyMusicTrackFile(
    string mapName,
    UbiArtEngineVersion engineVersion,
    IReadOnlyList<int> markers,
    IReadOnlyList<LegacySignatureMarker> signatures,
    IReadOnlyList<LegacySectionMarker> sections,
    int startBeat,
    uint endBeat,
    float videoStartTime)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    [LegacyBinaryField(0)]
    public LegacyMusicTrackHeader Header => new(markers.Count);

    [LegacyBinaryField(1)]
    public int MarkerCount => markers.Count;

    [LegacyBinaryField(2)]
    public IReadOnlyList<int> Markers => markers;

    [LegacyBinaryField(3)]
    public int SignatureCount => signatures.Count;

    [LegacyBinaryField(4)]
    public IReadOnlyList<LegacySignatureMarker> Signatures => signatures;

    [LegacyBinaryField(5)]
    public int SectionCount => sections.Count;

    [LegacyBinaryField(6)]
    public IReadOnlyList<LegacySectionMarker> Sections => sections;

    [LegacyBinaryField(7)]
    public LegacyMusicTrackTiming Timing => new(engineVersion, startBeat, endBeat, videoStartTime);

    [LegacyBinaryField(8)]
    public LegacyUbiArtPath AudioPath => LegacyBinary.Path($"{MapNameLower}.wav", $"world/maps/{MapNameLower}/audio/");

    [LegacyBinaryField(9)]
    public LegacyPadding Footer => LegacyBinary.Padding(8);
}

internal sealed class LegacyMusicTrackHeader(int markerCount)
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int SerializedSize => (166 * markerCount) + 166;

    [LegacyBinaryField(2)]
    public uint BaseTypeId => 0x1B857BCE;

    [LegacyBinaryField(3)]
    public uint BaseTypeSize => 0x6C;

    [LegacyBinaryField(4)]
    public LegacyPadding Reserved => LegacyBinary.Padding(28);

    [LegacyBinaryField(5)]
    public int ComponentCount => 1;

    [LegacyBinaryField(6)]
    public uint ComponentTypeId => 0x02883A7E;

    [LegacyBinaryField(7)]
    public uint ComponentSize => 0xA0;

    [LegacyBinaryField(8)]
    public uint StructureSize => 0x90;

    [LegacyBinaryField(9)]
    public uint MarkerListSize => 0x6C;
}

internal sealed class LegacyMusicTrackTiming(
    UbiArtEngineVersion engineVersion,
    int startBeat,
    uint endBeat,
    float videoStartTime)
{
    [LegacyBinaryField(0)]
    public int StartBeat => startBeat;

    [LegacyBinaryField(1)]
    public uint EndBeat => endBeat;

    [LegacyBinaryField(2)]
    public LegacyPadding? ModernPadding => (int)engineVersion >= 2018 ? LegacyBinary.Padding(10) : null;

    [LegacyBinaryField(3)]
    public float VideoStartTime => videoStartTime;

    [LegacyBinaryField(4)]
    public LegacyPadding FooterPadding => (int)engineVersion >= 2018 ? LegacyBinary.Padding(20) : LegacyBinary.Padding(4);
}

internal sealed class LegacySignatureMarker(int marker, int beats)
{
    [LegacyBinaryField(0)]
    public int Size => 8;

    [LegacyBinaryField(1)]
    public int Marker => marker;

    [LegacyBinaryField(2)]
    public int Beats => beats;
}

internal sealed class LegacySectionMarker(int startBeat, int sectionType, string comment)
{
    [LegacyBinaryField(0)]
    public int Size => 0x14;

    [LegacyBinaryField(1)]
    public int StartBeat => startBeat;

    [LegacyBinaryField(2)]
    public int SectionType => sectionType;

    [LegacyBinaryField(3)]
    public string Comment => comment;
}

internal sealed class LegacySceneFile(uint sceneId, IEnumerable<object?> actors, object? footer = null, int? actorCount = null)
{
    private IReadOnlyList<object?> ActorList { get; } = [.. actors];

    [LegacyBinaryField(0)]
    public LegacySceneHeader Header => new(sceneId, actorCount ?? ActorList.Count);

    [LegacyBinaryField(1)]
    public IReadOnlyList<object?> Actors => ActorList;

    [LegacyBinaryField(2)]
    public object? Footer => footer;
}

internal sealed class LegacySceneHeader(uint sceneId, int actorCount)
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public uint SceneId => sceneId;

    [LegacyBinaryField(2)]
    public LegacyPadding Reserved => LegacyBinary.Padding(15);

    [LegacyBinaryField(3)]
    public byte ActorCount => (byte)actorCount;
}

internal sealed class LegacyMainSceneFooter
{
    [LegacyBinaryField(0)]
    public int Unknown0 => 0;

    [LegacyBinaryField(1)]
    public int SettingsTypePart0 => 0x0001CE01;

    [LegacyBinaryField(2)]
    public uint SettingsTypePart1 => 0x8EDB0000;

    [LegacyBinaryField(3)]
    public int Unknown1 => 0;

    [LegacyBinaryField(4)]
    public int Unknown2 => 0x00060000;

    [LegacyBinaryField(5)]
    public int Unknown3 => 0x00010000;

    [LegacyBinaryField(6)]
    public int Unknown4 => 0x00020000;

    [LegacyBinaryField(7)]
    public int Unknown5 => 0;

    [LegacyBinaryField(8)]
    public int Unknown6 => 0;

    [LegacyBinaryField(9)]
    public LegacyPadding Padding => LegacyBinary.Padding(6);
}

internal sealed class LegacySingleActorSceneFile(string mapName, string suffix, string folder, string extension)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string ActorFolder => string.IsNullOrEmpty(folder)
        ? $"world/maps/{MapNameLower}/"
        : $"world/maps/{MapNameLower}/{folder}/";

    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public uint TypeId => 0x2DC82C5F;

    [LegacyBinaryField(2)]
    public int SerializedSize => 0x5C;

    [LegacyBinaryField(3)]
    public string ActorFileName => $"{MapNameLower}_{suffix}.{extension}";

    [LegacyBinaryField(4)]
    public string ActorPath => ActorFolder;

    [LegacyBinaryField(5)]
    public int ActorCount => 1;

    [LegacyBinaryField(6)]
    public LegacyPadding Padding => LegacyBinary.Padding(44);

    [LegacyBinaryField(7)]
    public int FooterVersion => 1;

    [LegacyBinaryField(8)]
    public uint FooterTypeId => 0xF466D41A;

    [LegacyBinaryField(9)]
    public int Footer => 0;
}

internal sealed class LegacySubSceneDefinitionActor(
    string mapName,
    string mapNameLower,
    string suffix,
    string folder,
    int viewType)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x4FA40F09;

    [LegacyBinaryField(1)]
    public int RelativeZ => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Angle => 0;

    [LegacyBinaryField(5)]
    public string FriendlyName => $"{mapName}{suffix}";

    [LegacyBinaryField(6)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(7)]
    public int PosX => 0;

    [LegacyBinaryField(8)]
    public int PosY => 0;

    [LegacyBinaryField(9)]
    public int Unknown0 => 0;

    [LegacyBinaryField(10)]
    public int Unknown1 => 0;

    [LegacyBinaryField(11)]
    public int Unknown2 => 0;

    [LegacyBinaryField(12)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown3 => 0;

    [LegacyBinaryField(14)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("subscene.tpl", "enginedata/actortemplates/");

    [LegacyBinaryField(15)]
    public LegacyPadding TemplatePadding => LegacyBinary.Padding(12);

    [LegacyBinaryField(16)]
    public LegacyUbiArtPath ScenePath => LegacyBinary.Path($"{mapNameLower}{suffix.ToLowerInvariant()}.isc", $"world/maps/{mapNameLower}/{folder}/");

    [LegacyBinaryField(17)]
    public int EmbedScene => 0;

    [LegacyBinaryField(18)]
    public int IsSinglePiece => 1;

    [LegacyBinaryField(19)]
    public int ZForced => 0;

    [LegacyBinaryField(20)]
    public int DirectPicking => 1;

    [LegacyBinaryField(21)]
    public int IgnoreSave => 1;

    [LegacyBinaryField(22)]
    public int Unknown4 => 0;

    [LegacyBinaryField(23)]
    public int ViewType => viewType;
}

internal sealed class LegacySongDescSceneActor(string mapName, string mapNameLower)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x97CA628B;

    [LegacyBinaryField(1)]
    public int RelativeZ => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Angle => 0;

    [LegacyBinaryField(5)]
    public string FriendlyName => $"{mapName} : Template Artist - Template Title.JDVer = 5, ID = 842776738, Type = 1 (Flags 0x00000000), NbCoach = 2, Difficulty = 2";

    [LegacyBinaryField(6)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(7)]
    public uint PosX => 0xC0620BE5;

    [LegacyBinaryField(8)]
    public uint PosY => 0xBFBE1F08;

    [LegacyBinaryField(9)]
    public int Unknown0 => 0;

    [LegacyBinaryField(10)]
    public int Unknown1 => 0;

    [LegacyBinaryField(11)]
    public int Unknown2 => 0;

    [LegacyBinaryField(12)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown3 => 0;

    [LegacyBinaryField(14)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("songdesc.main_legacy.tpl", $"cache/legacyconverteddata/{mapNameLower}/");

    [LegacyBinaryField(15)]
    public int ComponentCount => 2;

    [LegacyBinaryField(16)]
    public int Unknown4 => 0;

    [LegacyBinaryField(17)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(18)]
    public uint ComponentTypeId => 0xE07FCC3F;
}

internal sealed class LegacyComponentActor(
    string name,
    object preData,
    object postData,
    string templateFileName,
    string templateFolder,
    object tail)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x97CA628B;

    [LegacyBinaryField(1)]
    public object PreData => preData;

    [LegacyBinaryField(2)]
    public string Name => name;

    [LegacyBinaryField(3)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(4)]
    public object PostData => postData;

    [LegacyBinaryField(5)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(6)]
    public int Unknown0 => 0;

    [LegacyBinaryField(7)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path(templateFileName, templateFolder);

    [LegacyBinaryField(8)]
    public object Tail => tail;
}

internal sealed class LegacyEmbeddedSubSceneActor(string name, string templateFileName, string templateFolder)
{
    [LegacyBinaryField(0)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(1)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(2)]
    public int Angle => 0;

    [LegacyBinaryField(3)]
    public string Name => name;

    [LegacyBinaryField(4)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(5)]
    public float PosX => LegacyFloat.FromBits(0xBBC9C90Cu);

    [LegacyBinaryField(6)]
    public float PosY => LegacyFloat.FromBits(0xBBC9C90Cu);

    [LegacyBinaryField(7)]
    public int Unknown0 => 0;

    [LegacyBinaryField(8)]
    public int Unknown1 => 0;

    [LegacyBinaryField(9)]
    public int Unknown2 => 0;

    [LegacyBinaryField(10)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(11)]
    public int Unknown3 => 0;

    [LegacyBinaryField(12)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path(templateFileName, templateFolder);

    [LegacyBinaryField(13)]
    public int Unknown4 => 0;

    [LegacyBinaryField(14)]
    public int Unknown5 => 0;

    [LegacyBinaryField(15)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(16)]
    public uint ComponentTypeId => 0x231F27DE;

    [LegacyBinaryField(17)]
    public LegacyPadding Padding => LegacyBinary.Padding(16);
}

internal sealed class LegacyVideoScreenActor(string mapNameLower, bool embedded = false)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x97CA628B;

    [LegacyBinaryField(1)]
    public float RelativeZ => -1.0f;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Angle => 0;

    [LegacyBinaryField(5)]
    public string Name => "VideoScreen";

    [LegacyBinaryField(6)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(7)]
    public int PosX => 0;

    [LegacyBinaryField(8)]
    public float PosY => -4.5f;

    [LegacyBinaryField(9)]
    public int Unknown0 => 0;

    [LegacyBinaryField(10)]
    public int Unknown1 => 0;

    [LegacyBinaryField(11)]
    public int Unknown2 => 0;

    [LegacyBinaryField(12)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown3 => 0;

    [LegacyBinaryField(14)]
    public LegacyUbiArtPath TemplatePath => embedded
        ? LegacyBinary.Path("video_player_main.tpl", "world/_common/videoscreen/", 0xF5D5E8F2)
        : LegacyBinary.Path("video_player_main.tpl", "world/_common/videoscreen/");

    [LegacyBinaryField(15)]
    public int Unknown4 => 0;

    [LegacyBinaryField(16)]
    public int Unknown5 => 0;

    [LegacyBinaryField(17)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(18)]
    public uint ComponentTypeId => 0x1263DAD9;

    [LegacyBinaryField(19)]
    public LegacyUbiArtPath VideoPath => LegacyBinary.Path($"{mapNameLower}.webm", $"world/maps/{mapNameLower}/videoscoach/");

    [LegacyBinaryField(20)]
    public LegacyPadding Padding => LegacyBinary.Padding(8);
}

internal sealed class LegacyVideoOutputActor(bool embedded = false)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x97CA628B;

    [LegacyBinaryField(1)]
    public int RelativeZ => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => embedded ? 1.0f : LegacyFloat.FromBits(0x407C3D3Eu);

    [LegacyBinaryField(3)]
    public float ScaleY => embedded ? 1.0f : LegacyFloat.FromBits(0x400E147Bu);

    [LegacyBinaryField(4)]
    public int Angle => 0;

    [LegacyBinaryField(5)]
    public string Name => "VideoOutput";

    [LegacyBinaryField(6)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(7)]
    public int PosX => 0;

    [LegacyBinaryField(8)]
    public int PosY => 0;

    [LegacyBinaryField(9)]
    public int Unknown0 => 0;

    [LegacyBinaryField(10)]
    public int Unknown1 => 0;

    [LegacyBinaryField(11)]
    public int Unknown2 => 0;

    [LegacyBinaryField(12)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown3 => 0;

    [LegacyBinaryField(14)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("video_output_main.tpl", "world/_common/videoscreen/");

    [LegacyBinaryField(15)]
    public int Unknown4 => embedded ? 2 : 0;

    [LegacyBinaryField(16)]
    public int Unknown5 => 0;

    [LegacyBinaryField(17)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(18)]
    public uint ComponentTypeId => 0x0579E81B;

    [LegacyBinaryField(19)]
    public LegacyVideoOutputMaterialConfig Material { get; } = new(embedded);
}

internal sealed class LegacyVideoOutputMaterialConfig(bool embedded = false)
{
    [LegacyBinaryField(0)]
    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    [LegacyBinaryField(1)]
    public int Unknown0 => 0;

    [LegacyBinaryField(2)]
    public int Unknown1 => 0;

    [LegacyBinaryField(3)]
    public int Unknown2 => 0;

    [LegacyBinaryField(4)]
    public int Unknown3 => 0;

    [LegacyBinaryField(5)]
    public uint BlendMode => uint.MaxValue;

    [LegacyBinaryField(6)]
    public int Unknown4 => 0;

    [LegacyBinaryField(7)]
    public int MaterialType => 1;

    [LegacyBinaryField(8)]
    public int Unknown5 => 0;

    [LegacyBinaryField(9)]
    public int Unknown6 => 0;

    [LegacyBinaryField(10)]
    public int Unknown7 => 0;

    [LegacyBinaryField(11)]
    public int Unknown8 => 0;

    [LegacyBinaryField(12)]
    public IReadOnlyList<LegacyVideoOutputLayerConfig> Layers { get; } =
    [
        new(), new(), new(), new(), new(), new(), new(), new()
    ];

    [LegacyBinaryField(13)]
    public uint Unknown9 => uint.MaxValue;

    [LegacyBinaryField(14)]
    public int Unknown10 => 0;

    [LegacyBinaryField(15)]
    public int Unknown11 => 0;

    [LegacyBinaryField(16)]
    public int Unknown12 => 0;

    [LegacyBinaryField(17)]
    public int Unknown13 => 0;

    [LegacyBinaryField(18)]
    public uint Unknown14 => uint.MaxValue;

    [LegacyBinaryField(19)]
    public int Unknown15 => 0;

    [LegacyBinaryField(20)]
    public string ShaderFileName => "pleofullscreen.msh";

    [LegacyBinaryField(21)]
    public string ShaderFolder => "world/_common/matshader/";

    [LegacyBinaryField(22)]
    public uint ShaderId => embedded ? 0xCFBBFE7Au : 0x6A06E805;

    [LegacyBinaryField(23)]
    public int Unknown16 => 0;

    [LegacyBinaryField(24)]
    public int Unknown17 => 0;

    [LegacyBinaryField(25)]
    public int Unknown18 => 0;

    [LegacyBinaryField(26)]
    public uint Unknown19 => uint.MaxValue;

    [LegacyBinaryField(27)]
    public uint Unknown20 => uint.MaxValue;

    [LegacyBinaryField(28)]
    public int Unknown21 => 0;

    [LegacyBinaryField(29)]
    public int Unknown22 => 0;

    [LegacyBinaryField(30)]
    public int Unknown23 => 0;

    [LegacyBinaryField(31)]
    public float Unknown24 => 1.0f;

    [LegacyBinaryField(32)]
    public int Unknown25 => 0;

    [LegacyBinaryField(33)]
    public int Unknown26 => 0;

    [LegacyBinaryField(34)]
    public int Unknown27 => 1;

    [LegacyBinaryField(35)]
    public LegacyPadding Padding => LegacyBinary.Padding(20);
}

internal sealed class LegacyVideoOutputLayerConfig
{
    [LegacyBinaryField(0)]
    public uint Unknown0 => uint.MaxValue;

    [LegacyBinaryField(1)]
    public int Unknown1 => 0;

    [LegacyBinaryField(2)]
    public int Unknown2 => 0;

    [LegacyBinaryField(3)]
    public int Unknown3 => 0;
}

internal sealed class LegacyMenuArtSceneActor(
    string mapName,
    string mapNameLower,
    string suffix,
    uint bounds0,
    uint bounds1)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x97CA628B;

    [LegacyBinaryField(1)]
    public int RelativeZ => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 0.3f;

    [LegacyBinaryField(3)]
    public float ScaleY => 0.3f;

    [LegacyBinaryField(4)]
    public int Angle => 0;

    [LegacyBinaryField(5)]
    public string Name => $"{mapName}_{suffix}";

    [LegacyBinaryField(6)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(7)]
    public uint Bounds0 => bounds0;

    [LegacyBinaryField(8)]
    public uint Bounds1 => bounds1;

    [LegacyBinaryField(9)]
    public int Unknown0 => 0;

    [LegacyBinaryField(10)]
    public int Unknown1 => 0;

    [LegacyBinaryField(11)]
    public int Unknown2 => 0;

    [LegacyBinaryField(12)]
    public uint InstanceId => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown3 => 0;

    [LegacyBinaryField(14)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("tpl_materialgraphiccomponent2d.tpl", "enginedata/actortemplates/");

    [LegacyBinaryField(15)]
    public int Unknown4 => 0;

    [LegacyBinaryField(16)]
    public int Unknown5 => 0;

    [LegacyBinaryField(17)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(18)]
    public uint ComponentTypeId => 0x72B61FC5;

    [LegacyBinaryField(19)]
    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    [LegacyBinaryField(20)]
    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    [LegacyBinaryField(21)]
    public uint BlendMode => uint.MaxValue;

    [LegacyBinaryField(22)]
    public int Unknown6 => 0;

    [LegacyBinaryField(23)]
    public int MaterialType => 1;

    [LegacyBinaryField(24)]
    public int Unknown7 => 0;

    [LegacyBinaryField(25)]
    public int Unknown8 => 0;

    [LegacyBinaryField(26)]
    public LegacyUbiArtPath TexturePath => LegacyBinary.Path($"{mapNameLower}_{suffix}.tga", $"world/maps/{mapNameLower}/menuart/textures/");

    [LegacyBinaryField(27)]
    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    [LegacyBinaryField(28)]
    public LegacyUbiArtPath ShaderPath => LegacyBinary.Path("multitexture_1layer.msh", "world/_common/matshader/", 0xD7E7D9C7);

    [LegacyBinaryField(29)]
    public int Unknown9 => 0;

    [LegacyBinaryField(30)]
    public int Unknown10 => 0;

    [LegacyBinaryField(31)]
    public int Unknown11 => 0;

    [LegacyBinaryField(32)]
    public uint Unknown12 => uint.MaxValue;

    [LegacyBinaryField(33)]
    public uint Unknown13 => uint.MaxValue;

    [LegacyBinaryField(34)]
    public int Unknown14 => 0;

    [LegacyBinaryField(35)]
    public int Unknown15 => 0;

    [LegacyBinaryField(36)]
    public int Unknown16 => 0;

    [LegacyBinaryField(37)]
    public float Unknown17 => 1.0f;

    [LegacyBinaryField(38)]
    public int Unknown18 => 0;

    [LegacyBinaryField(39)]
    public int Unknown19 => 0;

    [LegacyBinaryField(40)]
    public int Footer => 1;
}

internal sealed class LegacyCoverActor(
    string name,
    string mapNameLower,
    string textureFile,
    object preData,
    object postData,
    object footer)
{
    [LegacyBinaryField(0)]
    public uint TypeId => 0x97CA628B;

    [LegacyBinaryField(1)]
    public object PreData => preData;

    [LegacyBinaryField(2)]
    public string Name => name;

    [LegacyBinaryField(3)]
    public uint Marker => uint.MaxValue;

    [LegacyBinaryField(4)]
    public object PostData => postData;

    [LegacyBinaryField(5)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("tpl_materialgraphiccomponent2d.tpl", "enginedata/actortemplates/", 0xB4A817A8);

    [LegacyBinaryField(6)]
    public int Unknown0 => 0;

    [LegacyBinaryField(7)]
    public int Unknown1 => 0;

    [LegacyBinaryField(8)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(9)]
    public uint ComponentTypeId => 0x72B61FC5;

    [LegacyBinaryField(10)]
    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    [LegacyBinaryField(11)]
    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    [LegacyBinaryField(12)]
    public uint BlendMode => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown2 => 0;

    [LegacyBinaryField(14)]
    public int MaterialType => 1;

    [LegacyBinaryField(15)]
    public LegacyPadding TexturePadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(16)]
    public LegacyUbiArtPath TexturePath => LegacyBinary.Path(textureFile, $"world/maps/{mapNameLower}/menuart/textures/");

    [LegacyBinaryField(17)]
    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    [LegacyBinaryField(18)]
    public LegacyUbiArtPath ShaderPath => LegacyBinary.Path("multitexture_1layer.msh", "world/_common/matshader/");

    [LegacyBinaryField(19)]
    public int Unknown3 => 0;

    [LegacyBinaryField(20)]
    public int Unknown4 => 0;

    [LegacyBinaryField(21)]
    public int Unknown5 => 0;

    [LegacyBinaryField(22)]
    public uint Unknown6 => uint.MaxValue;

    [LegacyBinaryField(23)]
    public uint Unknown7 => uint.MaxValue;

    [LegacyBinaryField(24)]
    public int Unknown8 => 0;

    [LegacyBinaryField(25)]
    public int Unknown9 => 0;

    [LegacyBinaryField(26)]
    public int Unknown10 => 0;

    [LegacyBinaryField(27)]
    public float Unknown11 => 1.0f;

    [LegacyBinaryField(28)]
    public object Footer => footer;
}

internal sealed class LegacyActorHeader
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int Unknown0 => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Rotation => 0;

    [LegacyBinaryField(5)]
    public int X => 0;

    [LegacyBinaryField(6)]
    public uint ParentId => uint.MaxValue;

    [LegacyBinaryField(7)]
    public int Z => 0;

    [LegacyBinaryField(8)]
    public int Unknown1 => 0;

    [LegacyBinaryField(9)]
    public int Unknown2 => 0;

    [LegacyBinaryField(10)]
    public int Unknown3 => 0;

    [LegacyBinaryField(11)]
    public int Unknown4 => 0;

    [LegacyBinaryField(12)]
    public uint Unknown5 => uint.MaxValue;

    [LegacyBinaryField(13)]
    public int Unknown6 => 0;
}

internal sealed class LegacyGenericActorFile(string luaPath)
{
    [LegacyBinaryField(0)]
    public LegacyActorHeader Header { get; } = new();

    [LegacyBinaryField(1)]
    public LegacyUbiArtPath TemplatePath => LegacyUbiArtPath.FromFullPath(luaPath);

    [LegacyBinaryField(2)]
    public int ComponentCount => 2;

    [LegacyBinaryField(3)]
    public int Unknown0 => 0;

    [LegacyBinaryField(4)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(5)]
    public uint ComponentTypeId => 0xE07FCC3F;
}

internal sealed class LegacyVideoPlayerActorFile(string mapName)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    [LegacyBinaryField(0)]
    public LegacyActorHeader Header { get; } = new();

    [LegacyBinaryField(1)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("video_player_main.tpl", "world/_common/videoscreen/");

    [LegacyBinaryField(2)]
    public int Unknown0 => 0;

    [LegacyBinaryField(3)]
    public int Unknown1 => 0;

    [LegacyBinaryField(4)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(5)]
    public uint ComponentTypeId => 0x1263DAD9;

    [LegacyBinaryField(6)]
    public LegacyUbiArtPath VideoPath => LegacyBinary.Path($"{MapNameLower}.webm", $"world/maps/{MapNameLower}/videoscoach/");

    [LegacyBinaryField(7)]
    public LegacyPadding Padding => LegacyBinary.Padding(8);
}

internal sealed class LegacyMpdFile
{
    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int TypeId => 0x00424BAE;

    [LegacyBinaryField(2)]
    public byte DataSize => 0x14;

    [LegacyBinaryField(3)]
    public float Scale => 1.0f;

    [LegacyBinaryField(4)]
    public int Footer => 0;
}

internal sealed class LegacyAutodanceActorFile(string mapName)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int Unknown0 => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Unknown1 => 0;

    [LegacyBinaryField(5)]
    public int Unknown2 => 0;

    [LegacyBinaryField(6)]
    public int Unknown3 => 0;

    [LegacyBinaryField(7)]
    public int Unknown4 => 0;

    [LegacyBinaryField(8)]
    public int Unknown5 => 0;

    [LegacyBinaryField(9)]
    public int Unknown6 => 0;

    [LegacyBinaryField(10)]
    public uint Unknown7 => uint.MaxValue;

    [LegacyBinaryField(11)]
    public int Unknown8 => 0;

    [LegacyBinaryField(12)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path($"{MapNameLower}_autodance.tpl", $"world/maps/{MapNameLower}/autodance/");

    [LegacyBinaryField(13)]
    public int Unknown9 => 0;

    [LegacyBinaryField(14)]
    public int Unknown10 => 0;

    [LegacyBinaryField(15)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(16)]
    public uint ComponentTypeId => 0x677B269B;
}

internal sealed class LegacyMenuArtActorFile(string textureName, string mapName)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private int TextureType => textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase) ? 1 : 6;

    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int Unknown0 => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Rotation => 0;

    [LegacyBinaryField(5)]
    public int Unknown1 => 0;

    [LegacyBinaryField(6)]
    public uint ParentId => uint.MaxValue;

    [LegacyBinaryField(7)]
    public int Unknown2 => 0;

    [LegacyBinaryField(8)]
    public LegacyPadding TransformPadding => LegacyBinary.Padding(16);

    [LegacyBinaryField(9)]
    public uint Unknown3 => uint.MaxValue;

    [LegacyBinaryField(10)]
    public int Unknown4 => 0;

    [LegacyBinaryField(11)]
    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("tpl_materialgraphiccomponent2d.tpl", "enginedata/actortemplates/");

    [LegacyBinaryField(12)]
    public int Unknown5 => 0;

    [LegacyBinaryField(13)]
    public int Unknown6 => 0;

    [LegacyBinaryField(14)]
    public int ComponentVersion => 1;

    [LegacyBinaryField(15)]
    public uint ComponentTypeId => 0x72B61FC5;

    [LegacyBinaryField(16)]
    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    [LegacyBinaryField(17)]
    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    [LegacyBinaryField(18)]
    public uint BlendMode => uint.MaxValue;

    [LegacyBinaryField(19)]
    public int Unknown7 => 0;

    [LegacyBinaryField(20)]
    public int MaterialType => TextureType;

    [LegacyBinaryField(21)]
    public LegacyPadding TexturePadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(22)]
    public LegacyUbiArtPath TexturePath => LegacyBinary.Path($"{textureName}.tga", $"world/maps/{MapNameLower}/menuart/textures/");

    [LegacyBinaryField(23)]
    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    [LegacyBinaryField(24)]
    public LegacyUbiArtPath ShaderPath => LegacyBinary.Path("multitexture_1layer.msh", "world/_common/matshader/");

    [LegacyBinaryField(25)]
    public LegacyPadding ShaderPadding => LegacyBinary.Padding(12);

    [LegacyBinaryField(26)]
    public uint Unknown8 => uint.MaxValue;

    [LegacyBinaryField(27)]
    public uint Unknown9 => uint.MaxValue;

    [LegacyBinaryField(28)]
    public int Unknown10 => 0;

    [LegacyBinaryField(29)]
    public int Unknown11 => 0;

    [LegacyBinaryField(30)]
    public int Unknown12 => 0;

    [LegacyBinaryField(31)]
    public float Unknown13 => 1.0f;

    [LegacyBinaryField(32)]
    public LegacyPadding FooterPadding => LegacyBinary.Padding(11);

    [LegacyBinaryField(33)]
    public byte Footer => 1;
}

internal sealed class LegacyMaterialLayerReferences
{
    [LegacyBinaryField(0)]
    public LegacyPadding PrefixPadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(1)]
    public IReadOnlyList<LegacyMaterialLayerReference> Layers { get; } =
    [
        new(), new(), new(), new(), new(), new(), new(), new()
    ];

    [LegacyBinaryField(2)]
    public LegacyPadding SuffixPadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(3)]
    public uint DefaultLayer => uint.MaxValue;

    [LegacyBinaryField(4)]
    public int Footer => 0;
}

internal sealed class LegacyMaterialLayerReference
{
    [LegacyBinaryField(0)]
    public int Unknown0 => 0;

    [LegacyBinaryField(1)]
    public uint LayerId => uint.MaxValue;

    [LegacyBinaryField(2)]
    public long ResourceId => 0;
}

internal static class LegacyFloat
{
    public static float FromBits(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
}
