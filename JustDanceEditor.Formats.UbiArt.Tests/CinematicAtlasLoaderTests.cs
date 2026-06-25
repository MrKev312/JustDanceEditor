using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.Cinematics.Timeline;

using System.Numerics;
using System.Text;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderBinaryTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CinematicAtlasLoaderTests
{
    [Fact]
    public void AtlasLoader_ReadsAtlasContainerWithoutUvParametersMap()
    {
        string firstTexturePath = "world/jd5/cantholddlc/graph/textures/fish.png";
        string secondTexturePath = "world/jd5/cantholddlc/graph/textures/fish_eye.png";
        byte[] bytes = CreateAtlasContainerWithoutUvParametersBytes(firstTexturePath, secondTexturePath);

        CinematicAtlasContainer container = CinematicAtlasLoader.Read(bytes);

        Assert.True(container.TryGetAtlasForTexture(firstTexturePath, out CinematicAtlas? first, out _));
        Assert.NotNull(first);
        AssertClose(64, first.Width);
        AssertClose(64, first.Height);
        Assert.True(first.UvMap.TryGetValue(0, out CinematicUvData? firstUvData));
        Assert.Equal(4, firstUvData.Uvs.Count);

        Assert.True(container.TryGetAtlasForTexture(secondTexturePath, out CinematicAtlas? second, out _));
        Assert.NotNull(second);
        AssertClose(128, second.Width);
        AssertClose(32, second.Height);
        Assert.True(second.UvMap.TryGetValue(0, out CinematicUvData? secondUvData));
        Assert.Equal(2, secondUvData.Uvs.Count);
    }


    [Fact]
    public void AtlasLoader_ReadsLooseTextAtlas()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            """
            ITF_GFX_UV_ATLAS =
            {
                TextureHeight=512,
                TextureAspectRatio=2,
                {
                    index=3,
                    uvNumber=4,
                    uv0=vector2dNew(0.25,0.125),
                    uv1=vector2dNew(0.75,0.125),
                    uv2=vector2dNew(0.75,0.375),
                    uv3=vector2dNew(0.25,0.375),
                },
            }
            """);

        bool read = CinematicAtlasLoader.TryReadLooseAtlas(bytes, out CinematicAtlas? atlas);

        Assert.True(read);
        Assert.NotNull(atlas);
        AssertClose(1024, atlas.Width);
        AssertClose(512, atlas.Height);
        Assert.True(atlas.UvMap.TryGetValue(3, out CinematicUvData? uvData));
        Assert.Equal(4, uvData.Uvs.Count);
        Assert.Equal(new Vector2(0.25f, 0.125f), uvData.Uvs[0]);
        Assert.Equal(new Vector2(0.75f, 0.375f), uvData.Uvs[2]);
    }
}