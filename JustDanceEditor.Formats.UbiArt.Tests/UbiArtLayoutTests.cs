using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System.IO;
using System.Threading.Tasks;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class UbiArtLayoutTests
{
    [Fact]
    public void Layout_Should_Handle_Uncooked_JD2014_Symmetry()
    {
        JD2014LayoutResolver layout = new();
        UbiArtPlatform style = UbiArtPlatform.Uncooked;
        UbiArtEngineVersion version = UbiArtEngineVersion.JD2014;

        string path = layout.GetMapWorldFolder("/root", "SongName", style, version);
        // Should be world/maps/jd5/SongName
        Assert.Contains("jd5", path);
    }

    [Fact]
    public async Task ExportSymmetry_Uncooked_Map()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        UbiArtLayoutResolver layout = new();
        UbiArtPlatform style = UbiArtPlatform.Uncooked;
        UbiArtEngineVersion version = UbiArtEngineVersion.JD2022;
        string mapWorldRelative = layout.GetMapWorldFolder(root, "song", style, version);
        string mapWorldFolder = Path.Combine(root, mapWorldRelative);

        // create expected folders
        Directory.CreateDirectory(mapWorldFolder);
        Directory.CreateDirectory(Path.Combine(mapWorldFolder, "audio"));
        Directory.CreateDirectory(Path.Combine(mapWorldFolder, "timeline"));
        Directory.CreateDirectory(Path.Combine(mapWorldFolder, "cinematics"));

        IntermediateSongPackage package = new() { Metadata = new JDI.Metadata.IntermediateMetadata { MapName = "song" } };

        UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);
        await writer.ExportAsync(package, null, root, style, version, layout);

        // Check songdesc is written under the map world folder
        Assert.True(File.Exists(Path.Combine(mapWorldFolder, "songdesc.tpl")));
        // Check timeline file written
        string timelineFolder = Path.Combine(root, layout.GetTimelineFolder(root, "song", style, version));
        Assert.True(File.Exists(Path.Combine(timelineFolder, "song_tml_dance.dtape")));

        Directory.Delete(root, true);
    }

    [Fact]
    public async Task LegacyWiiExport_SkipsAutodancePreviewAndMpdFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), null, root, UbiArtPlatform.Revolution, UbiArtEngineVersion.JD2020);

            string mapRoot = Path.Combine(root, "cache", "itf_cooked", "wii", "world", "maps", "song");
            Assert.False(File.Exists(Path.Combine(mapRoot, "autodance", "song_autodance.tpl.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "autodance", "song_autodance.isc.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "autodance", "song_autodance.act.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "song_video_map_preview.isc.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "video_player_map_preview.act.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "song.mpd.ckd")));
            Assert.True(File.Exists(Path.Combine(mapRoot, "videoscoach", "song_video.isc.ckd")));
            Assert.True(File.Exists(Path.Combine(mapRoot, "videoscoach", "video_player_main.act.ckd")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task LegacyX360Export_KeepsAutodanceAndPreviewSceneButSkipsPreviewActorAndMpdFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), null, root, UbiArtPlatform.Xenon, UbiArtEngineVersion.JD2019);

            string mapRoot = Path.Combine(root, "cache", "itf_cooked", "x360", "world", "maps", "song");
            Assert.True(File.Exists(Path.Combine(mapRoot, "autodance", "song_autodance.tpl.ckd")));
            Assert.True(File.Exists(Path.Combine(mapRoot, "autodance", "song_autodance.isc.ckd")));
            Assert.True(File.Exists(Path.Combine(mapRoot, "autodance", "song_autodance.act.ckd")));
            Assert.True(File.Exists(Path.Combine(mapRoot, "videoscoach", "song_video_map_preview.isc.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "video_player_map_preview.act.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "song.mpd.ckd")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(UbiArtPlatform.Cafe, "wiiu")]
    [InlineData(UbiArtPlatform.Xenon, "x360")]
    public async Task LegacyJD2015CookedExport_WritesDirectBinaryResourcesAndVersionedPaths(UbiArtPlatform platform, string platformFolder)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), null, root, platform, UbiArtEngineVersion.JD2015);

            string cookedRoot = Path.Combine(root, "cache", "itf_cooked", platformFolder);
            string mapRoot = Path.Combine(cookedRoot, "world", "jd2015", "song");
            string songDescPath = Path.Combine(mapRoot, "songdesc.tpl.ckd");
            string musicTrackPath = Path.Combine(mapRoot, "audio", "song_musictrack.tpl.ckd");
            string songDescActorPath = Path.Combine(mapRoot, "songdesc.act.ckd");

            Assert.True(File.Exists(songDescPath));
            Assert.True(File.Exists(musicTrackPath));
            Assert.True(File.Exists(songDescActorPath));
            Assert.False(Directory.Exists(Path.Combine(cookedRoot, "cache", "legacyconverteddata", "song")));
            Assert.False(Directory.Exists(Path.Combine(cookedRoot, "world", "maps", "song")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "song_video_map_preview.isc.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "video_player_main.act.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "videoscoach", "song.mpd.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "audio", "song_sequence.tpl.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "audio", "song.stape.ckd")));

            byte[] songDescBytes = await File.ReadAllBytesAsync(songDescPath, TestContext.Current.CancellationToken);
            Assert.True(songDescBytes.Length > 4);
            Assert.Equal(0, songDescBytes[0]);
            Assert.Equal(0, songDescBytes[1]);
            Assert.Equal(0, songDescBytes[2]);
            Assert.Equal(1, songDescBytes[3]);

            string songDescActorText = ReadAscii(songDescActorPath);
            Assert.Contains("songdesc.tpl", songDescActorText);
            Assert.Contains("world/jd2015/song/", songDescActorText);
            Assert.DoesNotContain("legacyconverteddata", songDescActorText);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(UbiArtPlatform.Revolution, "wii")]
    [InlineData(UbiArtPlatform.Cell, "ps3")]
    public async Task LegacyJD2014CookedExport_WritesPackedTimelineAndSkipsTapeCases(UbiArtPlatform platform, string platformFolder)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);
            IntermediateSongPackage package = CreatePackage();
            package.TimelineStructure.StartBeat = -1;
            package.TimelineStructure.Markers = [0, 24000];

            await writer.ExportAsync(package, null, root, platform, UbiArtEngineVersion.JD2014);

            string mapRoot = Path.Combine(root, "cache", "itf_cooked", platformFolder, "world", "jd5", "song");
            string timelineRoot = Path.Combine(mapRoot, "timeline");

            Assert.True(File.Exists(Path.Combine(mapRoot, "songdesc.tpl.ckd")));
            Assert.False(File.Exists(Path.Combine(mapRoot, "songdesc.act.ckd")));
            Assert.True(File.Exists(Path.Combine(timelineRoot, "timeline.tpl.ckd")));
            Assert.True(File.Exists(Path.Combine(timelineRoot, "timeline.act.ckd")));
            Assert.True(File.Exists(Path.Combine(timelineRoot, "song_tml.isc.ckd")));
            Assert.False(File.Exists(Path.Combine(timelineRoot, "song_tml_dance.dtape.ckd")));
            Assert.False(File.Exists(Path.Combine(timelineRoot, "song_tml_karaoke.ktape.ckd")));
            Assert.False(File.Exists(Path.Combine(timelineRoot, "song_tml_dance.tpl.ckd")));
            Assert.False(File.Exists(Path.Combine(timelineRoot, "song_tml_karaoke.tpl.ckd")));

            string audioRoot = Path.Combine(mapRoot, "audio");
            Assert.True(File.Exists(Path.Combine(audioRoot, "amb", "set_amb_song_intro.tpl.ckd")));
            Assert.False(File.Exists(Path.Combine(audioRoot, "song_sequence.tpl.ckd")));
            Assert.False(File.Exists(Path.Combine(audioRoot, "song.stape.ckd")));
            Assert.False(File.Exists(Path.Combine(audioRoot, "amb", "amb_song_intro.tpl.ckd")));

            string menuArtRoot = Path.Combine(mapRoot, "menuart");
            Assert.False(File.Exists(Path.Combine(menuArtRoot, "textures", "song_cover_generic.tga.ckd")));
            Assert.False(File.Exists(Path.Combine(menuArtRoot, "textures", "song_map_bkg.tga.ckd")));

            string timelineText = ReadAscii(Path.Combine(timelineRoot, "song_tml.isc.ckd"));
            Assert.Contains("timeline: song", timelineText);
            Assert.Contains("timeline.tpl", timelineText);
            Assert.DoesNotContain("song_tml_dance", timelineText);
            Assert.DoesNotContain("song_tml_karaoke", timelineText);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(UbiArtPlatform.Revolution, "wii")]
    [InlineData(UbiArtPlatform.Cell, "ps3")]
    public async Task LegacyJD2015CookedExport_CopiesRawMovesToTargetFolder(UbiArtPlatform platform, string movesFolder)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string materializedRoot = Path.Combine(root, "jdi");
        string outputRoot = Path.Combine(root, "out");

        try
        {
            string movesRoot = Path.Combine(materializedRoot, "assets", "moves");
            Directory.CreateDirectory(movesRoot);
            await File.WriteAllBytesAsync(Path.Combine(movesRoot, "Move_A.msm"), [1, 2, 3, 4], TestContext.Current.CancellationToken);

            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), materializedRoot, outputRoot, platform, UbiArtEngineVersion.JD2015);

            string rawMovesRoot = Path.Combine(outputRoot, "world", "jd2015", "song", "timeline", "moves");
            Assert.True(File.Exists(Path.Combine(rawMovesRoot, movesFolder, "move_a.msm")));
            Assert.False(File.Exists(Path.Combine(rawMovesRoot, "wiiu", "move_a.msm")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ModernOrbisCookedExport_CopiesHandMovesToWiiUAndGesturesToOrbis()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string materializedRoot = Path.Combine(root, "jdi");
        string outputRoot = Path.Combine(root, "out");

        try
        {
            string movesRoot = Path.Combine(materializedRoot, "assets", "moves");
            string gesturesRoot = IntermediatePackageLayout.Resolve(materializedRoot, UbiArtGestureFolders.PackageFolder(UbiArtGestureFolders.Orbis));
            Directory.CreateDirectory(movesRoot);
            Directory.CreateDirectory(gesturesRoot);
            await File.WriteAllBytesAsync(Path.Combine(movesRoot, "Move_A.msm"), [1, 2, 3, 4], TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(gesturesRoot, "Move_A.gesture"), [5, 6, 7, 8], TestContext.Current.CancellationToken);

            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), materializedRoot, outputRoot, UbiArtPlatform.Orbis, UbiArtEngineVersion.JD2022);

            string rawMovesRoot = Path.Combine(outputRoot, "world", "maps", "song", "timeline", "moves");
            Assert.True(File.Exists(Path.Combine(rawMovesRoot, "wiiu", "move_a.msm")));
            Assert.True(File.Exists(Path.Combine(rawMovesRoot, "orbis", "move_a.gesture")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(UbiArtPlatform.Revolution, true)]
    [InlineData(UbiArtPlatform.Xenon, false)]
    public async Task LegacyMenuArtBackgroundExport_FollowsTargetPlatform(UbiArtPlatform platform, bool expectMapBackground)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string materializedRoot = Path.Combine(root, "jdi");
        string outputRoot = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(materializedRoot);
            UbiArtEngineVersion version = platform == UbiArtPlatform.Revolution ? UbiArtEngineVersion.JD2020 : UbiArtEngineVersion.JD2019;
            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), materializedRoot, outputRoot, platform, version);

            string platformFolder = platform == UbiArtPlatform.Revolution ? "wii" : "x360";
            string menuArtTextures = Path.Combine(outputRoot, "cache", "itf_cooked", platformFolder, "world", "maps", "song", "menuart", "textures");
            Assert.Equal(expectMapBackground, File.Exists(Path.Combine(menuArtTextures, "song_map_bkg.tga.ckd")));
            Assert.False(File.Exists(Path.Combine(menuArtTextures, "song_banner_bkg.tga.ckd")));
            Assert.False(File.Exists(Path.Combine(menuArtTextures, "song_cover_online.tga.ckd")));
            Assert.False(File.Exists(Path.Combine(menuArtTextures, "song_cover_online_kids.tga.ckd")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static IntermediateSongPackage CreatePackage() => new()
    {
        Metadata = new JDI.Metadata.IntermediateMetadata
        {
            MapName = "song",
            ParentMapName = "song",
            Title = "Song",
            Artist = "Artist",
            CoachCount = 1
        }
    };

    private static string ReadAscii(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] is >= 32 and <= 126 ? (char)bytes[i] : '.';

        return new string(chars);
    }
}
