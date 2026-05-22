using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

internal abstract class LegacyCinematicTypedPickableActorBinary
{
    public LegacyCinematicPickableBinary Pickable { get; set; } = new();
}

[LegacyBinaryTypeId(0x97CA628B)]
internal sealed class LegacyCinematicSceneActorBinary : LegacyCinematicTypedPickableActorBinary
{
}

[LegacyBinaryTypeId(0x4FA40F09)]
internal sealed class LegacyCinematicSubSceneActorBinary : LegacyCinematicTypedPickableActorBinary
{
}

internal sealed class LegacyCinematicPickableBinary
{
    public float RelativeZ { get; set; }

    public float ScaleX { get; set; }

    public float ScaleY { get; set; }

    public uint XFlipped { get; set; }

    public string Name { get; set; } = string.Empty;

    [LegacyBinaryEngineVersionCondition(MaxEngineVersion = 2014)]
    public uint DefaultEnable { get; set; }

    public float PositionX { get; set; }

    public float PositionY { get; set; }

    public float Angle { get; set; }

    public LegacyUbiArtFlexiblePath InstanceDataFile { get; set; }

    public uint UnknownBeforeTemplatePath { get; set; }

    public LegacyUbiArtFlexiblePath TemplatePath { get; set; }

    public LegacyCinematicPickableFields ToRuntime() =>
        new(
            RelativeZ,
            ScaleX,
            ScaleY,
            XFlipped,
            Name,
            PositionX,
            PositionY,
            Angle,
            TemplatePath.FullPath);
}

internal abstract class LegacyCinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x72B61FC5)]
internal sealed class LegacyCinematicMaterialGraphicComponentBinary : LegacyCinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x1A7E999A)]
internal sealed class LegacyCinematicMesh3DComponentBinary : LegacyCinematicVisualComponentBinary
{
}

[LegacyBinaryTypeId(0x0579E81B)]
internal sealed class LegacyCinematicPleoTextureGraphicComponentBinary : LegacyCinematicVisualComponentBinary
{
}

internal abstract class LegacyCinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x0E355C68)]
internal sealed class LegacyCinematicFxControllerComponentTemplateBinary : LegacyCinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x00C61D05)]
internal sealed class LegacyCinematicFxBankComponentTemplateBinary : LegacyCinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0xEF03E2F5)]
internal sealed class LegacyCinematicParticleGeneratorComponentTemplateBinary : LegacyCinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x68ED319A)]
internal sealed class LegacyCinematicMesh3DComponentTemplateBinary : LegacyCinematicTemplateComponentBinary
{
}

[LegacyBinaryTypeId(0x9E845460)]
internal sealed class LegacyCinematicTapeBinary
{
}

internal sealed class LegacyCinematicTapeHeaderBinary
{
    public int Version { get; set; }

    public int TapeVersion { get; set; }

    public LegacyBinaryTypeId<LegacyCinematicTapeBinary> TypeId { get; set; }

    public int TypeSize { get; set; }

    public int ClipCount { get; set; }
}

internal abstract class LegacyCinematicTapeClipBinary
{
    public int SerializedSize { get; set; }

    public uint Id { get; set; }

    public uint TrackId { get; set; }

    public int IsActive { get; set; }

    public int StartFrame { get; set; }

    public int DurationFrames { get; set; }
}

[LegacyBinaryTypeId(0x0E1E8158)]
internal sealed class LegacyCinematicTapeReferenceClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x2D8C885B)]
internal sealed class LegacyCinematicSoundSetClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x0F19B038)]
internal sealed class LegacyCinematicFxClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xCBB7C029)]
internal sealed class LegacyCinematicAnimationClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xD128885D)]
internal sealed class LegacyCinematicActorEnableClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x36A312DC)]
internal sealed class LegacyCinematicPositionClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x8607D582)]
internal sealed class LegacyCinematicAlphaClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xF61B3A75)]
internal sealed class LegacyCinematicMaterialColorClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x7A9C58B3)]
internal sealed class LegacyCinematicRotationClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x5ED28F49)]
internal sealed class LegacyCinematicScaleClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x52B89D18)]
internal sealed class LegacyCinematicSecondaryTransformClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x547775BC)]
internal sealed class LegacyCinematicProportionClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xE68412CA)]
internal sealed class LegacyCinematicMaterialGraphicDiffuseAlphaClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xC6FED58E)]
internal sealed class LegacyCinematicMaterialGraphicDiffuseColorClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x4D309320)]
internal sealed class LegacyCinematicMaterialGraphicEnableLayerClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xDD4A9D55)]
internal sealed class LegacyCinematicMaterialGraphicUvRotationClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0xC4115B2E)]
internal sealed class LegacyCinematicMaterialGraphicUvTranslationClipBinary : LegacyCinematicTapeClipBinary
{
}

[LegacyBinaryTypeId(0x511FC7A5)]
internal sealed class LegacyCinematicMaterialGraphicUvScaleClipBinary : LegacyCinematicTapeClipBinary
{
}