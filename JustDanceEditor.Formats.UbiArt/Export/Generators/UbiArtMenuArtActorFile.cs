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

    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int Unknown0 => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public LegacyPadding PreTemplatePadding => LegacyBinary.Padding(IsJd2017 ? 28 : 12);

    [LegacyBinaryField(5)]
    public int? ModernPreTemplateFlag => IsJd2017 ? null : 1;

    [LegacyBinaryField(6)]
    public LegacyPadding ModernPreTemplatePadding => LegacyBinary.Padding(IsJd2017 ? 0 : 16);

    [LegacyBinaryField(7)]
    public int Unknown1 => 0;

    [LegacyBinaryField(8)]
    public uint Unknown2 => uint.MaxValue;

    [LegacyBinaryField(9)]
    public int Unknown3 => 0;

    [LegacyBinaryField(10)]
    public string TemplateFileName => "tpl_materialgraphiccomponent2d.tpl";

    [LegacyBinaryField(11)]
    public string TemplateFolder => "enginedata/actortemplates/";

    [LegacyBinaryField(12)]
    public uint TemplateId => 0xB4A817A8;

    [LegacyBinaryField(13)]
    public LegacyPadding ComponentPadding => LegacyBinary.Padding(8);

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
    public int Unknown4 => 0;

    [LegacyBinaryField(20)]
    public LegacyPadding MaterialTypePrefix => LegacyBinary.Padding(3);

    [LegacyBinaryField(21)]
    public byte MaterialType => IsMapBackground ? (byte)1 : (byte)6;

    [LegacyBinaryField(22)]
    public LegacyPadding TexturePadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(23)]
    public string TextureFileName => $"{textureName}.tga";

    [LegacyBinaryField(24)]
    public string TextureFolder => $"world/maps/{MapNameLower}/menuart/textures/";

    [LegacyBinaryField(25)]
    public uint TextureId => IsMapBackground ? 0x75B8D338u : 0xCA888FC5u;

    [LegacyBinaryField(26)]
    public LegacyMaterialLayerReferences LayerReferences { get; } = new();

    [LegacyBinaryField(27)]
    public string ShaderFileName => "multitexture_1layer.msh";

    [LegacyBinaryField(28)]
    public string ShaderFolder => "world/_common/matshader/";

    [LegacyBinaryField(29)]
    public uint ShaderId => 0xD7E7D9C7;

    [LegacyBinaryField(30)]
    public LegacyPadding ShaderPadding => LegacyBinary.Padding(IsJd2017 ? 12 : 40);

    [LegacyBinaryField(31)]
    public float? ModernShaderScale => IsJd2017 ? null : 1.0f;

    [LegacyBinaryField(32)]
    public uint Unknown5 => uint.MaxValue;

    [LegacyBinaryField(33)]
    public uint Unknown6 => uint.MaxValue;

    [LegacyBinaryField(34)]
    public LegacyPadding FooterPadding => LegacyBinary.Padding(12);

    [LegacyBinaryField(35)]
    public float FooterScale => 1.0f;

    [LegacyBinaryField(36)]
    public LegacyPadding TailPadding => LegacyBinary.Padding(11);

    [LegacyBinaryField(37)]
    public byte Footer => 1;
}

internal sealed class UbiArtVideoPlayerActorFile(string mapName, bool isPreview)
{
    private string MapNameLower => mapName.ToLowerInvariant();
    private string VideoFolder => $"world/maps/{MapNameLower}/videoscoach/";

    [LegacyBinaryField(0)]
    public int Version => 1;

    [LegacyBinaryField(1)]
    public int RelativeZ => 0;

    [LegacyBinaryField(2)]
    public float ScaleX => 1.0f;

    [LegacyBinaryField(3)]
    public float ScaleY => 1.0f;

    [LegacyBinaryField(4)]
    public int Rotation => 0;

    [LegacyBinaryField(5)]
    public int PosX => 0;

    [LegacyBinaryField(6)]
    public int PosY => 0;

    [LegacyBinaryField(7)]
    public int TransformVersion => 1;

    [LegacyBinaryField(8)]
    public LegacyPadding TransformPadding => LegacyBinary.Padding(16);

    [LegacyBinaryField(9)]
    public int Unknown0 => 0;

    [LegacyBinaryField(10)]
    public uint Unknown1 => uint.MaxValue;

    [LegacyBinaryField(11)]
    public int Unknown2 => 0;

    [LegacyBinaryField(12)]
    public string TemplateFileName => isPreview ? "video_player_map_preview.tpl" : "video_player_main.tpl";

    [LegacyBinaryField(13)]
    public string TemplateFolder => "world/_common/videoscreen/";

    [LegacyBinaryField(14)]
    public uint TemplateId => isPreview ? 0xD3945428u : 0xF5D5E8F2u;

    [LegacyBinaryField(15)]
    public LegacyPadding ComponentPadding => LegacyBinary.Padding(12);

    [LegacyBinaryField(16)]
    public ushort ComponentMarker => 1;

    [LegacyBinaryField(17)]
    public uint ComponentTypeId => 0x1263DAD9;

    [LegacyBinaryField(18)]
    public int Unknown3 => 0;

    [LegacyBinaryField(19)]
    public string WebmFileName => $"{MapNameLower}.webm";

    [LegacyBinaryField(20)]
    public byte WebmPadding => 0;

    [LegacyBinaryField(21)]
    public string WebmFolder => VideoFolder;

    [LegacyBinaryField(22)]
    public uint WebmId => 0x560FF17A;

    [LegacyBinaryField(23)]
    public LegacyPadding WebmFooterPadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(24)]
    public string MpdFileName => $"{MapNameLower}.mpd";

    [LegacyBinaryField(25)]
    public byte MpdPadding => 0;

    [LegacyBinaryField(26)]
    public string MpdFolder => VideoFolder;

    [LegacyBinaryField(27)]
    public uint MpdId => 0x268C7614;

    [LegacyBinaryField(28)]
    public LegacyPadding FooterPadding => LegacyBinary.Padding(8);

    [LegacyBinaryField(29)]
    public string? ChannelId => isPreview ? mapName : null;
}

internal sealed class UbiArtAutodanceActorFile(string mapName)
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
    public LegacyPadding TransformPadding => LegacyBinary.Padding(24);

    [LegacyBinaryField(5)]
    public int TransformVersion => 1;

    [LegacyBinaryField(6)]
    public LegacyPadding TemplatePadding => LegacyBinary.Padding(20);

    [LegacyBinaryField(7)]
    public uint Unknown1 => uint.MaxValue;

    [LegacyBinaryField(8)]
    public int Unknown2 => 0;

    [LegacyBinaryField(9)]
    public string TemplateFileName => $"{MapNameLower}_autodance.tpl";

    [LegacyBinaryField(10)]
    public string TemplateFolder => $"world/maps/{MapNameLower}/autodance/";

    [LegacyBinaryField(11)]
    public uint TemplateId => 0xD750313C;

    [LegacyBinaryField(12)]
    public LegacyPadding ComponentPadding => LegacyBinary.Padding(11);

    [LegacyBinaryField(13)]
    public byte ComponentVersion => 1;

    [LegacyBinaryField(14)]
    public uint ComponentTypeId => 0x67B8BB77;
}

internal sealed class UbiArtMpdFile
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
