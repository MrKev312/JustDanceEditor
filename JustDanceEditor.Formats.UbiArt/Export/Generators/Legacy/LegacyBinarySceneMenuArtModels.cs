using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

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