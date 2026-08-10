using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using KevInc.UbiArt.Cinematics.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTestSupport;
using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTimelineTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicSceneReaderTests
{
    [Fact]
    public void XmlSceneReader_ReadsEmbeddedPleoVideoScene()
    {
        using JustDanceUbiArtFileSystem fileSystem = CreateJd2016TestFileSystem();
        byte[] sceneBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes("""
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <root>
                <Scene ENGINE_VERSION="222288" GRIDUNIT="0.500000">
                    <ACTORS NAME="SubSceneActor">
                        <SubSceneActor RELATIVEZ="0.000000" SCALE="1.000000 1.000000" xFLIPPED="0" USERFRIENDLY="Song_VIDEO" POS2D="0.000000 0.000000" ANGLE="0.000000" LUA="enginedata/actortemplates/subscene.tpl" RELATIVEPATH="world/maps/song/videoscoach/song_video.isc" EMBED_SCENE="1">
                            <SCENE>
                                <Scene ENGINE_VERSION="222288" GRIDUNIT="0.500000">
                                    <ACTORS NAME="Actor">
                                        <Actor RELATIVEZ="-1.000000" SCALE="1.000000 1.000000" xFLIPPED="0" USERFRIENDLY="VideoScreen" POS2D="0.000000 -4.500000" ANGLE="0.000000" LUA="world/_common/videoscreen/video_player_main.tpl">
                                            <COMPONENTS NAME="PleoComponent">
                                                <PleoComponent video="world/maps/song/videoscoach/song.webm" dashMPD="" channelID="" />
                                            </COMPONENTS>
                                        </Actor>
                                    </ACTORS>
                                    <ACTORS NAME="Actor">
                                        <Actor RELATIVEZ="0.000000" SCALE="3.941238 2.220000" xFLIPPED="0" USERFRIENDLY="VideoOutput" POS2D="0.000000 0.000000" ANGLE="0.000000" LUA="world/_common/videoscreen/video_output_main.tpl">
                                            <COMPONENTS NAME="PleoTextureGraphicComponent">
                                                <PleoTextureGraphicComponent AtlasIndex="0" customAnchor="0.000000 0.000000" SinusAmplitude="0.000000 0.000000 0.000000" SinusSpeed="1.000000" AngleX="0.000000" AngleY="0.000000">
                                                    <PrimitiveParameters>
                                                        <GFXPrimitiveParam colorFactor="1.000000 1.000000 1.000000 1.000000" />
                                                    </PrimitiveParameters>
                                                    <ENUM NAME="anchor" SEL="1" />
                                                    <material>
                                                        <GFXMaterialSerializable ATL_Channel="0" ATL_Path="" shaderPath="world/_common/matshader/pleofullscreen.msh" />
                                                    </material>
                                                </PleoTextureGraphicComponent>
                                            </COMPONENTS>
                                        </Actor>
                                    </ACTORS>
                                </Scene>
                            </SCENE>
                        </SubSceneActor>
                    </ACTORS>
                </Scene>
            </root>
            """);

        IReadOnlyList<CinematicActor> actors = CinematicXmlSceneReader.ReadScene(
            fileSystem,
            sceneBytes,
            [],
            NullLogger.Instance);

        CinematicActor subScene = Assert.Single(actors, actor => actor.Key == "song_video");
        Assert.Null(subScene.VisualComponentTypeId);
        Assert.Null(subScene.PleoVideoPath);

        CinematicActor videoScreen = Assert.Single(actors, actor => actor.Key == "song_video/videoscreen");
        Assert.Equal("world/maps/song/videoscoach/song.webm", videoScreen.PleoVideoPath);

        CinematicActor videoOutput = Assert.Single(actors, actor => actor.Key == "song_video/videooutput");
        Assert.Equal(LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>(), videoOutput.VisualComponentTypeId);
        Assert.Equal(3.941238f, videoOutput.ScaleX, precision: 5);
        Assert.Equal(2.22f, videoOutput.ScaleY, precision: 5);
        Assert.Equal(TextureAnchor.MiddleCenter, videoOutput.Anchor);
    }

    [Fact]
    public void XmlSceneReader_ReadsMesh3DComponentAndTexturePatcher()
    {
        using JustDanceUbiArtFileSystem fileSystem = CreateJd2016TestFileSystem();
        byte[] sceneBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes("""
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <root>
                <Scene ENGINE_VERSION="222288" GRIDUNIT="0.500000">
                    <ACTORS NAME="Actor">
                        <Actor RELATIVEZ="-20.000000" SCALE="1.125000 1.250000" xFLIPPED="0" USERFRIENDLY="TXComponent_8x_Left" POS2D="-8.000000 0.000000" ANGLE="0.000000" LUA="enginedata/actortemplates/tpl_videotexturecomponent3d.tpl">
                            <COMPONENTS NAME="Mesh3DComponent">
                                <Mesh3DComponent ScaleZ="2.500000" mesh3D="world/maps/_mashup/graph/mesh/3d_8x.m3d" orientation="0.000000 1.000000 0.000000 0.000000, -1.000000 0.000000 0.000000 0.000000, 0.000000 0.000000 1.000000 0.000000, 0.000000 0.000000 0.000000 1.000000">
                                    <PrimitiveParameters>
                                        <GFXPrimitiveParam colorFactor="0.250000 0.500000 0.750000 0.875000" />
                                    </PrimitiveParameters>
                                    <materialList>
                                        <GFXMaterialSerializable ATL_Channel="0" ATL_Path="" shaderPath="world/maps/_mashup/graph/materials/video_texture_component_test.msh">
                                            <textureSet>
                                                <GFXMaterialTexturePathSet diffuse="world/maps/_mashup/graph/textures/texture_transparent.png" diffuse_2="world/maps/_mashup/graph/textures/unused_mask.png" diffuse_3="" diffuse_4="" />
                                            </textureSet>
                                        </GFXMaterialSerializable>
                                    </materialList>
                                </Mesh3DComponent>
                            </COMPONENTS>
                            <COMPONENTS NAME="TexturePatcherComponent">
                                <TexturePatcherComponent Diffuse1="pleotexturedyn/mainchannel" Diffuse2="pleotexturedyn/mainchannel" Diffuse3="" Diffuse4="" />
                            </COMPONENTS>
                        </Actor>
                    </ACTORS>
                </Scene>
            </root>
            """);

        IReadOnlyList<CinematicActor> actors = CinematicXmlSceneReader.ReadScene(
            fileSystem,
            sceneBytes,
            ["_mashup_graph"],
            NullLogger.Instance);

        CinematicActor actor = Assert.Single(actors);
        Assert.Equal("_mashup_graph/txcomponent_8x_left", actor.Key);
        Assert.Equal(LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentBinary>(), actor.VisualComponentTypeId);
        Assert.Equal("world/maps/_mashup/graph/mesh/3d_8x.m3d", actor.MeshPath);
        Assert.Equal("world/maps/_mashup/graph/materials/video_texture_component_test.msh", actor.MaterialPath);
        Assert.Equal("pleotexturedyn/mainchannel", actor.TexturePath);
        Assert.Equal(["pleotexturedyn/mainchannel", "pleotexturedyn/mainchannel"], actor.TexturePaths);
        AssertClose(2.5, actor.MeshInitialScaleZ);
        Assert.True(actor.MeshOrientation.HasValue);
        AssertClose(1, actor.MeshOrientation.M12);
        AssertClose(-1, actor.MeshOrientation.M21);
        AssertClose(0.25, actor.BaseTint.Red);
        AssertClose(0.5, actor.BaseTint.Green);
        AssertClose(0.75, actor.BaseTint.Blue);
        AssertClose(0.875, actor.BaseAlpha);
    }

    [Fact]
    public void XmlSceneReader_ConvertsSerializedAnglesToRuntimeRadians()
    {
        using JustDanceUbiArtFileSystem fileSystem = CreateJd2016TestFileSystem();
        byte[] sceneBytes = Encoding.GetEncoding("ISO-8859-1").GetBytes("""
            <?xml version="1.0" encoding="ISO-8859-1"?>
            <root>
                <Scene ENGINE_VERSION="222288" GRIDUNIT="0.500000">
                    <ACTORS NAME="Actor">
                        <Actor RELATIVEZ="-19.270042" SCALE="6.013441 6.083119" xFLIPPED="0" USERFRIENDLY="glow" POS2D="0.000000 0.000000" ANGLE="312.305176" LUA="enginedata/actortemplates/tpl_materialgraphiccomponent2d.tpl">
                            <COMPONENTS NAME="MaterialGraphicComponent">
                                <MaterialGraphicComponent AtlasIndex="0" customAnchor="0.000000 0.000000" SinusAmplitude="0.000000 0.000000 0.000000" SinusSpeed="1.000000" AngleX="90.000000" AngleY="270.000000">
                                    <material>
                                        <GFXMaterialSerializable ATL_Channel="0" ATL_Path="" shaderPath="">
                                            <textureSet>
                                                <GFXMaterialTexturePathSet diffuse="world/maps/_mashup/graph/textures/glow.png" />
                                            </textureSet>
                                        </GFXMaterialSerializable>
                                    </material>
                                </MaterialGraphicComponent>
                            </COMPONENTS>
                        </Actor>
                    </ACTORS>
                </Scene>
            </root>
            """);

        IReadOnlyList<CinematicActor> actors = CinematicXmlSceneReader.ReadScene(
            fileSystem,
            sceneBytes,
            ["_mashup_graph"],
            NullLogger.Instance);

        CinematicActor actor = Assert.Single(actors);
        AssertClose(312.305176 * Math.PI / 180.0, actor.Angle);
        AssertClose(Math.PI / 2.0, actor.Sinus.AngleX);
        AssertClose(3.0 * Math.PI / 2.0, actor.Sinus.AngleY);
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    public void SubSceneReader_UsesExternalSceneWhenEmbedFlagIsFalse(
        bool embedScene,
        bool nextLooksLikeScene,
        bool hasExternalSubScene,
        bool expected)
    {
        bool shouldReadEmbedded = CinematicSceneActorReader.ShouldReadEmbeddedSubScene(
            embedScene,
            nextLooksLikeScene,
            hasExternalSubScene);

        Assert.Equal(expected, shouldReadEmbedded);
    }

    [Fact]
    public void SceneReader_ReadsRootTrailingActorsWhenJd2015CountIsLow()
    {
        using JustDanceUbiArtFileSystem fileSystem = CreateJd2015TestFileSystem();
        byte[] sceneBytes = CreateRootSceneBytes(
            declaredActorCount: 1,
            CreateJd2015SceneActorBytes("FirstActor"),
            CreateJd2015SceneActorBytes("TrailingActor"),
            CreateJd2015SceneActorBytes("InitiallyDisabledActor", defaultEnabled: false));

        SceneReadResult result = CinematicSceneReader.ReadScene(
            fileSystem,
            sceneBytes,
            offset: 0,
            parentPath: ["Test_GRAPH"],
            depth: 0,
            NullLogger.Instance);

        Assert.Contains(result.Actors, actor => actor.Key == "test_graph/firstactor");
        Assert.Contains(result.Actors, actor => actor.Key == "test_graph/trailingactor");
        Assert.True(Assert.Single(result.Actors, actor =>
            actor.Key == "test_graph/initiallydisabledactor").DefaultEnabled);

        static JustDanceUbiArtFileSystem CreateJd2015TestFileSystem()
        {
            UbiArtConversionRequest request = new(Path.GetTempPath(), Path.GetTempPath(), "test")
            {
                Type = CookedType.Cooked,
                ImportPlatform = UbiArtPlatform.Cafe,
                ImportEngineVersion = UbiArtEngineVersion.JD2015
            };
            UbiArtVersionProfile profile = new(
                UbiArtPlatform.Cafe,
                UbiArtEngineVersion.JD2015,
                new JD2015LayoutResolver(),
                new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2015));
            return new JustDanceUbiArtFileSystem(
                request,
                profile,
                NullLogger<JustDanceUbiArtFileSystem>.Instance);
        }

        static byte[] CreateRootSceneBytes(int declaredActorCount, params byte[][] actorBytes)
        {
            using MemoryStream stream = new();
            WriteInt(stream, 1);
            WriteUInt(stream, 0);
            WriteInt(stream, 0);
            WriteInt(stream, 0);
            WriteInt(stream, 0);
            WriteInt(stream, declaredActorCount);
            foreach (byte[] actor in actorBytes)
                stream.Write(actor);

            return stream.ToArray();
        }

        static byte[] CreateJd2015SceneActorBytes(string name, bool defaultEnabled = true)
        {
            using MemoryStream stream = new();
            WriteUInt(stream, LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>());
            WriteFloat(stream, 0);
            WriteFloat(stream, 1);
            WriteFloat(stream, 1);
            WriteUInt(stream, 0);
            WriteString(stream, name);
            WriteUInt(stream, defaultEnabled ? 1u : 0u);
            WriteFloat(stream, 0);
            WriteFloat(stream, 0);
            WriteFloat(stream, 0);
            WritePathFileFirst(stream, "unused.tpl", "world/maps/test/", 0);
            WriteUInt(stream, 0);
            WritePathFileFirst(stream, "template.tpl", "world/maps/test/", 0);
            WriteInt(stream, 0);
            WriteInt(stream, 0);
            WriteInt(stream, 0);
            return stream.ToArray();
        }

        static void WritePathFileFirst(Stream stream, string fileName, string folder, uint resourceId)
        {
            WriteString(stream, fileName);
            WriteString(stream, folder);
            WriteUInt(stream, resourceId);
        }

        static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteUInt(stream, (uint)bytes.Length);
            stream.Write(bytes);
        }

        static void WriteInt(Stream stream, int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        static void WriteUInt(Stream stream, uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }

        static void WriteFloat(Stream stream, float value) =>
            WriteUInt(stream, unchecked((uint)BitConverter.SingleToInt32Bits(value)));
    }

    [Fact]
    public void ActorBuilder_SkipsGameplayTimelineAndMenuArtActors()
    {
        CinematicActor graphActor = CreateActor(
            ["Map_GRAPH", "Spark"],
            texturePath: "world/jd5/map/graph/textures/spark.png");
        CinematicActor pictoActor = CreateActor(
            ["Map_TML", "timeline: Map", "KissObject"],
            texturePath: "world/jd5/map/timeline/pictos/kissyou_object.tga");
        CinematicActor menuActor = CreateActor(
            ["Map_MENUART", "Cover"],
            texturePath: "world/jd5/map/menuart/textures/cover.tga");

        using MaterializedCinematicImage graphImage = new(graphActor.Key, graphActor.TexturePath!, new Image<Bgra32>(16, 16));
        using MaterializedCinematicImage pictoImage = new(pictoActor.Key, pictoActor.TexturePath!, new Image<Bgra32>(16, 16));
        using MaterializedCinematicImage menuImage = new(menuActor.Key, menuActor.TexturePath!, new Image<Bgra32>(16, 16));

        Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
        {
            [graphActor.Key] = graphImage,
            [pictoActor.Key] = pictoImage,
            [menuActor.Key] = menuImage
        };

        IReadOnlyList<RenderableCinematicActor> renderables = CinematicActorBuilder.BuildRenderableActors(
            new CinematicScene([graphActor, pictoActor, menuActor]),
            images,
            actorFilter: CinematicJustDanceActorFilter.ShouldRenderActor);

        RenderableCinematicActor renderable = Assert.Single(renderables);
        Assert.Equal(graphActor.Key, renderable.Actor.Key);
    }

    [Fact]
    public void ActorBuilder_PrefersRootVideoOutputOverMisnestedVideoOutput()
    {
        CinematicActor misnestedVideo = CreateActor(
            ["Map_GRAPH", "Clouds", "Map_VIDEO", "VideoOutput"],
            typeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>());
        CinematicActor rootVideo = CreateActor(
            ["Map_VIDEO", "VideoOutput"],
            typeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>());

        IReadOnlyList<CinematicActor> selected = CinematicActorBuilder.SelectVideoOutputActors(
            [misnestedVideo, rootVideo]);

        CinematicActor actor = Assert.Single(selected);
        Assert.Equal(rootVideo.Key, actor.Key);
    }

    [Fact]
    public void ActorBuilder_IncludesDynamicPleoTextureActorsWithRootVideoOutput()
    {
        CinematicActor rootVideo = CreateActor(
            ["Map_VIDEO", "VideoOutput"],
            typeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>());
        CinematicActor dynamicVideo = CreateActor(
            ["Map_VIDEO", "video_texture_component"],
            texturePath: "pleotexturedyn/mainchannel",
            z: -4.5f);

        IReadOnlyList<RenderableCinematicActor> renderables = CinematicActorBuilder.BuildRenderableActors(
            new CinematicScene([rootVideo, dynamicVideo]),
            new Dictionary<string, MaterializedCinematicImage>(StringComparer.OrdinalIgnoreCase),
            includeVideoOutput: true);

        Assert.Equal(2, renderables.Count);
        Assert.Contains(renderables, renderable =>
            renderable.Actor.Key == rootVideo.Key &&
            renderable.RenderKind == CinematicRenderKind.PleoVideo &&
            renderable.Plane == CinematicLayerPlane.Background);
        Assert.Contains(renderables, renderable =>
            renderable.Actor.Key == dynamicVideo.Key &&
            renderable.RenderKind == CinematicRenderKind.PleoVideo &&
            renderable.Plane == CinematicLayerPlane.Background);
    }

    [Fact]
    public void ActorBuilder_UsesDynamicPleoTextureActorsWhenNoRootVideoOutputExists()
    {
        CinematicActor dynamicVideo = CreateActor(
            ["Map_VIDEO", "video_texture_component"],
            texturePath: "pleotexturedyn/mainchannel",
            z: -4.5f);

        IReadOnlyList<RenderableCinematicActor> renderables = CinematicActorBuilder.BuildRenderableActors(
            new CinematicScene([dynamicVideo]),
            new Dictionary<string, MaterializedCinematicImage>(StringComparer.OrdinalIgnoreCase),
            includeVideoOutput: true);

        RenderableCinematicActor renderable = Assert.Single(renderables);
        Assert.Equal(dynamicVideo.Key, renderable.Actor.Key);
        Assert.Equal(CinematicRenderKind.PleoVideo, renderable.RenderKind);
        Assert.Equal(CinematicLayerPlane.Background, renderable.Plane);
    }

    [Fact]
    public void ActorBuilder_MapsVideoTextureComponentMeshMaterialToPleoVideo()
    {
        CinematicActor actor = CreateActor(
            ["_mashup_graph", "txcomponent_8x_left"],
            texturePath: "world/jd2015/_mashup/graph/textures/texture_transparent.png",
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentBinary>()) with
        {
            MaterialPath = "world/jd2015/_mashup/graph/materials/video_texture_component_test.msh",
            MeshPath = "world/jd2015/_mashup/graph/mesh/3d_8x.m3d"
        };
        RenderGeometry meshGeometry = CreateTestMeshGeometry();
        using MaterializedCinematicImage image = new(
            actor.Key,
            actor.TexturePath!,
            new Image<Bgra32>(16, 16),
            meshGeometry,
            CinematicRenderMaterial.Empty);
        Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
        {
            [actor.Key] = image
        };

        Assert.Empty(CinematicActorBuilder.BuildRenderableActors(new CinematicScene([actor]), images));

        RenderableCinematicActor renderable = Assert.Single(
            CinematicActorBuilder.BuildRenderableActors(
                new CinematicScene([actor]),
                images,
                includeVideoOutput: true));

        Assert.Equal(CinematicRenderKind.PleoVideo, renderable.RenderKind);
        Assert.Equal(CinematicGeometrySource.Mesh3D, renderable.Geometry.Source);
        Assert.Same(image, renderable.Image);
    }

    [Fact]
    public void ActorBuilder_PreservesDynamicPleoTextureMeshGeometry()
    {
        CinematicActor actor = CreateActor(
            ["Map_GRAPH", "TXComponent_8x_Left"],
            texturePath: "pleotexturedyn/mainchannel",
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentBinary>());
        RenderGeometry meshGeometry = CreateTestMeshGeometry();
        using MaterializedCinematicImage image = new(
            actor.Key,
            actor.TexturePath!,
            new Image<Bgra32>(1, 1),
            meshGeometry,
            CinematicRenderMaterial.Empty);
        Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase)
        {
            [actor.Key] = image
        };

        RenderableCinematicActor renderable = Assert.Single(
            CinematicActorBuilder.BuildRenderableActors(
                new CinematicScene([actor]),
                images,
                includeVideoOutput: true));

        Assert.Equal(CinematicRenderKind.PleoVideo, renderable.RenderKind);
        Assert.Equal(CinematicGeometrySource.Mesh3D, renderable.Geometry.Source);
        Assert.Same(meshGeometry, renderable.Geometry);
    }

    [Fact]
    public void PleoRootVideoOutput_PreservesAuthoredActorPosition()
    {
        CinematicActor rootVideo = CreateActor(
            ["Map_VIDEO", "VideoOutput"],
            x: -0.78f,
            y: 0.25f,
            typeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>());
        RenderableCinematicActor renderable = new(
            rootVideo,
            null,
            CinematicGeometryProjector.CreatePleoVideoGeometry(),
            CinematicLayerPlane.Background,
            rootVideo.ScenePriority,
            rootVideo.RelativeZ,
            rootVideo.SourceOffset,
            CinematicRenderKind.PleoVideo);
        ResolvedActorState state = new(
            -0.78f,
            0.25f,
            0,
            3.965f,
            2.232f,
            0,
            1,
            RgbTint.White,
            false);

        ProjectedQuad quad = CinematicGeometryProjector.ProjectQuad(
            renderable.Geometry,
            state,
            1920,
            1080,
            rootVideo.CustomAnchorX,
            rootVideo.CustomAnchorY,
            rootVideo.Anchor,
            CinematicCamera.CreateDefault(1920, 1080));

        Assert.True(quad.Bounds.Left < 0);
        AssertClose(-196, quad.Bounds.Left);
        AssertClose(1932, quad.Bounds.Width);
    }

    [Fact]
    public void FrameLoop_UsesHighestDepthClearColorComponentAsOpaqueCanvasClear()
    {
        uint clearColorComponentTypeId = LegacyBinarySerializer.GetTypeId<CinematicClearColorComponentBinary>();
        CinematicActor backClear = CreateActor(
            ["Map_GRAPH", "BackClear"],
            z: -50,
            visualComponentTypeId: clearColorComponentTypeId,
            baseTint: new RgbTint(1.0, 0.25, 0.125),
            baseAlpha: 0.0f);
        CinematicActor frontClear = CreateActor(
            ["Map_GRAPH", "FrontClear"],
            z: -10,
            visualComponentTypeId: clearColorComponentTypeId,
            baseTint: new RgbTint(0.5, 0.75, 1.0),
            baseAlpha: 0.0f);
        CinematicScene scene = new([backClear, frontClear]);
        IReadOnlyDictionary<string, ResolvedActorState> states = CinematicActorStateResolver.ResolveActorStates(
            scene.Actors,
            new CinematicRenderRuntime(scene),
            PropertyClipIndex.Empty,
            frame: 0);

        Bgra32 clear = CinematicFrameLoop.ResolveFrameClearColor(scene.Actors, states);

        Assert.Equal(128, clear.R);
        Assert.Equal(191, clear.G);
        Assert.Equal(255, clear.B);
        Assert.Equal(255, clear.A);
    }

    [Fact]
    public void StringId_UsesSourceDobbsUppercaseHash()
    {
        uint materialGraphicComponentId = CinematicStringId.Compute("MaterialGraphicComponent");

        Assert.Equal(LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicComponentBinary>(), materialGraphicComponentId);
    }

    [Fact]
    public void AtlasPath_UsesSourceDefaultTextureAtlasName()
    {
        string atlasPath = CinematicAtlasContainer.GetAtlasPathForTexture(
            "world\\jd5\\PrinceAli\\graph\\textures\\m_floor_02.png");

        Assert.Equal("world/jd5/princeali/graph/textures/m_floor_02.atl", atlasPath);
    }

    private static JustDanceUbiArtFileSystem CreateJd2016TestFileSystem()
    {
        UbiArtConversionRequest request = new(Path.GetTempPath(), Path.GetTempPath(), "song")
        {
            Type = CookedType.Cooked,
            ImportPlatform = UbiArtPlatform.Cafe,
            ImportEngineVersion = UbiArtEngineVersion.JD2016
        };
        UbiArtVersionProfile profile = new(
            UbiArtPlatform.Cafe,
            UbiArtEngineVersion.JD2016,
            new JD2015LayoutResolver(),
            new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2016));
        return new JustDanceUbiArtFileSystem(
            request,
            profile,
            NullLogger<JustDanceUbiArtFileSystem>.Instance);
    }
}
