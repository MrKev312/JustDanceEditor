using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class SongDataLoaderTests
{
    [Theory]
    [InlineData("AlphaClip")]
    [InlineData("MaterialGraphicUVScrollClip")]
    [InlineData("TranslationClip")]
    public void IsRenderOnlyCinematicClipClass_RecognizesKnownRenderClips(string className)
    {
        Assert.True(SongDataLoader.IsRenderOnlyCinematicClipClass(className));
    }

    [Theory]
    [InlineData("MotionClip")]
    [InlineData("UnknownFutureClip")]
    [InlineData("")]
    public void IsRenderOnlyCinematicClipClass_LeavesGameplayAndUnknownClipsVisible(string className)
    {
        Assert.False(SongDataLoader.IsRenderOnlyCinematicClipClass(className));
    }

    [Fact]
    public void TryRemapMashupCoachClip_CanCollapseMotionClipsToSingleCoachTimeline()
    {
        int framesPerBeat = (int)CinematicConstants.TapeFramesPerBeat;
        LegacyMashupBlock block = new()
        {
            AbsoluteStartBeat = 32,
            SourceBlock = new LegacyMashupBlockDescriptor
            {
                SongName = "Source",
                FirstBeat = 8,
                LastBeat = 16
            }
        };
        MotionClip source = new()
        {
            Id = 10,
            CoachId = 22,
            ClassifierPath = "world/maps/source/timeline/move.msm",
            StartTime = 10 * framesPerBeat,
            Duration = 4 * framesPerBeat,
            Color = [1.0f, 0.2f, 0.3f, 0.4f]
        };

        Assert.True(SongDataLoader.TryRemapMashupCoachClip(source, block, 99, forceSingleCoachTimeline: true, out Clip? collapsedClip));
        MotionClip collapsedMotion = Assert.IsType<MotionClip>(collapsedClip);
        Assert.Equal(0, collapsedMotion.CoachId);
        Assert.Equal(99, collapsedMotion.Id);
        Assert.Equal(34 * framesPerBeat, collapsedMotion.StartTime);
        Assert.Equal(4 * framesPerBeat, collapsedMotion.Duration);

        Assert.True(SongDataLoader.TryRemapMashupCoachClip(source, block, 100, forceSingleCoachTimeline: false, out Clip? preservedClip));
        MotionClip preservedMotion = Assert.IsType<MotionClip>(preservedClip);
        Assert.Equal(22, preservedMotion.CoachId);
    }

    [Fact]
    public void LoadSongData_Preserves_OriginalJDVersion()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string mapsFolder = Path.Combine(root, "world", "maps", "song");
        Directory.CreateDirectory(mapsFolder);

        // Also create the flat uncooked layout location (root/song/songdesc.tpl) so FileSystem direct checks succeed
        string flatSongFolder = Path.Combine(root, "song");
        Directory.CreateDirectory(flatSongFolder);

        // Create a songdesc with OriginalJDVersion = 123
        File.WriteAllText(Path.Combine(mapsFolder, "songdesc.tpl"), "{ \"COMPONENTS\": [ { \"OriginalJDVersion\": 123, \"JDVersion\": 4884, \"MapName\": \"song\" } ] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "songdesc.tpl"), "{ \"COMPONENTS\": [ { \"OriginalJDVersion\": 123, \"JDVersion\": 4884, \"MapName\": \"song\" } ] }");

        // Create audio, cinematics and timeline files that SongDataLoader expects
        Directory.CreateDirectory(Path.Combine(mapsFolder, "audio"));
        File.WriteAllText(Path.Combine(mapsFolder, "audio", "song_musictrack.tpl"), "{}");
        Directory.CreateDirectory(Path.Combine(flatSongFolder, "audio"));
        File.WriteAllText(Path.Combine(flatSongFolder, "audio", "song_musictrack.tpl"), "{}");

        Directory.CreateDirectory(Path.Combine(mapsFolder, "cinematics"));
        File.WriteAllText(Path.Combine(mapsFolder, "cinematics", "song_mainsequence.tape"), "{ \"Clips\": [] }");
        Directory.CreateDirectory(Path.Combine(mapsFolder, "timeline"));
        File.WriteAllText(Path.Combine(mapsFolder, "timeline", "song_tml_dance.dtape"), "{ \"Clips\": [] }");
        File.WriteAllText(Path.Combine(mapsFolder, "timeline", "song_tml.isc"), "<Timeline><Actor USERFRIENDLY=\"song_tml_karaoke\" LUA=\"karaoke_actor.tpl\" /></Timeline>");

        Directory.CreateDirectory(Path.Combine(flatSongFolder, "cinematics"));
        File.WriteAllText(Path.Combine(flatSongFolder, "cinematics", "song_mainsequence.tape"), "{ \"Clips\": [] }");
        Directory.CreateDirectory(Path.Combine(flatSongFolder, "timeline"));
        File.WriteAllText(Path.Combine(flatSongFolder, "timeline", "song_tml_dance.dtape"), "{ \"Clips\": [] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "timeline", "song_tml.isc"), "<Timeline><Actor USERFRIENDLY=\"song_tml_karaoke\" LUA=\"karaoke_actor.tpl\" /></Timeline>");

        // Create karaoke actor and tape referenced by it
        File.WriteAllText(Path.Combine(mapsFolder, "karaoke_actor.tpl"), "{ \"COMPONENTS\": [ { \"TapesRack\": [ { \"Entries\": [ { \"Path\": \"karaoke.tape\" } ] } ] } ] }");
        File.WriteAllText(Path.Combine(mapsFolder, "timeline", "karaoke.tape"), "{ \"Clips\": [] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "karaoke_actor.tpl"), "{ \"COMPONENTS\": [ { \"TapesRack\": [ { \"Entries\": [ { \"Path\": \"karaoke.tape\" } ] } ] } ] }");
        File.WriteAllText(Path.Combine(flatSongFolder, "timeline", "karaoke.tape"), "{ \"Clips\": [] }");

        UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
        UbiArtConversionRequest req = new(root, Path.GetTempPath(), "song")
        {
            Type = CookedType.Uncooked
        };
        JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
        fs.Initialize();

        SongDataLoader loader = new(NullLogger<SongDataLoader>.Instance);
        JDUbiArtSong song = loader.LoadSongData(req, fs);

        Assert.Equal(123u, song.JDVersion);

        Directory.Delete(root, true);
    }
}