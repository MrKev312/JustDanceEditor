using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

internal sealed class LegacyPathContext(string mapRoot, string commonRoot)
{
    private string MapRoot { get; } = NormalizeRoot(mapRoot);
    private string CommonRoot { get; } = NormalizeRoot(commonRoot);

    public string MapFolder(string mapNameLower) => $"{MapRoot}/{mapNameLower}/";
    public string MapSubFolder(string mapNameLower, string folder) => $"{MapFolder(mapNameLower)}{NormalizeRelative(folder)}/";
    public string CommonFolder(string folder) => $"{CommonRoot}/{NormalizeRelative(folder)}/";

    private static string NormalizeRoot(string path) => path.Replace('\\', '/').Trim('/');
    private static string NormalizeRelative(string path) => path.Replace('\\', '/').Trim('/');
}

internal sealed class LegacyTapeFile(string mapName, UbiArtEngineVersion engineVersion, IEnumerable<LegacyTapeClip> clips)
{
    private IReadOnlyList<LegacyTapeClip> ClipList { get; } = [.. clips];

    public uint Version => 1;

    public int TapeVersion => (224 * ClipList.Count) + 166;

    public LegacyBinaryTypeId<LegacyClipTapeBinary> TypeId => new();

    public uint TypeSize => (int)engineVersion == 2015 ? 0x8Cu : 0x9Cu;

    public int ClipCount => ClipList.Count;

    public IReadOnlyList<LegacyTapeClip> Clips => ClipList;

    public LegacyPadding FooterPadding => LegacyBinary.Padding((int)engineVersion == 2015 ? 8 : 12);

    public int TapeClock => 0;

    public int TapeBarCount => 1;

    public int FreeResourcesAfterPlay => 0;

    public string MapName => mapName;
}

internal abstract class LegacyTapeClip
{
    public abstract int SerializedSize { get; }

    public uint Id { get; init; }

    public uint TrackId { get; init; }

    public int IsActive { get; init; } = 1;

    public int StartTime { get; init; }

    public int Duration { get; init; }
}

[LegacyBinaryTypeId(0x955384A1)]
internal sealed class LegacyMotionClip : LegacyTapeClip
{
    public override int SerializedSize => 0x70;

    public LegacyUbiArtPath ClassifierPath { get; init; }

    public int Unknown0 => 0;

    public int GoldMove { get; init; }

    public int CoachId { get; init; }

    public int MoveType { get; init; }

    public LegacyAbgrColor Color { get; init; } = LegacyAbgrColor.White;

    public LegacyMotionPlatformSpecifics MotionPlatformSpecifics { get; } = new();
}

[LegacyBinaryTypeId(0x52EC8962)]
internal sealed class LegacyPictogramClip : LegacyTapeClip
{
    public override int SerializedSize => 0x38;

    public LegacyUbiArtPath PictoPath { get; init; }

    public int Unknown0 => 0;

    public uint CoachCount { get; init; } = uint.MaxValue;
}

[LegacyBinaryTypeId(0xFD69B110)]
internal sealed class LegacyGoldEffectClip : LegacyTapeClip
{
    public override int SerializedSize => 0x1C;

    public int EffectType { get; init; }
}

[LegacyBinaryTypeId(0x68552A41)]
internal sealed class LegacyKaraokeClip : LegacyTapeClip
{
    public override int SerializedSize => 0x50;

    public float Pitch { get; init; }

    public string Lyrics { get; init; } = string.Empty;

    public int IsEndOfLine { get; init; }

    public int ContentType { get; init; }

    public int StartTimeTolerance { get; init; } = 4;

    public int EndTimeTolerance { get; init; } = 4;

    public float SemitoneTolerance { get; init; } = 5;
}

[LegacyBinaryTypeId(0x2D8C885B)]
internal sealed class LegacySoundSetClip : LegacyTapeClip
{
    public override int SerializedSize => 0x40;

    public LegacyUbiArtPath SoundSetPath { get; init; }

    public int SoundChannel => 0;

    public int StopsOnEnd => 0;

    public int AccountedForDuration => 0;

    public int Padding => 0;
}

[LegacyBinaryTypeId(0x52E06A9A)]
internal sealed class LegacyHideUserInterfaceClip(UbiArtEngineVersion engineVersion) : LegacyTapeClip
{
    public override int SerializedSize => 0x48;

    public LegacyPadding VersionPadding => LegacyBinary.Padding((int)engineVersion == 2015 ? 4 : 8);

    public int EventType => 1;

    public int Padding => 0;
}

[LegacyBinaryTypeId(0x101F9D2B)]
internal sealed class LegacyVibrationClip : LegacyTapeClip
{
    public override int SerializedSize => 0x18;
}

internal sealed class LegacyAbgrColor(float alpha, float blue, float green, float red)
{
    public static LegacyAbgrColor White { get; } = new(1, 1, 1, 1);

    public float Alpha => alpha;

    public float Blue => blue;

    public float Green => green;

    public float Red => red;
}

internal sealed class LegacyMotionPlatformSpecifics
{
    public IReadOnlyList<LegacyCameraScoringDefaults> Platforms { get; } =
    [
        new(),
        new(),
        new()
    ];

    public LegacyPadding Terminator => LegacyBinary.Padding(16);
}

internal sealed class LegacyCameraScoringDefaults
{
    public int Platform => 3;

    public int ScoreScale => 1;

    public int ScoreSmoothing => 0;

    public int Thresholds => 0;
}

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

internal sealed class LegacySceneFile(uint sceneId, IEnumerable<object?> actors, object? footer = null, int? actorCount = null)
{
    private IReadOnlyList<object?> ActorList { get; } = [.. actors];

    public LegacySceneHeader Header => new(sceneId, actorCount ?? ActorList.Count);

    public IReadOnlyList<object?> Actors => ActorList;

    public object? Footer => footer;
}

internal sealed class LegacySceneHeader(uint sceneId, int actorCount)
{
    public int Version => 1;

    public uint SceneId => sceneId;

    public LegacyPadding Reserved => LegacyBinary.Padding(15);

    public byte ActorCount => (byte)actorCount;
}

internal sealed class LegacyMainSceneFooter
{
    public int Unknown0 => 0;

    public int SettingsTypePart0 => 0x0001CE01;

    public uint SettingsTypePart1 => 0x8EDB0000;

    public int Unknown1 => 0;

    public int Unknown2 => 0x00060000;

    public int Unknown3 => 0x00010000;

    public int Unknown4 => 0x00020000;

    public int Unknown5 => 0;

    public int Unknown6 => 0;

    public LegacyPadding Padding => LegacyBinary.Padding(6);
}

internal sealed class LegacySingleActorSceneFile(string mapName, string suffix, string folder, string extension, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string ActorFolder => string.IsNullOrEmpty(folder)
        ? paths.MapFolder(MapNameLower)
        : paths.MapSubFolder(MapNameLower, folder);

    public int Version => 1;

    public uint TypeId => 0x2DC82C5F;

    public int SerializedSize => 0x5C;

    public string ActorFileName => $"{MapNameLower}_{suffix}.{extension}";

    public string ActorPath => ActorFolder;

    public int ActorCount => 1;

    public LegacyPadding Padding => LegacyBinary.Padding(44);

    public int FooterVersion => 1;

    public uint FooterTypeId => 0xF466D41A;

    public int Footer => 0;
}

internal sealed class LegacySubSceneDefinitionActor(
    string mapName,
    string mapNameLower,
    string suffix,
    string folder,
    int viewType,
    LegacyPathContext paths)
{
    public uint TypeId => 0x4FA40F09;

    public int RelativeZ => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Angle => 0;

    public string FriendlyName => $"{mapName}{suffix}";

    public uint Marker => uint.MaxValue;

    public int PosX => 0;

    public int PosY => 0;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("subscene.tpl", "enginedata/actortemplates/");

    public LegacyPadding TemplatePadding => LegacyBinary.Padding(12);

    public LegacyUbiArtPath ScenePath => LegacyBinary.Path($"{mapNameLower}{suffix.ToLowerInvariant()}.isc", paths.MapSubFolder(mapNameLower, folder));

    public int EmbedScene => 0;

    public int IsSinglePiece => 1;

    public int ZForced => 0;

    public int DirectPicking => 1;

    public int IgnoreSave => 1;

    public int Unknown4 => 0;

    public int ViewType => viewType;
}

internal sealed class LegacySongDescSceneActor(string mapName, string mapNameLower, LegacyPathContext paths, bool useLegacyConvertedData)
{
    public uint TypeId => 0x97CA628B;

    public int RelativeZ => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Angle => 0;

    public string FriendlyName => $"{mapName} : Template Artist - Template Title.JDVer = 5, ID = 842776738, Type = 1 (Flags 0x00000000), NbCoach = 2, Difficulty = 2";

    public uint Marker => uint.MaxValue;

    public uint PosX => 0xC0620BE5;

    public uint PosY => 0xBFBE1F08;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => useLegacyConvertedData
        ? LegacyBinary.Path("songdesc.main_legacy.tpl", $"cache/legacyconverteddata/{mapNameLower}/")
        : LegacyBinary.Path("songdesc.tpl", paths.MapFolder(mapNameLower));

    public int ComponentCount => 2;

    public int Unknown4 => 0;

    public int ComponentVersion => 1;

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
    public uint TypeId => 0x97CA628B;

    public object PreData => preData;

    public string Name => name;

    public uint Marker => uint.MaxValue;

    public object PostData => postData;

    public uint InstanceId => uint.MaxValue;

    public int Unknown0 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path(templateFileName, templateFolder);

    public object Tail => tail;
}

internal sealed class LegacyEmbeddedSubSceneActor(string name, string templateFileName, string templateFolder)
{
    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Angle => 0;

    public string Name => name;

    public uint Marker => uint.MaxValue;

    public float PosX => LegacyFloat.FromBits(0xBBC9C90Cu);

    public float PosY => LegacyFloat.FromBits(0xBBC9C90Cu);

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path(templateFileName, templateFolder);

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x231F27DE;

    public LegacyPadding Padding => LegacyBinary.Padding(16);
}

internal sealed class LegacyJd2014TimelineSceneActor(string name, string mapNameLower, LegacyPathContext paths)
{
    public uint TypeId => 0x97CA628B;

    public int RelativeZ => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Angle => 0;

    public string FriendlyName => name;

    public int Marker => 0;

    public uint PosX => 0x40BFAFBA;

    public uint PosY => 0xC0A04250;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("timeline.tpl", paths.MapSubFolder(mapNameLower, "timeline"));

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0xDD41F555;

    public LegacyPadding Padding => LegacyBinary.Padding(12);
}

internal sealed class LegacyVideoScreenActor(string mapNameLower, LegacyPathContext paths, bool embedded = false)
{
    public uint TypeId => 0x97CA628B;

    public float RelativeZ => -1.0f;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Angle => 0;

    public string Name => "VideoScreen";

    public uint Marker => uint.MaxValue;

    public int PosX => 0;

    public float PosY => -4.5f;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => embedded
        ? LegacyBinary.Path("video_player_main.tpl", paths.CommonFolder("videoscreen"), 0xF5D5E8F2)
        : LegacyBinary.Path("video_player_main.tpl", paths.CommonFolder("videoscreen"));

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x1263DAD9;

    public LegacyUbiArtPath VideoPath => LegacyBinary.Path($"{mapNameLower}.webm", paths.MapSubFolder(mapNameLower, "videoscoach"));

    public LegacyPadding Padding => LegacyBinary.Padding(8);
}

internal sealed class LegacyVideoOutputActor(LegacyPathContext paths, bool embedded = false)
{
    public uint TypeId => 0x97CA628B;

    public int RelativeZ => 0;

    public float ScaleX => embedded ? 1.0f : LegacyFloat.FromBits(0x407C3D3Eu);

    public float ScaleY => embedded ? 1.0f : LegacyFloat.FromBits(0x400E147Bu);

    public int Angle => 0;

    public string Name => "VideoOutput";

    public uint Marker => uint.MaxValue;

    public int PosX => 0;

    public int PosY => 0;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("video_output_main.tpl", paths.CommonFolder("videoscreen"));

    public int Unknown4 => embedded ? 2 : 0;

    public int Unknown5 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x0579E81B;

    public LegacyVideoOutputMaterialConfig Material { get; } = new(paths, embedded);
}

internal sealed class LegacyVideoOutputMaterialConfig(LegacyPathContext paths, bool embedded = false)
{
    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public int Unknown3 => 0;

    public uint BlendMode => uint.MaxValue;

    public int Unknown4 => 0;

    public int MaterialType => 1;

    public int Unknown5 => 0;

    public int Unknown6 => 0;

    public int Unknown7 => 0;

    public int Unknown8 => 0;

    public IReadOnlyList<LegacyVideoOutputLayerConfig> Layers { get; } =
    [
        new(), new(), new(), new(), new(), new(), new(), new()
    ];

    public uint Unknown9 => uint.MaxValue;

    public int Unknown10 => 0;

    public int Unknown11 => 0;

    public int Unknown12 => 0;

    public int Unknown13 => 0;

    public uint Unknown14 => uint.MaxValue;

    public int Unknown15 => 0;

    public string ShaderFileName => "pleofullscreen.msh";

    public string ShaderFolder => paths.CommonFolder("matshader");

    public uint ShaderId => embedded ? 0xCFBBFE7Au : 0x6A06E805;

    public int Unknown16 => 0;

    public int Unknown17 => 0;

    public int Unknown18 => 0;

    public uint Unknown19 => uint.MaxValue;

    public uint Unknown20 => uint.MaxValue;

    public int Unknown21 => 0;

    public int Unknown22 => 0;

    public int Unknown23 => 0;

    public float Unknown24 => 1.0f;

    public int Unknown25 => 0;

    public int Unknown26 => 0;

    public int Unknown27 => 1;

    public LegacyPadding Padding => LegacyBinary.Padding(20);
}

internal sealed class LegacyVideoOutputLayerConfig
{
    public uint Unknown0 => uint.MaxValue;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public int Unknown3 => 0;
}

internal sealed class LegacyMenuArtSceneActor(
    string mapName,
    string mapNameLower,
    string suffix,
    uint bounds0,
    uint bounds1,
    LegacyPathContext paths)
{
    public uint TypeId => 0x97CA628B;

    public int RelativeZ => 0;

    public float ScaleX => 0.3f;

    public float ScaleY => 0.3f;

    public int Angle => 0;

    public string Name => $"{mapName}_{suffix}";

    public uint Marker => uint.MaxValue;

    public uint Bounds0 => bounds0;

    public uint Bounds1 => bounds1;

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public uint InstanceId => uint.MaxValue;

    public int Unknown3 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("tpl_materialgraphiccomponent2d.tpl", "enginedata/actortemplates/");

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x72B61FC5;

    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    public uint BlendMode => uint.MaxValue;

    public int Unknown6 => 0;

    public int MaterialType => 1;

    public int Unknown7 => 0;

    public int Unknown8 => 0;

    public LegacyUbiArtPath TexturePath => LegacyBinary.Path($"{mapNameLower}_{suffix}.tga", paths.MapSubFolder(mapNameLower, "menuart/textures"));

    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    public LegacyUbiArtPath ShaderPath => LegacyBinary.Path("multitexture_1layer.msh", paths.CommonFolder("matshader"), 0xD7E7D9C7);

    public int Unknown9 => 0;

    public int Unknown10 => 0;

    public int Unknown11 => 0;

    public uint Unknown12 => uint.MaxValue;

    public uint Unknown13 => uint.MaxValue;

    public int Unknown14 => 0;

    public int Unknown15 => 0;

    public int Unknown16 => 0;

    public float Unknown17 => 1.0f;

    public int Unknown18 => 0;

    public int Unknown19 => 0;

    public int Footer => 1;
}

internal sealed class LegacyCoverActor(
    string name,
    string mapNameLower,
    string textureFile,
    object preData,
    object postData,
    object footer,
    LegacyPathContext paths)
{
    public uint TypeId => 0x97CA628B;

    public object PreData => preData;

    public string Name => name;

    public uint Marker => uint.MaxValue;

    public object PostData => postData;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("tpl_materialgraphiccomponent2d.tpl", "enginedata/actortemplates/", 0xB4A817A8);

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x72B61FC5;

    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    public uint BlendMode => uint.MaxValue;

    public int Unknown2 => 0;

    public int MaterialType => 1;

    public LegacyPadding TexturePadding => LegacyBinary.Padding(8);

    public LegacyUbiArtPath TexturePath => LegacyBinary.Path(textureFile, paths.MapSubFolder(mapNameLower, "menuart/textures"));

    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    public LegacyUbiArtPath ShaderPath => LegacyBinary.Path("multitexture_1layer.msh", paths.CommonFolder("matshader"));

    public int Unknown3 => 0;

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public uint Unknown6 => uint.MaxValue;

    public uint Unknown7 => uint.MaxValue;

    public int Unknown8 => 0;

    public int Unknown9 => 0;

    public int Unknown10 => 0;

    public float Unknown11 => 1.0f;

    public object Footer => footer;
}

internal sealed class LegacyActorHeader
{
    public int Version => 1;

    public int Unknown0 => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Rotation => 0;

    public int X => 0;

    public uint ParentId => uint.MaxValue;

    public int Z => 0;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public int Unknown3 => 0;

    public int Unknown4 => 0;

    public uint Unknown5 => uint.MaxValue;

    public int Unknown6 => 0;
}

internal sealed class LegacyGenericActorFile(string luaPath)
{
    public LegacyActorHeader Header { get; } = new();

    public LegacyUbiArtPath TemplatePath => LegacyUbiArtPath.FromFullPath(luaPath);

    public int ComponentCount => 2;

    public int Unknown0 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0xE07FCC3F;
}

internal sealed class LegacyJd2014TimelineActorFile(string mapName, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    public LegacyActorHeader Header { get; } = new();

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("timeline.tpl", paths.MapSubFolder(MapNameLower, "timeline"));

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0xDD41F555;
}

internal sealed class LegacyVideoPlayerActorFile(string mapName, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    public LegacyActorHeader Header { get; } = new();

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("video_player_main.tpl", paths.CommonFolder("videoscreen"));

    public int Unknown0 => 0;

    public int Unknown1 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x1263DAD9;

    public LegacyUbiArtPath VideoPath => LegacyBinary.Path($"{MapNameLower}.webm", paths.MapSubFolder(MapNameLower, "videoscoach"));

    public LegacyPadding Padding => LegacyBinary.Padding(8);
}

internal sealed class LegacyMpdFile
{
    public int Version => 1;

    public int TypeId => 0x00424BAE;

    public byte DataSize => 0x14;

    public float Scale => 1.0f;

    public int Footer => 0;
}

internal sealed class LegacyAutodanceActorFile(string mapName, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    public int Version => 1;

    public int Unknown0 => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Unknown1 => 0;

    public int Unknown2 => 0;

    public int Unknown3 => 0;

    public int Unknown4 => 0;

    public int Unknown5 => 0;

    public int Unknown6 => 0;

    public uint Unknown7 => uint.MaxValue;

    public int Unknown8 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path($"{MapNameLower}_autodance.tpl", paths.MapSubFolder(MapNameLower, "autodance"));

    public int Unknown9 => 0;

    public int Unknown10 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x677B269B;
}

internal sealed class LegacyMenuArtActorFile(string textureName, string mapName, LegacyPathContext paths)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private int TextureType => textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase) ? 1 : 6;

    public int Version => 1;

    public int Unknown0 => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Rotation => 0;

    public int Unknown1 => 0;

    public uint ParentId => uint.MaxValue;

    public int Unknown2 => 0;

    public LegacyPadding TransformPadding => LegacyBinary.Padding(16);

    public uint Unknown3 => uint.MaxValue;

    public int Unknown4 => 0;

    public LegacyUbiArtPath TemplatePath => LegacyBinary.Path("tpl_materialgraphiccomponent2d.tpl", "enginedata/actortemplates/");

    public int Unknown5 => 0;

    public int Unknown6 => 0;

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x72B61FC5;

    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    public uint BlendMode => uint.MaxValue;

    public int Unknown7 => 0;

    public int MaterialType => TextureType;

    public LegacyPadding TexturePadding => LegacyBinary.Padding(8);

    public LegacyUbiArtPath TexturePath => LegacyBinary.Path($"{textureName}.tga", paths.MapSubFolder(MapNameLower, "menuart/textures"));

    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    public LegacyUbiArtPath ShaderPath => LegacyBinary.Path("multitexture_1layer.msh", paths.CommonFolder("matshader"));

    public LegacyPadding ShaderPadding => LegacyBinary.Padding(12);

    public uint Unknown8 => uint.MaxValue;

    public uint Unknown9 => uint.MaxValue;

    public int Unknown10 => 0;

    public int Unknown11 => 0;

    public int Unknown12 => 0;

    public float Unknown13 => 1.0f;

    public LegacyPadding FooterPadding => LegacyBinary.Padding(11);

    public byte Footer => 1;
}

internal sealed class LegacyMaterialLayerReferences
{
    public LegacyPadding PrefixPadding => LegacyBinary.Padding(8);

    public IReadOnlyList<LegacyMaterialLayerReference> Layers { get; } =
    [
        new(), new(), new(), new(), new(), new(), new(), new()
    ];

    public LegacyPadding SuffixPadding => LegacyBinary.Padding(8);

    public uint DefaultLayer => uint.MaxValue;

    public int Footer => 0;
}

internal sealed class LegacyMaterialLayerReference
{
    public int Unknown0 => 0;

    public uint LayerId => uint.MaxValue;

    public long ResourceId => 0;
}

internal static class LegacyFloat
{
    public static float FromBits(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));
}