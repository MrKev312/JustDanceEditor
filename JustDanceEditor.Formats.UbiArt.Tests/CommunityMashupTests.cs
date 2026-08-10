using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;

using Xunit;

using static JustDanceEditor.Formats.UbiArt.Tests.CinematicRenderTimelineTestSupport;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class CommunityMashupTests
{
    [Fact]
    public void IsCommunityMashup_UsesJd2018BackgroundType()
    {
        JDUbiArtSong song = CreateSong("OrdinaryMap");
        song.SongDesc.Components[0].BackgroundType = 5;

        Assert.True(song.IsCommunityMashup);
    }

    [Fact]
    public void IsCommunityMashup_UsesOldSystemTagFallback()
    {
        JDUbiArtSong song = CreateSong("OrdinaryMap");
        song.SongDesc.Components[0].Tags = ["CommunityMashup"];

        Assert.True(song.IsCommunityMashup);
    }

    [Theory]
    [InlineData("HappyCMU", false)]
    [InlineData("HappySR", true)]
    public void IsCommunityMashup_RecognizesAuthoringMapNames(string mapName, bool isStarRemix)
    {
        JDUbiArtSong song = CreateSong(mapName);

        Assert.True(song.IsCommunityMashup);
        Assert.Equal(isStarRemix, song.IsStarRemix);
    }

    [Fact]
    public void IsCommunityMashup_DoesNotMatchOrdinaryMap()
    {
        JDUbiArtSong song = CreateSong("Happy");

        Assert.False(song.IsCommunityMashup);
        Assert.False(song.IsStarRemix);
    }

    [Fact]
    public void Jd2015AvatarSpawnKeepsAnchorLayoutAndUsesAuthoredAnimLightVisual()
    {
        CinematicActorBind bind = new(
            "root_dummy",
            OffsetX: 12,
            OffsetY: -4,
            OffsetZ: 3,
            OffsetAngle: 0.25f,
            LocalScaleX: 0.8f,
            LocalScaleY: 0.7f,
            UseParentFlip: 1,
            ScaleInheritProp: 0,
            UseParentAlpha: 1,
            UseParentColor: 1);
        CinematicActor anchor = CreateActor(
            ["grp_profile_info", "avatar"],
            x: 10,
            y: 20,
            z: 30,
            scaleX: 0.5f,
            scaleY: 0.6f,
            angle: 0.2f,
            scenePriority: 7,
            defaultEnabled: false,
            bind: bind,
            baseTint: new RgbTint(0.1f, 0.2f, 0.3f),
            baseAlpha: 0.4f);
        CinematicActor avatar = CreateAnimLightActor() with
        {
            TemplatePath = "world/jd2015/_ui/avatars/ui_avatar_0001/ui_avatar_0001.tpl"
        };

        CinematicActor placed = Jd2015CommunityMashupRenderPlanner.PlaceSpawnedAvatarAtAnchor(anchor, avatar);

        Assert.Equal(anchor.Path, placed.Path);
        Assert.Equal(anchor.Name, placed.Name);
        Assert.Equal(anchor.PositionX, placed.PositionX);
        Assert.Equal(anchor.PositionY, placed.PositionY);
        Assert.Equal(anchor.RelativeZ, placed.RelativeZ);
        Assert.Equal(anchor.ScaleX, placed.ScaleX);
        Assert.Equal(anchor.ScaleY, placed.ScaleY);
        Assert.Equal(anchor.Angle, placed.Angle);
        Assert.Equal(anchor.ParentBind, placed.ParentBind);
        Assert.Equal(anchor.DefaultEnabled, placed.DefaultEnabled);
        Assert.Equal(anchor.BaseTint, placed.BaseTint);
        Assert.Equal(anchor.BaseAlpha, placed.BaseAlpha);
        Assert.Equal(avatar.TemplatePath, placed.TemplatePath);
        Assert.Equal(avatar.TexturePath, placed.TexturePath);
        Assert.Equal(avatar.VisualComponentTypeId, placed.VisualComponentTypeId);
        Assert.Same(avatar.AnimLightTemplate, placed.AnimLightTemplate);
    }

    private static JDUbiArtSong CreateSong(string mapName) => new()
    {
        Name = mapName,
        SongDesc = new SongDesc
        {
            Components = [new InfoComponent { MapName = mapName }]
        }
    };
}
