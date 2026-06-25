using JustDanceEditor.Formats.UbiArt.Export.Generators.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;

namespace JustDanceEditor.Formats.UbiArt.Export.Generators;

internal enum UbiArtMenuArtActorVersion
{
    Modern,
    Jd2017
}

internal sealed class UbiArtMenuArtActorFile(string textureName, string mapName, UbiArtMenuArtActorVersion version)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private bool IsMapBackground => textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase);
    private bool IsJd2017 => version == UbiArtMenuArtActorVersion.Jd2017;

    public int Version => 1;

    public int Unknown0 => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public LegacyPadding PreTemplatePadding => LegacyBinary.Padding(IsJd2017 ? 28 : 12);

    public int? ModernPreTemplateFlag => IsJd2017 ? null : 1;

    public LegacyPadding ModernPreTemplatePadding => LegacyBinary.Padding(IsJd2017 ? 0 : 16);

    public int Unknown1 => 0;

    public uint Unknown2 => uint.MaxValue;

    public int Unknown3 => 0;

    public string TemplateFileName => "tpl_materialgraphiccomponent2d.tpl";

    public string TemplateFolder => "enginedata/actortemplates/";

    public uint TemplateId => 0xB4A817A8;

    public LegacyPadding ComponentPadding => LegacyBinary.Padding(8);

    public int ComponentVersion => 1;

    public uint ComponentTypeId => 0x72B61FC5;

    public LegacyAbgrColor Color => LegacyAbgrColor.White;

    public LegacyPadding ColorPadding => LegacyBinary.Padding(16);

    public uint BlendMode => uint.MaxValue;

    public int Unknown4 => 0;

    public LegacyPadding MaterialTypePrefix => LegacyBinary.Padding(3);

    public byte MaterialType => IsMapBackground ? (byte)1 : (byte)6;

    public LegacyPadding TexturePadding => LegacyBinary.Padding(8);

    public string TextureFileName => $"{textureName}.tga";

    public string TextureFolder => $"world/maps/{MapNameLower}/menuart/textures/";

    public uint TextureId => IsMapBackground ? 0x75B8D338u : 0xCA888FC5u;

    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    public string ShaderFileName => "multitexture_1layer.msh";

    public string ShaderFolder => "world/_common/matshader/";

    public uint ShaderId => 0xD7E7D9C7;

    public LegacyPadding ShaderPadding => LegacyBinary.Padding(IsJd2017 ? 12 : 40);

    public float? ModernShaderScale => IsJd2017 ? null : 1.0f;

    public uint Unknown5 => uint.MaxValue;

    public uint Unknown6 => uint.MaxValue;

    public LegacyPadding FooterPadding => LegacyBinary.Padding(12);

    public float FooterScale => 1.0f;

    public LegacyPadding TailPadding => LegacyBinary.Padding(11);

    public byte Footer => 1;
}

internal sealed class UbiArtVideoPlayerActorFile(string mapName, bool isPreview)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string VideoFolder => $"world/maps/{MapNameLower}/videoscoach/";

    public int Version => 1;

    public int RelativeZ => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public int Rotation => 0;

    public int PosX => 0;

    public int PosY => 0;

    public int TransformVersion => 1;

    public LegacyPadding TransformPadding => LegacyBinary.Padding(16);

    public int Unknown0 => 0;

    public uint Unknown1 => uint.MaxValue;

    public int Unknown2 => 0;

    public string TemplateFileName => isPreview ? "video_player_map_preview.tpl" : "video_player_main.tpl";

    public string TemplateFolder => "world/_common/videoscreen/";

    public uint TemplateId => isPreview ? 0xD3945428u : 0xF5D5E8F2u;

    public LegacyPadding ComponentPadding => LegacyBinary.Padding(12);

    public ushort ComponentMarker => 1;

    public uint ComponentTypeId => 0x1263DAD9;

    public int Unknown3 => 0;

    public string WebmFileName => $"{MapNameLower}.webm";

    public byte WebmPadding => 0;

    public string WebmFolder => VideoFolder;

    public uint WebmId => 0x560FF17A;

    public LegacyPadding WebmFooterPadding => LegacyBinary.Padding(8);

    public string MpdFileName => $"{MapNameLower}.mpd";

    public byte MpdPadding => 0;

    public string MpdFolder => VideoFolder;

    public uint MpdId => 0x268C7614;

    public LegacyPadding FooterPadding => LegacyBinary.Padding(8);

    public string? ChannelId => isPreview ? mapName : null;
}

internal sealed class UbiArtAutodanceActorFile(string mapName)
{
    private string MapNameLower => mapName.ToLowerInvariant();

    public int Version => 1;

    public int Unknown0 => 0;

    public float ScaleX => 1.0f;

    public float ScaleY => 1.0f;

    public LegacyPadding TransformPadding => LegacyBinary.Padding(24);

    public int TransformVersion => 1;

    public LegacyPadding TemplatePadding => LegacyBinary.Padding(20);

    public uint Unknown1 => uint.MaxValue;

    public int Unknown2 => 0;

    public string TemplateFileName => $"{MapNameLower}_autodance.tpl";

    public string TemplateFolder => $"world/maps/{MapNameLower}/autodance/";

    public uint TemplateId => 0xD750313C;

    public LegacyPadding ComponentPadding => LegacyBinary.Padding(11);

    public byte ComponentVersion => 1;

    public uint ComponentTypeId => 0x67B8BB77;
}

internal sealed class UbiArtMpdFile
{
    public int Version => 1;

    public int TypeId => 0x00424BAE;

    public byte DataSize => 0x14;

    public float Scale => 1.0f;

    public int Footer => 0;
}