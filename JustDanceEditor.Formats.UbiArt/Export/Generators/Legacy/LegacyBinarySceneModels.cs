using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

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