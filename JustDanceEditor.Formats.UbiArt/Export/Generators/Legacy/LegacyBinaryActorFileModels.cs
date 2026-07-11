using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;

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