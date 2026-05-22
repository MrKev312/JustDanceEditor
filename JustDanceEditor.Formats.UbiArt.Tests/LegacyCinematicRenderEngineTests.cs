using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class LegacyCinematicRenderEngineTests
{
    [Fact]
    public void Geometry_DefaultsToSourceBackedTwoWorldUnitQuad()
    {
        Image<Bgra32> image = new(320, 180);
        using MaterializedCinematicImage materialized = new("actor", "world/maps/test/graph/textures/a.png", image);

        RenderGeometry geometry = LegacyCinematicRenderEngine.CreateRenderGeometry(materialized);

        Assert.Equal(2.0, geometry.WidthWorld);
        Assert.Equal(2.0, geometry.HeightWorld);
        Assert.Equal(CinematicGeometrySource.NoAtlasQuad, geometry.Source);
    }

    [Fact]
    public void StringId_UsesSourceDobbsUppercaseHash()
    {
        uint materialGraphicComponentId = LegacyCinematicStringId.Compute("MaterialGraphicComponent");

        Assert.Equal(LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicComponentBinary>(), materialGraphicComponentId);
    }

    [Fact]
    public void AtlasPath_UsesSourceDefaultTextureAtlasName()
    {
        string atlasPath = LegacyCinematicAtlasContainer.GetAtlasPathForTexture(
            "world\\jd5\\PrinceAli\\graph\\textures\\m_floor_02.png");

        Assert.Equal("world/jd5/princeali/graph/textures/m_floor_02.atl", atlasPath);
    }

    [Fact]
    public void MaterialReader_ReadsSourceAnimatedUvTranslation()
    {
        LegacyCinematicUvModifier modifier = new(
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

        LegacyCinematicMaterial material = LegacyCinematicMaterialReader.Read(bytes);
        LegacyCinematicUvSampler sampler = material.CreateLayer0Sampler(4.8);

        Assert.Equal(2, material.BlendMode);
        Assert.Equal(TextureAddressMode.Wrap, sampler.AddressModeU);
        Assert.Equal(TextureAddressMode.Clamp, sampler.AddressModeV);
        Assert.True(sampler.Enabled);
        Assert.Equal(2, sampler.BlendMode);
        Assert.Equal(LegacyCinematicTextureUsage.EntireTexture, sampler.TextureUsage);
        AssertClose(1, sampler.DiffuseColor.Red);
        AssertClose(1, sampler.DiffuseColor.Green);
        AssertClose(1, sampler.DiffuseColor.Blue);
        AssertClose(1, sampler.DiffuseColor.Alpha);
        LegacyCinematicUvModifierState state = Assert.Single(sampler.Modifiers);
        AssertClose(-0.0816, state.TranslationU, tolerance: 0.00001);
    }

    [Fact]
    public void MaterialReader_AllDisabledLegacyAddAlphaFallsBackToDiffuseTexture()
    {
        LegacyCinematicUvModifier modifier = new(
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

        LegacyCinematicMaterial material = LegacyCinematicMaterialReader.Read(bytes);
        LegacyCinematicUvSampler sampler = material.CreateLayer0Sampler(0);

        Assert.Equal(7, material.BlendMode);
        Assert.True(sampler.Enabled);
        Assert.Equal(LegacyCinematicTextureUsage.EntireTexture, sampler.TextureUsage);
    }

    [Fact]
    public void MaterialReader_ReadsDiffuseColorInSourceSerializedOrder()
    {
        LegacyCinematicUvModifier modifier = new(
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
            diffuseColor: new LegacyCinematicMaterialColor(0.64313728, 0.32549021, 0.019607844, 1));

        LegacyCinematicMaterial material = LegacyCinematicMaterialReader.Read(bytes);
        LegacyCinematicUvSampler sampler = material.CreateLayer0Sampler(0);

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
        bool created = LegacyCinematicMaterialReader.TryCreateShaderFallback(shaderPath, out LegacyCinematicMaterial material);

        Assert.True(created);
        Assert.Equal(expectedBlendMode, material.BlendMode);
        Assert.True(material.CreateLayer0Sampler(0).Enabled);
    }

    [Fact]
    public void MaterialReader_DoesNotCreateShaderFallbackForNonShaderPaths()
    {
        bool created = LegacyCinematicMaterialReader.TryCreateShaderFallback(
            "world/jd5/princeali/graph/materials/p_gilding.mat",
            out _);

        Assert.False(created);
    }

    [Fact]
    public void ParticleTemplateReader_ReadsSourceSerializedParameterBlock()
    {
        byte[] bytes = CreateParticleTemplateBytes();

        bool read = LegacyCinematicParticleTemplateReader.TryRead(bytes, out LegacyCinematicParticleTemplate template);

        Assert.True(read);
        Assert.Equal(0x40, template.Parameters.SourceOffset);
        Assert.Equal((uint)5, template.Parameters.MaxParticles);
        Assert.Equal(uint.MaxValue, template.Parameters.EmitParticlesCount);
        Assert.True(template.Parameters.RenderInReflection);
        AssertClose(-90, template.Parameters.VelocityAngle);
        AssertClose(-3, template.Parameters.Gravity.Y);
        AssertClose(0.05, template.Parameters.Frequency);
        Assert.Equal((uint)2, template.Parameters.PhaseCount);
        AssertClose(-1, template.Parameters.GenerationBox.Min.X);
        AssertClose(20, template.Parameters.BoundingBox.Max.Y);
        AssertClose(0.7, template.Parameters.UniformScale);
        Assert.True(template.Parameters.UseActorTranslation);
        Assert.Equal(2, template.Phases.Count);
        AssertClose(0.3, template.Phases[0].PhaseTime);
        AssertClose(0, template.Phases[0].ColorMin.Alpha);
        Assert.Equal(35, template.Phases[0].AnimEnd);
        AssertClose(2.75, template.Phases[1].PhaseTime);
        Assert.Equal(24, template.Phases[1].AnimEnd);
    }

    [Fact]
    public void ParticleTemplateReader_RejectsNonParticleMaterialBytes()
    {
        LegacyCinematicUvModifier modifier = new(
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
        byte[] bytes = CreateMaterialBytes(TextureAddressMode.Wrap, TextureAddressMode.Wrap, modifier);

        bool read = LegacyCinematicParticleTemplateReader.TryRead(bytes, out _);

        Assert.False(read);
    }

    [Fact]
    public void AtlasGeometry_RectangleAtlasUsesTwoWorldUnitQuad()
    {
        LegacyCinematicAtlas atlas = new(
            8,
            2,
            new Dictionary<int, LegacyCinematicUvData>
            {
                [0] = new([new(0, 0), new(1, 1)])
            },
            new Dictionary<int, LegacyCinematicUvParameters>());

        bool created = LegacyCinematicAtlasContainer.TryCreateGeometry(atlas, 0, out RenderGeometry geometry);

        Assert.True(created);
        Assert.Equal(2.0, geometry.WidthWorld);
        Assert.Equal(2.0, geometry.HeightWorld);
        Assert.Equal(CinematicGeometrySource.RectangleAtlasQuad, geometry.Source);
    }

    [Fact]
    public void AtlasGeometry_PointListUsesAtlasWidthAndHeight()
    {
        LegacyCinematicAtlas atlas = new(
            8,
            2,
            new Dictionary<int, LegacyCinematicUvData>
            {
                [0] = new([new(0, 0), new(1, 0), new(1, 1), new(0, 1)])
            },
            new Dictionary<int, LegacyCinematicUvParameters>());

        bool created = LegacyCinematicAtlasContainer.TryCreateGeometry(atlas, 0, out RenderGeometry geometry);

        Assert.True(created);
        AssertClose(8, geometry.WidthWorld);
        AssertClose(2, geometry.HeightWorld);
        Assert.Equal(CinematicGeometrySource.MeshAtlas, geometry.Source);
    }

    [Fact]
    public void AtlasGeometry_PointListTriangulatesConcaveNgon()
    {
        LegacyCinematicAtlas atlas = new(
            1,
            1,
            new Dictionary<int, LegacyCinematicUvData>
            {
                [0] = new([new(0, 0), new(1, 0), new(0.6f, 0.5f), new(1, 1), new(0, 1)])
            },
            new Dictionary<int, LegacyCinematicUvParameters>());

        bool created = LegacyCinematicAtlasContainer.TryCreateGeometry(atlas, 0, out RenderGeometry geometry);

        Assert.True(created);
        Assert.Equal(9, geometry.Indices.Count);
        Assert.NotEqual([0, 1, 2, 0, 2, 3, 0, 3, 4], geometry.Indices);
    }

    [Fact]
    public void AtlasGeometry_AppliesUvParameterOffsetsAndDepth()
    {
        LegacyCinematicAtlas atlas = new(
            8,
            2,
            new Dictionary<int, LegacyCinematicUvData>
            {
                [0] = new([new(0, 0), new(1, 0), new(1, 1)])
            },
            new Dictionary<int, LegacyCinematicUvParameters>
            {
                [0] = new(
                    0,
                    string.Empty,
                    [
                        new(0, 0.5f, 0xFFFFFFFF, 2.0f, -1.0f, 0),
                        new(0, 0.0f, 0xFFFFFFFF, 0.0f, 0.0f, 0),
                        new(0, 0.0f, 0xFFFFFFFF, 0.0f, 0.0f, 0)
                    ],
                    [new(0, 1, 2)],
                    HasWeight: false,
                    HasColor: false)
            });

        bool created = LegacyCinematicAtlasContainer.TryCreateGeometry(atlas, 0, out RenderGeometry geometry);

        Assert.True(created);
        AssertClose(-2, geometry.Vertices[0].X);
        AssertClose(0, geometry.Vertices[0].Y);
        AssertClose(0.5, geometry.Vertices[0].Z);
    }

    [Fact]
    public void AtlasContainer_LooksUpLegacyTexturePathKeysWhenAtlKeyIsAbsent()
    {
        string texturePath = "world/jd5/princeali/graph/textures/m_floor_02.png";
        LegacyCinematicAtlas atlas = new(
            8,
            2,
            new Dictionary<int, LegacyCinematicUvData>
            {
                [0] = new([new(0, 0), new(1, 0), new(1, 1), new(0, 1)])
            },
            new Dictionary<int, LegacyCinematicUvParameters>());
        LegacyCinematicAtlasContainer container = new(new Dictionary<uint, LegacyCinematicAtlas>
        {
            [LegacyCinematicStringId.Compute(texturePath)] = atlas
        });

        bool created = container.TryCreateGeometry(texturePath, 0, out RenderGeometry geometry, out string atlasPath);

        Assert.True(created);
        Assert.Equal("world/jd5/princeali/graph/textures/m_floor_02.atl", atlasPath);
        Assert.Equal(CinematicGeometrySource.MeshAtlas, geometry.Source);
    }

    [Fact]
    public void SubSceneTransform_UsesPickableInitialWorldFormula()
    {
        float angle = MathF.PI / 2.0f;
        LegacyCinematicActor subScene = CreateActor(
            ["root"],
            typeId: LegacyBinarySerializer.GetTypeId<LegacyCinematicSubSceneActorBinary>(),
            subScenePath: "world/maps/test/child.isc",
            x: 10,
            y: 20,
            z: 3,
            scaleX: 2,
            scaleY: 3,
            angle: angle);
        LegacyCinematicActor child = CreateActor(
            ["root", "child"],
            x: 1,
            y: 2,
            z: 4,
            scaleX: 5,
            scaleY: 6,
            angle: 0.25f);
        LegacyCinematicRenderRuntime runtime = new(new LegacyCinematicScene([subScene, child]));

        ResolvedActorState state = LegacyCinematicRenderEngine.ResolveActorState(
            child,
            runtime,
            PropertyClipIndex.Empty,
            frame: 0);

        AssertClose(4, state.PositionX);
        AssertClose(22, state.PositionY);
        AssertClose(7, state.PositionZ);
        AssertClose(10, state.ScaleX);
        AssertClose(18, state.ScaleY);
        AssertClose(angle + 0.25f, state.Angle);
    }

    [Fact]
    public void SubSceneTransform_PropagatesInitialFlipLikePickable()
    {
        LegacyCinematicActor subScene = CreateActor(
            ["root"],
            typeId: LegacyBinarySerializer.GetTypeId<LegacyCinematicSubSceneActorBinary>(),
            subScenePath: "world/maps/test/child.isc",
            scaleX: 2,
            scaleY: 1,
            xFlipped: 1);
        LegacyCinematicActor child = CreateActor(["root", "child"], x: 1, angle: 0.25f);
        LegacyCinematicRenderRuntime runtime = new(new LegacyCinematicScene([subScene, child]));

        ResolvedActorState state = LegacyCinematicRenderEngine.ResolveActorState(
            child,
            runtime,
            PropertyClipIndex.Empty,
            frame: 0);

        AssertClose(-2, state.PositionX);
        AssertClose(-0.25f, state.Angle);
        Assert.True(state.XFlipped);
        Assert.True(state.ScaleX < 0);
    }

    [Fact]
    public void BindTransform_UsesParentScaleRotationAndCombineScale()
    {
        float angle = MathF.PI / 2.0f;
        LegacyCinematicActor parent = CreateActor(
            ["parent"],
            x: 10,
            y: 0,
            scaleX: 2,
            scaleY: 3,
            angle: angle);
        LegacyCinematicActor child = CreateActor(
            ["child"],
            bind: new LegacyCinematicActorBind(
                "parent",
                OffsetX: 1,
                OffsetY: 2,
                OffsetZ: 5,
                OffsetAngle: 0.25f,
                LocalScaleX: 4,
                LocalScaleY: 5,
                UseParentFlip: 0,
                ScaleInheritProp: LegacyCinematicConstants.BindScaleInheritCombine,
                UseParentAlpha: 1,
                UseParentColor: 1));
        LegacyCinematicRenderRuntime runtime = new(new LegacyCinematicScene([parent, child]));

        ResolvedActorState state = LegacyCinematicRenderEngine.ResolveActorState(
            child,
            runtime,
            PropertyClipIndex.Empty,
            frame: 0);

        AssertClose(4, state.PositionX);
        AssertClose(2, state.PositionY);
        AssertClose(5, state.PositionZ);
        AssertClose(8, state.ScaleX);
        AssertClose(15, state.ScaleY);
        AssertClose(angle + 0.25f, state.Angle);
    }

    [Theory]
    [InlineData(LegacyCinematicConstants.BindScaleInheritUseParent, 2, 3)]
    [InlineData(LegacyCinematicConstants.BindScaleInheritUseChild, 7, 11)]
    [InlineData(LegacyCinematicConstants.BindScaleInheritCombine, 14, 33)]
    public void BindTransform_AppliesSourceScaleInheritanceModes(int scaleMode, float expectedX, float expectedY)
    {
        LegacyCinematicActor parent = CreateActor(["parent"], scaleX: 2, scaleY: 3);
        LegacyCinematicActor child = CreateActor(
            ["child"],
            scaleX: 7,
            scaleY: 11,
            bind: new LegacyCinematicActorBind(
                "parent",
                OffsetX: 0,
                OffsetY: 0,
                OffsetZ: 0,
                OffsetAngle: 0,
                LocalScaleX: 7,
                LocalScaleY: 11,
                UseParentFlip: 0,
                ScaleInheritProp: scaleMode,
                UseParentAlpha: 0,
                UseParentColor: 0));
        LegacyCinematicRenderRuntime runtime = new(new LegacyCinematicScene([parent, child]));

        ResolvedActorState state = LegacyCinematicRenderEngine.ResolveActorState(
            child,
            runtime,
            PropertyClipIndex.Empty,
            frame: 0);

        AssertClose(expectedX, state.ScaleX);
        AssertClose(expectedY, state.ScaleY);
    }

    [Fact]
    public void PropertyClip_AlphaPersistsPastDurationUntilNextClipOverrides()
    {
        LegacyCinematicActor actor = CreateActor(["actor"]);
        PropertyClipIndex clipIndex = new(new Dictionary<string, List<PropertyClip>>(StringComparer.OrdinalIgnoreCase)
        {
            [actor.Key] =
            [
                CreateAlphaClip(actor, startFrame: 0, durationFrames: 10, alpha: 0, order: 0),
                CreateAlphaClip(actor, startFrame: 20, durationFrames: 5, alpha: 1, order: 1)
            ]
        });
        LegacyCinematicRenderRuntime runtime = new(new LegacyCinematicScene([actor]));

        ResolvedActorState hiddenAfterClipEnd = LegacyCinematicRenderEngine.ResolveActorState(
            actor,
            runtime,
            clipIndex,
            frame: 15);
        ResolvedActorState visibleAfterOverride = LegacyCinematicRenderEngine.ResolveActorState(
            actor,
            runtime,
            clipIndex,
            frame: 22);

        AssertClose(0, hiddenAfterClipEnd.Alpha);
        AssertClose(1, visibleAfterOverride.Alpha);
    }

    [Fact]
    public void PropertyClip_SubSceneAlphaAppliesToChildrenUntilChildClipOverrides()
    {
        LegacyCinematicActor group = CreateActor(
            ["root", "group"],
            typeId: LegacyBinarySerializer.GetTypeId<LegacyCinematicSubSceneActorBinary>());
        LegacyCinematicActor child = CreateActor(["root", "group", "child"]);
        LegacyCinematicScene scene = new([group, child]);
        PropertyClipIndex clipIndex = LegacyCinematicTapeReader.BuildPropertyClipIndex(
            [
                CreateAlphaClip(group, startFrame: 0, durationFrames: 10, alpha: 0, order: 0),
                CreateAlphaClip(child, startFrame: 20, durationFrames: 5, alpha: 1, order: 1)
            ],
            scene,
            NullLogger.Instance);
        LegacyCinematicRenderRuntime runtime = new(scene);

        ResolvedActorState hiddenByGroupClip = LegacyCinematicRenderEngine.ResolveActorState(
            child,
            runtime,
            clipIndex,
            frame: 15);
        ResolvedActorState visibleAfterChildOverride = LegacyCinematicRenderEngine.ResolveActorState(
            child,
            runtime,
            clipIndex,
            frame: 22);

        AssertClose(0, hiddenByGroupClip.Alpha);
        AssertClose(1, visibleAfterChildOverride.Alpha);
    }

    [Fact]
    public void Timeline_MapsVideoFramesThroughConductorBeatMarkers()
    {
        LegacyCinematicTimeline timeline = LegacyCinematicTimeline.Create(
            [0, 48000, 96000],
            startBeat: -12,
            videoStartOffsetSeconds: -8.0);

        double firstVideoFrame = timeline.GetTapeFrameForOutputFrame(0, renderStartFrame: 0);
        double beatZeroFrame = timeline.GetTapeFrameForOutputFrame(200, renderStartFrame: 0);
        double beatOneFrame = timeline.GetTapeFrameForOutputFrame(225, renderStartFrame: 0);

        AssertClose(-288, firstVideoFrame);
        AssertClose(0, beatZeroFrame);
        AssertClose(24, beatOneFrame);
    }

    [Fact]
    public void Projection_UsesDefaultPerspectiveCamera()
    {
        RenderGeometry geometry = new(2, 2, 0, 0, CinematicGeometrySource.NoAtlasQuad);
        ResolvedActorState state = new(0, 0, 0, 1, 1, 0, 1, RgbTint.White, false);

        ProjectedQuad quad = LegacyCinematicRenderEngine.ProjectQuad(geometry, state, 1920, 1080);

        AssertClose(960, (quad.Bounds.Left + quad.Bounds.Right) / 2.0);
        AssertClose(540, (quad.Bounds.Top + quad.Bounds.Bottom) / 2.0);
        AssertClose(487, quad.Bounds.Width, tolerance: 1.5);
        AssertClose(487, quad.Bounds.Height, tolerance: 1.5);
    }

    [Fact]
    public void Projection_AppliesCustomAnchorBeforeActorTransform()
    {
        RenderGeometry geometry = new(2, 2, 0, 0, CinematicGeometrySource.NoAtlasQuad);
        ResolvedActorState state = new(0, 0, 0, 1, 1, 0, 1, RgbTint.White, false);

        ProjectedQuad unanchored = LegacyCinematicRenderEngine.ProjectQuad(geometry, state, 1920, 1080);
        ProjectedQuad anchored = LegacyCinematicRenderEngine.ProjectQuad(
            geometry,
            state,
            1920,
            1080,
            customAnchorX: 1.0f,
            customAnchorY: 0.5f,
            anchor: TextureAnchor.Custom);

        double unanchoredCenterX = (unanchored.Bounds.Left + unanchored.Bounds.Right) / 2.0;
        double unanchoredCenterY = (unanchored.Bounds.Top + unanchored.Bounds.Bottom) / 2.0;
        double anchoredCenterX = (anchored.Bounds.Left + anchored.Bounds.Right) / 2.0;
        double anchoredCenterY = (anchored.Bounds.Top + anchored.Bounds.Bottom) / 2.0;

        Assert.True(anchoredCenterX > unanchoredCenterX);
        Assert.True(anchoredCenterY < unanchoredCenterY);
    }

    [Fact]
    public void Projection_AppliesSourceTextureAnchorBeforeActorTransform()
    {
        RenderGeometry geometry = new(2, 2, 0, 0, CinematicGeometrySource.NoAtlasQuad);
        ResolvedActorState state = new(0, 0, 0, 1, 1, 0, 1, RgbTint.White, false);

        ProjectedQuad unanchored = LegacyCinematicRenderEngine.ProjectQuad(geometry, state, 1920, 1080);
        ProjectedQuad middleLeft = LegacyCinematicRenderEngine.ProjectQuad(
            geometry,
            state,
            1920,
            1080,
            anchor: TextureAnchor.MiddleLeft);

        double unanchoredCenterX = (unanchored.Bounds.Left + unanchored.Bounds.Right) / 2.0;
        double middleLeftCenterX = (middleLeft.Bounds.Left + middleLeft.Bounds.Right) / 2.0;

        Assert.True(middleLeftCenterX > unanchoredCenterX);
    }

    [Fact]
    public void Renderer_DrawsHigherDepthAfterLowerDepthInUnifiedList()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor back = CreateActor(["back"], z: 0, texturePath: "back.png", atlasIndex: 1);
            LegacyCinematicActor front = CreateActor(["front"], z: 1, texturePath: "front.png", atlasIndex: 1);
            using MaterializedCinematicImage backImage = CreateSolidImage(back.Key, back.TexturePath!, CreateColor(red: 0, green: 0, blue: 255));
            using MaterializedCinematicImage frontImage = CreateSolidImage(front.Key, front.TexturePath!, CreateColor(red: 255, green: 0, blue: 0));
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [back.Key] = backImage,
                [front.Key] = frontImage
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([back, front]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.R > center.B);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_DoesNotUseAtlasIndexAsDrawPriority()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor back = CreateActor(["back"], z: 0, texturePath: "back.png", atlasIndex: 99);
            LegacyCinematicActor front = CreateActor(["front"], z: 1, texturePath: "front.png", atlasIndex: 0);
            using MaterializedCinematicImage backImage = CreateSolidImage(back.Key, back.TexturePath!, CreateColor(red: 0, green: 0, blue: 255));
            using MaterializedCinematicImage frontImage = CreateSolidImage(front.Key, front.TexturePath!, CreateColor(red: 255, green: 0, blue: 0));
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [back.Key] = backImage,
                [front.Key] = frontImage
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([back, front]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.R > center.B);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_UsesSourceOffsetAsDescendingPrimitiveAddressProxyWhenDepthMatches()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor lowerAddress = CreateActor(["lower"], z: 0, texturePath: "lower.png", sourceOffset: 1);
            LegacyCinematicActor higherAddress = CreateActor(["higher"], z: 0, texturePath: "higher.png", sourceOffset: 2);
            using MaterializedCinematicImage lowerImage = CreateSolidImage(lowerAddress.Key, lowerAddress.TexturePath!, CreateColor(red: 255, green: 0, blue: 0));
            using MaterializedCinematicImage higherImage = CreateSolidImage(higherAddress.Key, higherAddress.TexturePath!, CreateColor(red: 0, green: 0, blue: 255));
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [lowerAddress.Key] = lowerImage,
                [higherAddress.Key] = higherImage
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([lowerAddress, higherAddress]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.R > center.B);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_MultiplyBlendTreatsWhiteAsTransparentAndBlackAsShadow()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor back = CreateActor(["back"], z: 0, texturePath: "back.png", sourceOffset: 1);
            LegacyCinematicActor shadow = CreateActor(["shadow"], z: 1, texturePath: "shadow.png", sourceOffset: 2);
            using MaterializedCinematicImage backImage = CreateSolidImage(back.Key, back.TexturePath!, CreateColor(red: 220, green: 120, blue: 40));
            using Image<Bgra32> shadowSource = CreateSplitWhiteBlackImage();
            LegacyCinematicMaterial multiplyMaterial = new(
                10,
                [LegacyCinematicMaterialLayer.Default]);
            using MaterializedCinematicImage shadowImage = new(
                shadow.Key,
                shadow.TexturePath!,
                shadowSource.Clone(),
                RenderGeometry.NoAtlasQuad,
                multiplyMaterial);
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [back.Key] = backImage,
                [shadow.Key] = shadowImage
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([back, shadow]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 whiteHalf = rendered[24, 32];
            Bgra32 blackHalf = rendered[40, 32];

            Assert.True(whiteHalf.R > 180 && whiteHalf.G > 90 && whiteHalf.B > 20);
            Assert.True(blackHalf.R < 20 && blackHalf.G < 20 && blackHalf.B < 20);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void RenderFrameSequence_AppliesMaterialUvElapsedFromOutputFrame()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor actor = CreateActor(
                ["root", "cloud"],
                texturePath: "world/maps/test/graph/textures/cloud.png");
            using Image<Bgra32> source = CreateSplitImage();
            LegacyCinematicUvModifier modifier = new(
                TranslationU: -0.5f,
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
            LegacyCinematicMaterial material = new(
                2,
                [new LegacyCinematicMaterialLayer(true, TextureAddressMode.Wrap, TextureAddressMode.Clamp, [modifier])]);
            using MaterializedCinematicImage materialized = new(
                actor.Key,
                actor.TexturePath!,
                source.Clone(),
                RenderGeometry.NoAtlasQuad,
                material);
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [actor.Key] = materialized
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([actor]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 26,
                renderStartFrame: 0,
                NullLogger.Instance,
                materialTimeStartFrame: -60);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00025.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.R > 180 && center.B < 80, $"Expected UV-scrolled red half, got R={center.R} B={center.B}.");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void RenderCompositeFrameSequence_SamplesPleoFramesWithObservedOpeningOffset()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string sourceFrames = Path.Combine(temp, "source");
        string outputFrames = Path.Combine(temp, "output");
        Directory.CreateDirectory(sourceFrames);

        try
        {
            CreateStackedPleoSourceFrame(Path.Combine(sourceFrames, "source_00001.png"), CreateColor(255, 0, 0));
            LegacyCinematicActor video = CreateActor(
                ["VideoOutput"],
                visualComponentTypeId: LegacyBinarySerializer.GetTypeId<LegacyCinematicPleoTextureGraphicComponentBinary>());

            LegacyCinematicFrameRenderer.RenderCompositeFrameSequence(
                new LegacyCinematicScene([video]),
                new Dictionary<string, MaterializedCinematicImage>(StringComparer.OrdinalIgnoreCase),
                PropertyClipIndex.Empty,
                sourceFrames,
                outputFrames,
                sourceWidth: 8,
                visibleHeight: 8,
                alphaHeight: 4,
                videoOutputWidth: 64,
                videoOutputHeight: 64,
                videoStartOffsetSeconds: -8.465,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 4,
                renderStartFrame: 0,
                materialTimeStartFrame: 0,
                materialTimeOffsetSeconds: 0,
                NullLogger.Instance);

            using Image<Bgra32> frame0 = Image.Load<Bgra32>(Path.Combine(outputFrames, "frame_00000.png"));
            using Image<Bgra32> frame1 = Image.Load<Bgra32>(Path.Combine(outputFrames, "frame_00001.png"));
            using Image<Bgra32> frame2 = Image.Load<Bgra32>(Path.Combine(outputFrames, "frame_00002.png"));

            Assert.True(frame0[32, 32].R < 10 && frame0[32, 32].G < 10 && frame0[32, 32].B < 10);
            Assert.True(frame1[32, 32].R > 180 && frame1[32, 32].B < 80);
            Assert.True(frame2[32, 32].R < 10 && frame2[32, 32].G < 10 && frame2[32, 32].B < 10);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_AddAlphaBlendBrightensDestinationWithSourceAlpha()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor back = CreateActor(["back"], z: 0, texturePath: "back.png", sourceOffset: 1);
            LegacyCinematicActor glow = CreateActor(["glow"], z: 1, texturePath: "glow.png", sourceOffset: 2);
            using MaterializedCinematicImage backImage = CreateSolidImage(back.Key, back.TexturePath!, CreateColor(red: 20, green: 30, blue: 40));
            using MaterializedCinematicImage glowImage = CreateSolidImage(
                glow.Key,
                glow.TexturePath!,
                CreateColor(red: 200, green: 0, blue: 0, alpha: 128),
                new LegacyCinematicMaterial(7, [LegacyCinematicMaterialLayer.Default]));
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [back.Key] = backImage,
                [glow.Key] = glowImage
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([back, glow]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.R > 100 && center.G >= 25 && center.B >= 35);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_MaterialLayerDiffuseColorModulatesEffectTexture()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor glow = CreateActor(["glow"], z: 0, texturePath: "glow.png");
            LegacyCinematicMaterial material = new(
                7,
                [
                    new LegacyCinematicMaterialLayer(
                        true,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        [],
                        BlendMode: 2,
                        TextureUsage: LegacyCinematicTextureUsage.EntireTexture,
                        DiffuseColor: new LegacyCinematicMaterialColor(0.25, 0, 0, 0.5))
                ]);
            using MaterializedCinematicImage image = CreateSolidImage(
                glow.Key,
                glow.TexturePath!,
                CreateColor(red: 255, green: 255, blue: 255),
                material);
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [glow.Key] = image
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([glow]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.InRange(center.R, 20, 45);
            Assert.True(center.G < 5 && center.B < 5);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_MaterialLayersSampleTextureSetSlotsInLayerOrder()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor actor = CreateActor(["effect"], z: 0, texturePath: "effect_1.png");
            LegacyCinematicMaterial material = new(
                2,
                [
                    new LegacyCinematicMaterialLayer(
                        false,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        []),
                    new LegacyCinematicMaterialLayer(
                        true,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        [],
                        BlendMode: 2)
                ]);
            Image<Bgra32> red = new(8, 8, CreateColor(red: 255, green: 0, blue: 0));
            Image<Bgra32> blue = new(8, 8, CreateColor(red: 0, green: 0, blue: 255));
            using MaterializedCinematicImage image = new(
                actor.Key,
                actor.TexturePath!,
                red,
                RenderGeometry.NoAtlasQuad,
                material,
                [
                    MaterializedCinematicTexture.Create("effect_1.png", red),
                    MaterializedCinematicTexture.Create("effect_2.png", blue)
                ]);
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [actor.Key] = image
            };

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([actor]),
                images,
                PropertyClipIndex.Empty,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.B > 180 && center.R < 20, $"Expected layer 1 to sample the second texture, got R={center.R} B={center.B}.");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Renderer_MaterialLayerEnableClipOverridesInitialLayerState()
    {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);

        try
        {
            LegacyCinematicActor actor = CreateActor(["actor"], texturePath: "actor.png");
            LegacyCinematicMaterial material = new(
                2,
                [
                    new LegacyCinematicMaterialLayer(
                        false,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        [])
                ]);
            using MaterializedCinematicImage image = CreateSolidImage(
                actor.Key,
                actor.TexturePath!,
                CreateColor(red: 255, green: 0, blue: 0),
                material);
            Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
            {
                [actor.Key] = image
            };
            PropertyClipIndex clipIndex = new(new Dictionary<string, List<PropertyClip>>(StringComparer.OrdinalIgnoreCase)
            {
                [actor.Key] =
                [
                    new PropertyClip(
                        new ActorTargetPath(actor.Path),
                        LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicEnableLayerClipBinary>(),
                        StartFrame: 0,
                        DurationFrames: 10,
                        State: new CinematicVisualState(null, null, new CinematicLayerEnable(0, true)),
                        Order: 0)
                ]
            });

            LegacyCinematicFrameRenderer.RenderFrameSequence(
                new LegacyCinematicScene([actor]),
                images,
                clipIndex,
                temp,
                CinematicLayerPlane.Background,
                outputWidth: 64,
                outputHeight: 64,
                frameCount: 1,
                renderStartFrame: 0,
                NullLogger.Instance);

            using Image<Bgra32> rendered = Image.Load<Bgra32>(Path.Combine(temp, "frame_00000.png"));
            Bgra32 center = rendered[32, 32];

            Assert.True(center.R > 180 && center.G < 20 && center.B < 20);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static MaterializedCinematicImage CreateSolidImage(string key, string texturePath, Bgra32 color)
    {
        Image<Bgra32> image = new(8, 8, color);
        return new MaterializedCinematicImage(key, texturePath, image);
    }

    private static MaterializedCinematicImage CreateSolidImage(
        string key,
        string texturePath,
        Bgra32 color,
        LegacyCinematicMaterial material)
    {
        Image<Bgra32> image = new(8, 8, color);
        return new MaterializedCinematicImage(key, texturePath, image, RenderGeometry.NoAtlasQuad, material);
    }

    private static void CreateStackedPleoSourceFrame(string path, Bgra32 color)
    {
        using Image<Bgra32> image = new(8, 12);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = y < 8 ? color : CreateColor(255, 255, 255);
            }
        });
        image.Save(path);
    }

    private static Image<Bgra32> CreateSplitImage()
    {
        Image<Bgra32> image = new(8, 8);
        Bgra32 red = CreateColor(255, 0, 0);
        Bgra32 blue = CreateColor(0, 0, 255);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = x < row.Length / 2 ? red : blue;
            }
        });
        return image;
    }

    private static Image<Bgra32> CreateSplitWhiteBlackImage()
    {
        Image<Bgra32> image = new(8, 8);
        Bgra32 white = CreateColor(255, 255, 255);
        Bgra32 black = CreateColor(0, 0, 0);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Bgra32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = x < row.Length / 2 ? white : black;
            }
        });
        return image;
    }

    private static Bgra32 CreateColor(byte red, byte green, byte blue)
        => CreateColor(red, green, blue, alpha: 255);

    private static Bgra32 CreateColor(byte red, byte green, byte blue, byte alpha)
    {
        Bgra32 color = default;
        color.R = red;
        color.G = green;
        color.B = blue;
        color.A = alpha;
        return color;
    }

    private static PropertyClip CreateAlphaClip(
        LegacyCinematicActor actor,
        int startFrame,
        int durationFrames,
        float alpha,
        int order) =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<LegacyCinematicAlphaClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: null,
                Material: new CinematicMaterial(
                    Red: null,
                    Green: null,
                    Blue: null,
                    Alpha: new CinematicCurve([new CinematicKeyframe(0, alpha, 0, alpha, 0, alpha)]))),
            order);

    private static LegacyCinematicActor CreateActor(
        IReadOnlyList<string> path,
        uint? typeId = null,
        string? subScenePath = null,
        float x = 0,
        float y = 0,
        float z = 0,
        float scaleX = 1,
        float scaleY = 1,
        uint xFlipped = 0,
        float angle = 0,
        string? texturePath = null,
        int atlasIndex = 0,
        TextureAnchor anchor = TextureAnchor.MiddleCenter,
        float customAnchorX = 0,
        float customAnchorY = 0,
        int scenePriority = 0,
        int? sourceOffset = null,
        LegacyCinematicActorBind? bind = null,
        uint? visualComponentTypeId = null) =>
        new(
            path,
            path[^1],
            SourceOffset: sourceOffset ?? path.Count,
            SiblingOrder: path.Count,
            typeId ?? LegacyBinarySerializer.GetTypeId<LegacyCinematicSceneActorBinary>(),
            z,
            scaleX,
            scaleY,
            xFlipped,
            angle,
            x,
            y,
            "world/maps/test/template.tpl",
            subScenePath,
            texturePath,
            texturePath == null ? [] : [texturePath],
            texturePath == null ? null : "world/maps/test/material.mat",
            null,
            visualComponentTypeId ?? (texturePath == null ? null : LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicComponentBinary>()),
            atlasIndex,
            anchor,
            customAnchorX,
            customAnchorY,
            scenePriority,
            RgbTint.White,
            1.0f,
            bind);

    private static byte[] CreateMaterialBytes(
        TextureAddressMode addressU,
        TextureAddressMode addressV,
        LegacyCinematicUvModifier modifier,
        bool enabled = true,
        int blendMode = 2,
        LegacyCinematicMaterialColor? diffuseColor = null)
    {
        LegacyCinematicMaterialColor color = diffuseColor ?? LegacyCinematicMaterialColor.White;
        byte[] bytes = new byte[0xC8];
        WriteInt32(bytes, 0, 1);
        WriteInt32(bytes, 4, bytes.Length);
        WriteInt32(bytes, 0x64, blendMode);
        WriteInt32(bytes, 0x68, 0x3C);
        WriteInt32(bytes, 0x6C, enabled ? 1 : 0);
        WriteInt32(bytes, 0x70, (int)addressU);
        WriteInt32(bytes, 0x74, (int)addressV);
        WriteInt32(bytes, 0x78, 1);
        WriteSingle(bytes, 0x7C, (float)color.Blue);
        WriteSingle(bytes, 0x80, (float)color.Green);
        WriteSingle(bytes, 0x84, (float)color.Red);
        WriteSingle(bytes, 0x88, (float)color.Alpha);
        WriteInt32(bytes, 0x8C, 5);
        WriteInt32(bytes, 0x90, 1);
        WriteInt32(bytes, 0x94, 0x30);
        WriteModifier(bytes, 0x98, modifier);
        return bytes;
    }

    private static byte[] CreateParticleTemplateBytes()
    {
        byte[] bytes = new byte[0x280];
        int offset = 0x40;

        void WriteUInt(uint value)
        {
            WriteUInt32(bytes, offset, value);
            offset += 4;
        }

        void WriteInt(int value)
        {
            WriteInt32(bytes, offset, value);
            offset += 4;
        }

        void WriteFloat(float value)
        {
            WriteSingle(bytes, offset, value);
            offset += 4;
        }

        void WriteBool(bool value) => WriteUInt(value ? 1u : 0u);
        void WriteVec2(float x, float y)
        {
            WriteFloat(x);
            WriteFloat(y);
        }

        void WriteVec3(float x, float y, float z)
        {
            WriteFloat(x);
            WriteFloat(y);
            WriteFloat(z);
        }

        void WriteColor(float red, float green, float blue, float alpha)
        {
            WriteFloat(blue);
            WriteFloat(green);
            WriteFloat(red);
            WriteFloat(alpha);
        }

        void WriteBox(float minX, float minY, float maxX, float maxY)
        {
            WriteInt(0x10);
            WriteVec2(minX, minY);
            WriteVec2(maxX, maxY);
        }

        void WritePhase(float phaseTime, float alpha, int animEnd)
        {
            WriteInt(0x54);
            WriteFloat(phaseTime);
            WriteColor(1, 1, 1, alpha);
            WriteColor(1, 1, 1, alpha);
            WriteVec2(0.5f, 0.5f);
            WriteVec2(0.5f, 0.5f);
            WriteInt(0);
            WriteInt(animEnd);
            WriteUInt(uint.MaxValue);
            WriteFloat(0);
            WriteBool(false);
            WriteBool(true);
        }

        WriteUInt(5);
        WriteColor(1, 1, 1, 1);
        WriteUInt(uint.MaxValue);
        WriteBool(false);
        WriteBool(true);
        WriteFloat(-1);
        WriteFloat(-1);
        WriteVec3(0, 0, 0);
        WriteVec2(0, 0);
        WriteFloat(0);
        WriteFloat(-90);
        WriteFloat(0);
        WriteVec3(0, -3, 0);
        WriteVec3(0, 1, 0);
        WriteFloat(0);
        WriteBool(true);
        WriteFloat(2);
        WriteFloat(1);
        WriteFloat(0.05f);
        WriteFloat(0);
        WriteBool(false);
        WriteUInt(1);
        WriteUInt(1);
        WriteUInt(uint.MaxValue);
        WriteFloat(0);
        WriteFloat(MathF.PI);
        WriteFloat(6.981317f);
        WriteFloat(5.235988f);
        WriteFloat(0);
        WriteUInt(2);
        WriteFloat(0.25f);
        WriteFloat(0);
        WriteFloat(4);
        WriteFloat(5);
        WriteVec3(1, 1, 1);
        WriteVec3(0, 0, 0);
        WriteBool(true);
        WriteUInt(1);
        WriteBool(false);
        WriteBox(-1, 0, 1, 0);
        WriteFloat(0);
        WriteUInt(0);
        WriteFloat(0);
        WriteFloat(1);
        WriteFloat(-1);
        WriteBox(-10, -20, 10, 20);
        WriteUInt(0);
        WriteUInt(0);
        WriteUInt(0);
        WriteFloat(0.7f);
        WriteBool(false);
        WriteBool(false);
        WriteUInt(128);
        WriteUInt(128);
        WriteFloat(-MathF.PI);
        WriteFloat(MathF.PI);
        WriteBool(true);
        WriteBool(true);
        WriteBool(true);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteBool(false);
        WriteUInt(10);
        WriteFloat(0);
        WriteUInt(1);
        WriteBool(true);
        WriteBool(true);
        WriteVec2(0, 0);
        WriteBool(false);
        WriteUInt(2);
        WritePhase(0.3f, 0, 35);
        WritePhase(2.75f, 1, 24);

        return bytes;
    }

    private static void WriteModifier(byte[] bytes, int offset, LegacyCinematicUvModifier modifier)
    {
        WriteSingle(bytes, offset, modifier.TranslationU);
        WriteSingle(bytes, offset + 4, modifier.TranslationV);
        WriteInt32(bytes, offset + 8, modifier.AnimTranslationU ? 1 : 0);
        WriteInt32(bytes, offset + 12, modifier.AnimTranslationV ? 1 : 0);
        WriteSingle(bytes, offset + 16, modifier.Rotation);
        WriteSingle(bytes, offset + 20, modifier.RotationOffsetU);
        WriteSingle(bytes, offset + 24, modifier.RotationOffsetV);
        WriteInt32(bytes, offset + 28, modifier.AnimRotation ? 1 : 0);
        WriteSingle(bytes, offset + 32, modifier.ScaleU);
        WriteSingle(bytes, offset + 36, modifier.ScaleV);
        WriteSingle(bytes, offset + 40, modifier.ScaleOffsetU);
        WriteSingle(bytes, offset + 44, modifier.ScaleOffsetV);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), value);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(offset, 4),
            unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void AssertClose(double expected, double actual, double tolerance = 0.0001) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:0.####}, got {actual:0.####}.");
}