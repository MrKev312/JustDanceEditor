using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using KevInc.UbiArt.Cinematics.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;

using System;
using System.Collections.Generic;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTimelineTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicMaterialReaderTests
{
    [Fact]
    public void MaterialReader_ReadsSourceAnimatedUvTranslation()
    {
        CinematicUvModifier modifier = new(
            TranslationU: -0.017f,
            TranslationV: 0,
            AnimTranslationU: true,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(TextureAddressMode.Wrap, TextureAddressMode.Clamp, modifier);

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);
        CinematicUvSampler sampler = material.CreateLayer0Sampler(4.8);

        Assert.Equal(2, material.BlendMode);
        Assert.Equal(TextureAddressMode.Wrap, sampler.AddressModeU);
        Assert.Equal(TextureAddressMode.Clamp, sampler.AddressModeV);
        Assert.True(sampler.Enabled);
        Assert.Equal(2, sampler.BlendMode);
        Assert.Equal(CinematicTextureUsage.EntireTexture, sampler.TextureUsage);
        AssertClose(1, sampler.DiffuseColor.Red);
        AssertClose(1, sampler.DiffuseColor.Green);
        AssertClose(1, sampler.DiffuseColor.Blue);
        AssertClose(1, sampler.DiffuseColor.Alpha);
        CinematicUvModifierState state = Assert.Single(sampler.Modifiers);
        AssertClose(-0.0816, state.TranslationU, tolerance: 0.00001);
    }

    [Fact]
    public void MaterialReader_ReadsModernJsonMaterialLayers()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""
            {"__class":"GFXMaterialShader_Template","blendmode":7,"Layer1":{"__class":"MaterialLayer","Enabled":1,"TexAdressingModeU":3,"TexAdressingModeV":3,"DiffuseColor":[1,0.5,0.25,0.75],"TextureUsage":1,"UVModifiers":[{"__class":"UVModifier","TranslationU":0.25,"TranslationV":-0.4,"AnimTranslationU":0,"AnimTranslationV":1,"Rotation":-0.785398,"RotationOffsetU":0.5,"RotationOffsetV":0.5,"AnimRotation":0,"ScaleU":0.9,"ScaleV":1,"ScaleOffsetU":0,"ScaleOffsetV":0.5}]},"BlendLayer2":10,"Layer2":{"__class":"MaterialLayer","Enabled":1,"TexAdressingModeU":0,"TexAdressingModeV":1,"DiffuseColor":[1,1,1,1],"TextureUsage":5}}
            """ + "\0");

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);

        Assert.Equal(7, material.BlendMode);
        Assert.Equal(2, material.Layers.Count);
        CinematicMaterialLayer layer0 = material.Layers[0];
        Assert.True(layer0.Enabled);
        Assert.Equal(TextureAddressMode.Border, layer0.AddressModeU);
        Assert.Equal(TextureAddressMode.Border, layer0.AddressModeV);
        Assert.Equal(CinematicTextureUsage.AlphaIsRed, layer0.TextureUsage);
        Assert.Equal(2, layer0.BlendMode);
        AssertClose(1, layer0.DiffuseColor!.Value.Red);
        AssertClose(0.5, layer0.DiffuseColor.Value.Green);
        AssertClose(0.25, layer0.DiffuseColor.Value.Blue);
        AssertClose(0.75, layer0.DiffuseColor.Value.Alpha);
        CinematicUvModifier modifier = Assert.Single(layer0.UvModifiers);
        AssertClose(0.25, modifier.TranslationU);
        AssertClose(-0.4, modifier.TranslationV);
        Assert.False(modifier.AnimTranslationU);
        Assert.True(modifier.AnimTranslationV);
        AssertClose(-0.785398, modifier.Rotation);
        AssertClose(0.9, modifier.ScaleU);

        CinematicMaterialLayer layer1 = material.Layers[1];
        Assert.Equal(10, layer1.BlendMode);
        Assert.Equal(TextureAddressMode.Wrap, layer1.AddressModeU);
        Assert.Equal(TextureAddressMode.Mirror, layer1.AddressModeV);
        Assert.Equal(CinematicTextureUsage.EntireTexture, layer1.TextureUsage);
    }

    [Fact]
    public void MaterialGraphicUvScroll_ForcesAnimatedUvTranslation()
    {
        CinematicActor actor = CreateActor(["actor"]);
        CinematicUvModifier modifier = new(
            TranslationU: 0,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        CinematicRenderMaterial material = new(
            2,
            [
                new CinematicMaterialLayer(
                    true,
                    TextureAddressMode.Wrap,
                    TextureAddressMode.Wrap,
                    [modifier])
            ]);
        PropertyClipIndex clipIndex = new(new Dictionary<string, List<PropertyClip>>(StringComparer.OrdinalIgnoreCase)
        {
            [actor.Key] =
            [
                new PropertyClip(
                    new ActorTargetPath(actor.Path),
                    LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicUvScrollClipBinary>(),
                    StartFrame: 0,
                    DurationFrames: 120,
                    State: new CinematicVisualState(
                        Transform: null,
                        Material: null,
                        LayerEnable: null,
                        MaterialGraphic: new CinematicMaterialGraphicClip(
                            CinematicMaterialGraphicClipKind.UvScroll,
                            LayerIndex: 0,
                            UvModifierIndex: 0,
                            U: CreateSingleKeyCurve(0.25f),
                            V: CreateSingleKeyCurve(-0.5f))),
                    Order: 0)
            ]
        });

        CinematicMaterialRuntimeOverrides? overrides = CinematicActorTiming.GetMaterialRuntimeOverrides(actor, clipIndex, frame: 30);
        CinematicUvSampler sampler = Assert.Single(material.CreateSamplers(elapsedSeconds: 4, overrides));
        CinematicUvModifierState state = Assert.Single(sampler.Modifiers);

        AssertClose(1.0, state.TranslationU);
        AssertClose(-2.0, state.TranslationV);
    }

    [Fact]
    public void MaterialGraphicUvScroll_ClearsAfterClipExit()
    {
        CinematicActor actor = CreateActor(["actor"]);
        CinematicUvModifier modifier = new(
            TranslationU: 0,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        CinematicRenderMaterial material = new(
            2,
            [
                new CinematicMaterialLayer(
                    true,
                    TextureAddressMode.Wrap,
                    TextureAddressMode.Wrap,
                    [modifier])
            ]);
        PropertyClipIndex clipIndex = new(new Dictionary<string, List<PropertyClip>>(StringComparer.OrdinalIgnoreCase)
        {
            [actor.Key] =
            [
                new PropertyClip(
                    new ActorTargetPath(actor.Path),
                    LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicUvScrollClipBinary>(),
                    StartFrame: 10,
                    DurationFrames: 5,
                    State: new CinematicVisualState(
                        Transform: null,
                        Material: null,
                        LayerEnable: null,
                        MaterialGraphic: new CinematicMaterialGraphicClip(
                            CinematicMaterialGraphicClipKind.UvScroll,
                            LayerIndex: 0,
                            UvModifierIndex: 0,
                            U: CreateSingleKeyCurve(0.25f))),
                    Order: 0)
            ]
        });

        CinematicMaterialRuntimeOverrides? overrides = CinematicActorTiming.GetMaterialRuntimeOverrides(actor, clipIndex, frame: 16);
        CinematicUvSampler sampler = Assert.Single(material.CreateSamplers(elapsedSeconds: 4, overrides));
        CinematicUvModifierState state = Assert.Single(sampler.Modifiers);

        Assert.Null(overrides);
        AssertClose(0, state.TranslationU);
    }

    [Fact]
    public void MaterialGraphicUvScroll_PersistentMaterialStateSurvivesClipExit()
    {
        CinematicActor actor = CreateActor(["actor"]);
        CinematicUvModifier modifier = new(
            TranslationU: 0,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        CinematicRenderMaterial material = new(
            2,
            [
                new CinematicMaterialLayer(
                    true,
                    TextureAddressMode.Wrap,
                    TextureAddressMode.Wrap,
                    [modifier])
            ]);
        PropertyClipIndex clipIndex = new(new Dictionary<string, List<PropertyClip>>(StringComparer.OrdinalIgnoreCase)
        {
            [actor.Key] =
            [
                new PropertyClip(
                    new ActorTargetPath(actor.Path),
                    LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicUvScrollClipBinary>(),
                    StartFrame: 10,
                    DurationFrames: 5,
                    State: new CinematicVisualState(
                        Transform: null,
                        Material: null,
                        LayerEnable: null,
                        MaterialGraphic: new CinematicMaterialGraphicClip(
                            CinematicMaterialGraphicClipKind.UvScroll,
                            LayerIndex: 0,
                            UvModifierIndex: 0,
                            U: CreateSingleKeyCurve(0.25f))),
                    Order: 0,
                    PersistentMaterialState: true)
            ]
        });

        CinematicMaterialRuntimeOverrides? overrides = CinematicActorTiming.GetMaterialRuntimeOverrides(actor, clipIndex, frame: 16);
        CinematicUvSampler sampler = Assert.Single(material.CreateSamplers(elapsedSeconds: 4, overrides));
        CinematicUvModifierState state = Assert.Single(sampler.Modifiers);

        Assert.NotNull(overrides);
        AssertClose(1.0, state.TranslationU);
    }

    [Fact]
    public void MaterialReader_ReadsLegacyLayerWithAlphaThresholdBeforeAddressModes()
    {
        CinematicUvModifier modifier = new(
            TranslationU: 0.25f,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(
            TextureAddressMode.Mirror,
            TextureAddressMode.Clamp,
            modifier,
            layerSize: 0x3C);

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);
        CinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        Assert.Equal(TextureAddressMode.Mirror, sampler.AddressModeU);
        Assert.Equal(TextureAddressMode.Clamp, sampler.AddressModeV);
        CinematicUvModifierState state = Assert.Single(sampler.Modifiers);
        AssertClose(0.25f, state.TranslationU);
    }

    [Fact]
    public void MaterialReader_ReadsSourceSizedLayerWithoutAlphaThreshold()
    {
        CinematicUvModifier modifier = new(
            TranslationU: 0.125f,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 2,
            ScaleV: 1,
            ScaleOffsetU: 0.0f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(
            TextureAddressMode.Clamp,
            TextureAddressMode.Mirror,
            modifier,
            diffuseColor: new CinematicMaterialColor(0.25, 0.5, 0.75, 1),
            textureUsage: CinematicTextureUsage.EntireTexture,
            layerSize: 0x3C,
            includeAlphaThreshold: false);

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);
        CinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        Assert.Equal(TextureAddressMode.Clamp, sampler.AddressModeU);
        Assert.Equal(TextureAddressMode.Mirror, sampler.AddressModeV);
        Assert.Equal(CinematicTextureUsage.EntireTexture, sampler.TextureUsage);
        AssertClose(0.25, sampler.DiffuseColor.Red);
        AssertClose(0.5, sampler.DiffuseColor.Green);
        AssertClose(0.75, sampler.DiffuseColor.Blue);
        CinematicUvModifierState state = Assert.Single(sampler.Modifiers);
        AssertClose(0.125f, state.TranslationU);
        AssertClose(2, state.ScaleU);
    }

    [Fact]
    public void MaterialReader_ReadsMarkedUvModifierListsLargerThanLegacyDefault()
    {
        CinematicUvModifier modifier = new(
            TranslationU: 0.125f,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 0.25f,
            ScaleV: 0.5f,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(
            TextureAddressMode.Mirror,
            TextureAddressMode.Mirror,
            modifier,
            textureUsage: CinematicTextureUsage.AlphaIsRed,
            uvModifierCount: 48);

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);
        CinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        Assert.Equal(TextureAddressMode.Mirror, sampler.AddressModeU);
        Assert.Equal(TextureAddressMode.Mirror, sampler.AddressModeV);
        Assert.Equal(CinematicTextureUsage.AlphaIsRed, sampler.TextureUsage);
        Assert.Equal(48, sampler.Modifiers.Count);
        AssertClose(0.25f, sampler.Modifiers[0].ScaleU);
    }

    [Fact]
    public void MaterialReader_AllDisabledLegacyAddAlphaFallsBackToDiffuseTexture()
    {
        CinematicUvModifier modifier = new(
            TranslationU: 0,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(
            TextureAddressMode.Wrap,
            TextureAddressMode.Wrap,
            modifier,
            enabled: false,
            blendMode: 7);

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);
        CinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        Assert.Equal(7, material.BlendMode);
        Assert.True(sampler.Enabled);
        Assert.Equal(CinematicTextureUsage.EntireTexture, sampler.TextureUsage);
    }

    [Fact]
    public void MaterialReader_ReadsDiffuseColorInSourceSerializedOrder()
    {
        CinematicUvModifier modifier = new(
            TranslationU: 0,
            TranslationV: 0,
            AnimTranslationU: false,
            AnimTranslationV: false,
            Rotation: 0,
            RotationOffsetU: 0.5f,
            RotationOffsetV: 0.5f,
            AnimRotation: false,
            ScaleU: 1,
            ScaleV: 1,
            ScaleOffsetU: 0.5f,
            ScaleOffsetV: 0.5f);
        byte[] bytes = CreateMaterialBytes(
            TextureAddressMode.Wrap,
            TextureAddressMode.Wrap,
            modifier,
            diffuseColor: new CinematicMaterialColor(0.64313728, 0.32549021, 0.019607844, 1));

        CinematicRenderMaterial material = CinematicMaterialReader.Read(bytes);
        CinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        AssertClose(0.64313728, sampler.DiffuseColor.Red);
        AssertClose(0.32549021, sampler.DiffuseColor.Green);
        AssertClose(0.019607844, sampler.DiffuseColor.Blue);
        AssertClose(1, sampler.DiffuseColor.Alpha);
    }

    [Theory]
    [InlineData("world/_common/matshader/AddAlpha.msh", 7)]
    [InlineData("world/_common/matshader/Default.msh", 2)]
    [InlineData("world/_common/matshader/Screen.msh", 21)]
    [InlineData("world/_common/matshader/PreMultAlpha.msh", 3)]
    public void MaterialReader_CreatesSourceBlendFallbackForMatShaderPaths(string shaderPath, int expectedBlendMode)
    {
        bool created = CinematicMaterialReader.TryCreateShaderFallback(shaderPath, out CinematicRenderMaterial material);

        Assert.True(created);
        Assert.Equal(expectedBlendMode, material.BlendMode);
        Assert.True(material.CreateLayer0Sampler(0).Enabled);
    }

    [Fact]
    public void MaterialReader_PleoAlphaFallbackUsesStackedAlphaSampler()
    {
        bool created = CinematicMaterialReader.TryCreateShaderFallback(
            "world/jd5/_common/matshader/pleoalpha.msh",
            out CinematicRenderMaterial material);

        CinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        Assert.True(created);
        Assert.Equal(2, material.BlendMode);
        Assert.Equal(CinematicTextureUsage.PleoStackedAlpha, sampler.TextureUsage);
        Assert.Equal(TextureAddressMode.Clamp, sampler.AddressModeU);
        Assert.Equal(TextureAddressMode.Clamp, sampler.AddressModeV);
    }

    [Fact]
    public void MaterialReader_ShadowMaskColorLayerUsesStaticShadowOpacity()
    {
        CinematicRenderMaterial material = new(
            CinematicFrameRenderer.GfxBlendAlpha,
            [
                CinematicMaterialLayer.Default with { TextureUsage = CinematicTextureUsage.AlphaIsAlpha },
                CinematicMaterialLayer.Default with { TextureUsage = CinematicTextureUsage.EntireTexture }
            ]);

        CinematicRenderMaterial shadow = CinematicMaterialReader.ApplyMaterialPathConventions(
            "world/jd2015/speedy/graph/materials/d_cactus_shadow.msh",
            material);
        CinematicRenderMaterial regular = CinematicMaterialReader.ApplyMaterialPathConventions(
            "world/jd2015/speedy/graph/materials/d_cactus.msh",
            material);

        AssertClose(CinematicMaterialReader.AnimatedShadowLayerAlpha, shadow.Layers[1].DiffuseColor.GetValueOrDefault().Alpha);
        AssertClose(1, regular.Layers[1].DiffuseColor.GetValueOrDefault().Alpha);
    }

    [Fact]
    public void MaterialReader_DoesNotCreateShaderFallbackForNonShaderPaths()
    {
        bool created = CinematicMaterialReader.TryCreateShaderFallback(
            "world/jd5/princeali/graph/materials/p_gilding.mat",
            out _);

        Assert.False(created);
    }
}
