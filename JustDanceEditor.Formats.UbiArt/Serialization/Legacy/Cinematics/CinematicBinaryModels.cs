using KevInc.UbiArt.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

internal abstract class CinematicTypedPickableActorBinary
{
    public CinematicPickableBinary Pickable { get; set; } = new();
}

[LegacyBinaryTypeId(0x97CA628B)]
internal sealed class CinematicSceneActorBinary : CinematicTypedPickableActorBinary
{
}

[LegacyBinaryTypeId(0x4FA40F09)]
internal sealed class CinematicSubSceneActorBinary : CinematicTypedPickableActorBinary
{
}

internal sealed class CinematicPickableBinary
{
    public float RelativeZ { get; set; }

    public float ScaleX { get; set; }

    public float ScaleY { get; set; }

    public uint XFlipped { get; set; }

    public string Name { get; set; } = string.Empty;

    [LegacyBinaryEngineVersionCondition(MaxEngineVersion = 2014)]
    public uint DefaultEnable { get; set; } = 1;

    public float PositionX { get; set; }

    public float PositionY { get; set; }

    public float Angle { get; set; }

    public LegacyUbiArtFlexiblePath InstanceDataFile { get; set; }

    public uint UnknownBeforeTemplatePath { get; set; }

    public LegacyUbiArtFlexiblePath TemplatePath { get; set; }

    public CinematicPickableFields ToRuntime() =>
        new(
            RelativeZ,
            ScaleX,
            ScaleY,
            XFlipped,
            Name,
            DefaultEnable != 0,
            PositionX,
            PositionY,
            Angle,
            TemplatePath.FullPath);
}

internal abstract class CinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x72B61FC5)]
internal sealed class CinematicMaterialGraphicComponentBinary : CinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x1A7E999A)]
internal sealed class CinematicMesh3DComponentBinary : CinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x0579E81B)]
internal sealed class CinematicPleoTextureGraphicComponentBinary : CinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0xA6E4EFBA)]
internal sealed class CinematicAnimLightComponentBinary : CinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x62A12110)]
internal sealed class CinematicAnimatedComponentBinary : CinematicVisualComponentBinary
{
}

internal abstract class CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x0E355C68)]
internal sealed class CinematicFxControllerComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x00C61D05)]
internal sealed class CinematicFxBankComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0xEF03E2F5)]
internal sealed class CinematicParticleGeneratorComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x68ED319A)]
internal sealed class CinematicMesh3DComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x2949932E)]
internal sealed class CinematicPleoTextureGraphicComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0xA3557351)]
internal sealed class CinematicAnimLightComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x9E401F14)]
internal sealed class CinematicAnimatedComponentTemplateBinary : CinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x9E845460)]
internal sealed class CinematicTapeBinary
{
}

internal sealed class CinematicTapeHeaderBinary
{
    public int Version { get; set; }

    public int TapeVersion { get; set; }

    public LegacyBinaryTypeId<CinematicTapeBinary> TypeId { get; set; }

    public int TypeSize { get; set; }

    public int ClipCount { get; set; }
}

internal abstract class CinematicTapeClipBinary
{
    public int SerializedSize { get; set; }

    public uint Id { get; set; }

    public uint TrackId { get; set; }

    public int IsActive { get; set; }

    public int StartFrame { get; set; }

    public int DurationFrames { get; set; }
}

[LegacyBinaryTypeId(0x0E1E8158)]
internal sealed class CinematicTapeReferenceClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x2D8C885B)]
internal sealed class CinematicSoundSetClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x0F19B038)]
internal sealed class CinematicFxClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xCBB7C029)]
internal sealed class CinematicAnimationClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x9A19F97D)]
internal sealed class CinematicPivotClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xA247B5D3)]
internal sealed class CinematicSpawnActorClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x115F128D)]
internal sealed class CinematicTapeLauncherClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xCE73233E)]
internal sealed class CinematicGameplayEventClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xE5B334C8)]
internal sealed class CinematicTextClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x5C944B01)]
internal sealed class CinematicMarkerClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x0AF42893)]
internal sealed class CinematicMashupClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x896A96B0)]
internal sealed class CinematicSlotClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x47B10C89)]
internal sealed class CinematicVideoClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xD128885D)]
internal sealed class CinematicActorEnableClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xAEBB218B)]
internal sealed class CinematicClearColorComponentBinary;

[LegacyBinaryTypeId(0x36A312DC)]
internal sealed class CinematicPositionClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x8607D582)]
internal sealed class CinematicAlphaClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xF61B3A75)]
internal sealed class CinematicMaterialColorClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x7A9C58B3)]
internal sealed class CinematicRotationClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x5ED28F49)]
internal sealed class CinematicScaleClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x52B89D18)]
internal sealed class CinematicSecondaryTransformClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x547775BC)]
internal sealed class CinematicProportionClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x1F11BC9A)]
internal sealed class CinematicProportion3DClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xE68412CA)]
internal sealed class CinematicMaterialGraphicDiffuseAlphaClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xC6FED58E)]
internal sealed class CinematicMaterialGraphicDiffuseColorClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x4D309320)]
internal sealed class CinematicMaterialGraphicEnableLayerClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xDD4A9D55)]
internal sealed class CinematicMaterialGraphicUvRotationClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xC4115B2E)]
internal sealed class CinematicMaterialGraphicUvTranslationClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x57E26726)]
internal sealed class CinematicMaterialGraphicUvScrollClipBinary : CinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x511FC7A5)]
internal sealed class CinematicMaterialGraphicUvScaleClipBinary : CinematicTapeClipBinary
{
}