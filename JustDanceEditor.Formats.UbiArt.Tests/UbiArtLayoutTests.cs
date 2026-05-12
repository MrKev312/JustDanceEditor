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
        UbiArtLayoutResolver layout = new();
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

            await writer.ExportAsync(CreatePackage(), null, root, UbiArtPlatform.Wii, UbiArtEngineVersion.JD2020);

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

            await writer.ExportAsync(CreatePackage(), null, root, UbiArtPlatform.X360, UbiArtEngineVersion.JD2019);

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
    [InlineData(UbiArtPlatform.Wii, true)]
    [InlineData(UbiArtPlatform.X360, false)]
    public async Task LegacyMenuArtBackgroundExport_FollowsTargetPlatform(UbiArtPlatform platform, bool expectMapBackground)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string materializedRoot = Path.Combine(root, "jdi");
        string outputRoot = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(materializedRoot);
            UbiArtEngineVersion version = platform == UbiArtPlatform.Wii ? UbiArtEngineVersion.JD2020 : UbiArtEngineVersion.JD2019;
            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);

            await writer.ExportAsync(CreatePackage(), materializedRoot, outputRoot, platform, version);

            string platformFolder = platform == UbiArtPlatform.Wii ? "wii" : "x360";
            string menuArtTextures = Path.Combine(outputRoot, "cache", "itf_cooked", platformFolder, "world", "maps", "song", "menuart", "textures");
            Assert.Equal(expectMapBackground, File.Exists(Path.Combine(menuArtTextures, "song_map_bkg.tga.ckd")));
            Assert.False(File.Exists(Path.Combine(menuArtTextures, "song_banner_bkg.tga.ckd")));
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
}
