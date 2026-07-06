using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

internal sealed class LegacyTapeCaseFile(string mapName, string tapeType, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string Extension => tapeType == "dance" ? "dtape" : "ktape";

    public LegacyResourceHeader Header => new(tapeType == "dance" ? 0xFE : 0x100, 0x8229ABC3, 0x40);

    public int One0 => 1;

    public int StructSize => 0x10;

    public int One1 => 1;

    public int TapeReferenceSize => 0x28;

    public uint TapeReferenceTypeId => tapeType == "dance" ? 0x24A37BF0u : 0xFD4547ACu;

    public LegacyUbiArtPath TapePath => LegacyBinary.Path($"{MapNameLower}_tml_{tapeType}.{Extension}", paths.MapSubFolder(MapNameLower, "timeline"));

    public int Padding => 0;
}

internal sealed class LegacySequenceTplFile
{
    public LegacyResourceHeader Header => new(0xB0, 0x8229ABC3u, 0x40);

    public int Padding => 0;
}

internal sealed class LegacySoundTapeFile(string mapName)
{
    public int Version => 1;

    public int SerializedSize => 0xA2;

    public uint TapeTypeId => 0x9E845460;

    public int TapeTypeSize => 0x9C;

    public LegacyPadding Reserved => LegacyBinary.Padding(20);

    public int ClipCount => 1;

    public int TapeClock => 0;

    public string MapName => mapName;
}

internal sealed class LegacyAmbTplFile(string mapName, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string AmbName => $"amb_{MapNameLower}_intro";

    public LegacyResourceHeader Header => new(0x2B0, 0xD94D6C53u, 0x118);

    public int SoundCount => 1;

    public int SoundDescriptorSize => 0xF8;

    public uint SoundDescriptorTypeId => 0xFEC0F184;

    public float Volume => -3.0f;

    public uint SoundParamsTypeId => 0xEB537A60;

    public uint LimitMode => uint.MaxValue;

    public int Category => 0;

    public uint BusId => uint.MaxValue;

    public int FadeInTime => 0;

    public int FadeOutTime => 0;

    public int FileCount => 1;

    public LegacyUbiArtPath AudioPath => LegacyBinary.Path($"{AmbName}.wav", paths.MapSubFolder(MapNameLower, "audio/amb"));

    public LegacyPadding FilePadding => LegacyBinary.Padding(24);

    public int ParamsSize => 0x60;

    public int NumChannels => 2;

    public int Loop => 0;

    public int PlayMode => 1;

    public uint RandomMode => uint.MaxValue;

    public int RandomVolMin => 0;

    public int RandomVolMax => 0;

    public int RandomPitchMin => 0;

    public int RandomPitchMax => 0;

    public float VolumeMultiplierMin => 1.0f;

    public float VolumeMultiplierMax => 1.0f;

    public int LowPass => 0;

    public int HighPass => 0;

    public int Reverb => 0;

    public int ChannelMode => 2;

    public uint Unknown0 => uint.MaxValue;

    public uint Unknown1 => uint.MaxValue;

    public int Unknown2 => 0;

    public int Unknown3 => 0;

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public uint Unknown6 => uint.MaxValue;

    public int Footer => 0;
}

internal sealed class LegacyMainSequenceTplFile(string mapName, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    public LegacyResourceHeader Header => new(0x101, 0x0C736497u, 0x40);

    public int One0 => 1;

    public int StructSize => 0x10;

    public int One1 => 1;

    public int TapeReferenceSize => 0x28;

    public uint TapeReferenceTypeId => 0xABF3773E;

    public LegacyUbiArtPath TapePath => LegacyBinary.Path($"{MapNameLower}_mainsequence.tape", paths.MapSubFolder(MapNameLower, "cinematics"));

    public int Padding => 0;
}

internal sealed class LegacySgsFile
{
    public int Version => 1;

    public uint TypeId => 0xCE018EDB;

    public int Unknown0 => 0;

    public int GraphCount => 6;

    public int Unknown1 => 1;

    public int Unknown2 => 2;

    public int Unknown3 => 0;

    public int Unknown4 => 0;

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
    float videoStartTime,
    LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    public LegacyMusicTrackHeader Header => new(markers.Count);

    public int MarkerCount => markers.Count;

    public IReadOnlyList<int> Markers => markers;

    public int SignatureCount => signatures.Count;

    public IReadOnlyList<LegacySignatureMarker> Signatures => signatures;

    public int SectionCount => sections.Count;

    public IReadOnlyList<LegacySectionMarker> Sections => sections;

    public LegacyMusicTrackTiming Timing => new(engineVersion, startBeat, endBeat, videoStartTime);

    public LegacyUbiArtPath AudioPath => LegacyBinary.Path($"{MapNameLower}.wav", paths.MapSubFolder(MapNameLower, "audio"));

    public LegacyPadding Footer => LegacyBinary.Padding(8);
}

internal sealed class LegacyMusicTrackHeader(int markerCount)
{
    public int Version => 1;

    public int SerializedSize => (166 * markerCount) + 166;

    public LegacyBinaryTypeId<LegacyResourceBaseBinary> BaseTypeId => new();

    public uint BaseTypeSize => 0x6C;

    public LegacyPadding Reserved => LegacyBinary.Padding(28);

    public int ComponentCount => 1;

    public LegacyBinaryTypeId<LegacyMusicTrackComponentBinary> ComponentTypeId => new();

    public uint ComponentSize => 0xA0;

    public uint StructureSize => 0x90;

    public uint MarkerListSize => 0x6C;
}

internal sealed class LegacyMusicTrackTiming(
    UbiArtEngineVersion engineVersion,
    int startBeat,
    uint endBeat,
    float videoStartTime)
{
    public int StartBeat => startBeat;

    public uint EndBeat => endBeat;

    public LegacyPadding? ModernPadding => (int)engineVersion >= 2018 ? LegacyBinary.Padding(10) : null;

    public float VideoStartTime => videoStartTime;

    public LegacyPadding FooterPadding => (int)engineVersion >= 2018 ? LegacyBinary.Padding(20) : LegacyBinary.Padding(4);
}

internal sealed class LegacySignatureMarker(int marker, int beats)
{
    public int Size => 8;

    public int Marker => marker;

    public int Beats => beats;
}

internal sealed class LegacySectionMarker(int startBeat, int sectionType, string comment)
{
    public int Size => 0x14;

    public int StartBeat => startBeat;

    public int SectionType => sectionType;

    public string Comment => comment;
}