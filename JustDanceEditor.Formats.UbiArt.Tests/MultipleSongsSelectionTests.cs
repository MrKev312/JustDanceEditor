using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;
using KevInc.UbiArt.Ipk;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class MultipleSongsSelectionTests
{
    [Theory]
    [InlineData(UbiArtPlatform.Revolution, "WII")]
    [InlineData(UbiArtPlatform.Cafe, "WIIU")]
    [InlineData(UbiArtPlatform.Cell, "PS3")]
    [InlineData(UbiArtPlatform.Xenon, "X360")]
    public void GetFilePath_LegacySongInput_ResolvesSiblingBlockFlowsPackage(UbiArtPlatform platform, string platformName)
    {
        string root = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string inputBundle = Path.Combine(root, $"Song_{platformName}");
            string blockFlows = Path.Combine(root, $"BlockFlows_{platformName}");
            string sourceTimeline = Path.Combine(
                blockFlows,
                "cache",
                "itf_cooked",
                platform.GetCookedFolderName(),
                "world",
                "jdblocks",
                "source_1",
                "timeline");
            Directory.CreateDirectory(inputBundle);
            Directory.CreateDirectory(sourceTimeline);
            File.WriteAllText(Path.Combine(sourceTimeline, "source_1_tml_dance.dtape.ckd"), "source timeline");
            string sourceVideoFolder = Path.Combine(Path.GetDirectoryName(sourceTimeline)!, "videoscoach");
            Directory.CreateDirectory(sourceVideoFolder);
            File.WriteAllText(Path.Combine(sourceVideoFolder, $"source_1.{platform.GetCookedFolderName()}.webm"), "source video");

            UbiArtConversionRequest request = new(inputBundle, Path.Combine(root, "out"), null)
            {
                Type = CookedType.Cooked
            };
            UbiArtVersionProfile profile = new(
                platform,
                UbiArtEngineVersion.JD2015,
                new JD2015LayoutResolver(),
                new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2015));
            using JustDanceUbiArtFileSystem fileSystem = new(request, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fileSystem.Initialize();

            bool found = fileSystem.GetFilePath(
                Path.Combine("world", "jdblocks", "source_1", "timeline", "source_1_tml_dance.dtape"),
                out CookedFile? sourceFile);
            LegacyMashupData mashup = new() { MapName = "testMU", BaseSongName = "test" };
            LegacyMashupBlock block = new()
            {
                UsesAlternativeBlock = true,
                SourceBlock = new LegacyMashupBlockDescriptor { SongName = "source_1", FirstBeat = 0, LastBeat = 16 }
            };
            bool foundVideo = MashupSourceVideoResolver.TryFindSourceVideo(fileSystem, mashup, block, out MashupSourceVideo sourceVideo);

            Assert.True(found);
            Assert.NotNull(sourceFile);
            Assert.True(foundVideo);
            Assert.EndsWith($"source_1.{platform.GetCookedFolderName()}.webm", sourceVideo.File.RelativePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }

    [Fact]
    public void GetAvailableSongs_DoesNotTreatEmptyLegacyMashupMarkerAsMashup()
    {
        string root = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string inputBundle = Path.Combine(root, "Bundle_4_WII");
            string songFolder = Path.Combine(
                inputBundle,
                "cache",
                "itf_cooked",
                "wii",
                "world",
                "jd2015",
                "happyalt");
            string timelineFolder = Path.Combine(songFolder, "timeline");
            Directory.CreateDirectory(timelineFolder);
            File.WriteAllText(Path.Combine(songFolder, "songdesc.tpl.ckd"), "song desc");
            File.WriteAllBytes(
                Path.Combine(timelineFolder, "happyaltmu.tpl.ckd"),
                Convert.FromHexString(
                    "00000001000000981B857BCE0000006C00000000000000000000000000000000000000000000000000000000000000015B648E4400000028000000010000000000000000"));

            UbiArtConversionRequest request = new(inputBundle, Path.Combine(root, "out"), null)
            {
                Type = CookedType.Cooked
            };
            UbiArtVersionProfile profile = new(
                UbiArtPlatform.Revolution,
                UbiArtEngineVersion.JD2015,
                new JD2015LayoutResolver(),
                new BinaryUbiArtSerializer(UbiArtEngineVersion.JD2015));
            using JustDanceUbiArtFileSystem fileSystem = new(request, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fileSystem.Initialize();

            (string SongName, string SongDescPath)[] songs = fileSystem.GetAvailableSongs();

            Assert.Equal(["happyalt"], songs.Select(song => song.SongName));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }

    [Fact]
    public void GetFilePath_NumberedLegacyBundle_ResolvesSharedSiblingBundleScene()
    {
        string root = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string inputBundle = Path.Combine(root, "Bundle_1_WIIU");
            string sharedBundle = Path.Combine(root, "Bundle_WIIU");
            string siblingSongBundle = Path.Combine(root, "Bundle_2_WIIU");
            string inputSongFolder = Path.Combine(inputBundle, "cache", "itf_cooked", "wiiu", "world", "jd5", "blameit");
            string siblingSongFolder = Path.Combine(siblingSongBundle, "cache", "itf_cooked", "wiiu", "world", "jd5", "siblingsong");
            string sharedMashupFolder = Path.Combine(sharedBundle, "cache", "itf_cooked", "wiiu", "world", "jd5", "_mashup");
            Directory.CreateDirectory(inputSongFolder);
            Directory.CreateDirectory(siblingSongFolder);
            Directory.CreateDirectory(sharedMashupFolder);
            File.WriteAllText(Path.Combine(inputSongFolder, "songdesc.tpl.ckd"), "input song");
            File.WriteAllText(Path.Combine(siblingSongFolder, "songdesc.tpl.ckd"), "sibling song");
            File.WriteAllText(Path.Combine(sharedMashupFolder, "_mashup_main_scene.isc.ckd"), "shared scene");

            UbiArtConversionRequest req = new(inputBundle, Path.Combine(root, "out"), null)
            {
                Type = CookedType.Cooked
            };
            UbiArtVersionProfile profile = new(UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2014, new JD2014LayoutResolver(), new BinaryUbiArtSerializer());
            using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fs.Initialize();

            bool foundSharedScene = fs.GetFilePath(
                Path.Combine("world", "jd5", "_mashup", "_mashup_main_scene.isc"),
                out CookedFile? sharedScene);
            (string SongName, string SongDescPath)[] songs = fs.GetAvailableSongs();

            Assert.True(foundSharedScene);
            Assert.NotNull(sharedScene);
            Assert.Equal(["blameit"], songs.Select(song => song.SongName));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }

    [Fact]
    public void GetFilePath_NumberedLegacyIpk_ResolvesSharedSiblingIpkScene()
    {
        string root = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string inputSource = Path.Combine(root, "input-source");
            string sharedSource = Path.Combine(root, "shared-source");
            string inputSongFolder = Path.Combine(inputSource, "cache", "itf_cooked", "wiiu", "world", "jd5", "blameit");
            string sharedMashupFolder = Path.Combine(sharedSource, "cache", "itf_cooked", "wiiu", "world", "jd5", "_mashup");
            Directory.CreateDirectory(inputSongFolder);
            Directory.CreateDirectory(sharedMashupFolder);
            File.WriteAllText(Path.Combine(inputSongFolder, "songdesc.tpl.ckd"), "input song");
            File.WriteAllText(Path.Combine(sharedMashupFolder, "_mashup_main_scene.isc.ckd"), "shared scene");

            string inputIpk = Path.Combine(root, "Bundle_1_WIIU.ipk");
            string sharedIpk = Path.Combine(root, "Bundle_WIIU.ipk");
            new UbiArtIpkWriter(inputSource, inputIpk).Pack();
            new UbiArtIpkWriter(sharedSource, sharedIpk).Pack();

            UbiArtConversionRequest req = new(inputIpk, Path.Combine(root, "out"), null)
            {
                Type = CookedType.Cooked
            };
            UbiArtVersionProfile profile = new(UbiArtPlatform.Cafe, UbiArtEngineVersion.JD2014, new JD2014LayoutResolver(), new BinaryUbiArtSerializer());
            using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fs.Initialize();

            bool foundSharedScene = fs.GetFilePath(
                Path.Combine("world", "jd5", "_mashup", "_mashup_main_scene.isc"),
                out CookedFile? sharedScene);

            Assert.True(foundSharedScene);
            Assert.NotNull(sharedScene);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }

    [Fact]
    public void GetAvailableSongs_ForIpkInput_DoesNotListSiblingIpkSongs()
    {
        string root = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string inputSourceA = Path.Combine(root, "input-source", "cache", "itf_cooked", "wii", "world", "maps", "asong");
            string inputSourceZ = Path.Combine(root, "input-source", "cache", "itf_cooked", "wii", "world", "maps", "zsong");
            string siblingSource = Path.Combine(root, "sibling-source", "cache", "itf_cooked", "wii", "world", "maps", "siblingsong");
            Directory.CreateDirectory(inputSourceA);
            Directory.CreateDirectory(inputSourceZ);
            Directory.CreateDirectory(siblingSource);
            File.WriteAllText(Path.Combine(inputSourceA, "songdesc.tpl.ckd"), "input a");
            File.WriteAllText(Path.Combine(inputSourceZ, "songdesc.tpl.ckd"), "input z");
            File.WriteAllText(Path.Combine(siblingSource, "songdesc.tpl.ckd"), "sibling");

            string inputIpk = Path.Combine(root, "bundle_0_wii.ipk");
            string siblingIpk = Path.Combine(root, "bundle_1_wii.ipk");
            new UbiArtIpkWriter(Path.Combine(root, "input-source"), inputIpk).Pack();
            new UbiArtIpkWriter(Path.Combine(root, "sibling-source"), siblingIpk).Pack();

            UbiArtConversionRequest req = new(inputIpk, Path.Combine(root, "out"), null)
            {
                Type = CookedType.Cooked
            };
            UbiArtVersionProfile profile = new(UbiArtPlatform.Revolution, UbiArtEngineVersion.JD2020, new UbiArtLayoutResolver(), new BinaryUbiArtSerializer());
            using JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fs.Initialize();

            (string SongName, string SongDescPath)[] songs = fs.GetAvailableSongs();

            Assert.Equal(["asong", "zsong"], songs.Select(song => song.SongName));
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch { }
        }
    }

    [Fact]
    public async Task ResolveSongAsync_UsesSelector_WhenMultipleSongsPresent()
    {
        string temp = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // Create world/maps/songA and songB
            string maps = Path.Combine(temp, "world", "maps");
            Directory.CreateDirectory(Path.Combine(maps, "songA"));
            Directory.CreateDirectory(Path.Combine(maps, "songB"));
            File.WriteAllText(Path.Combine(maps, "songA", "songdesc.tpl"), "dummy");
            File.WriteAllText(Path.Combine(maps, "songB", "songdesc.tpl"), "dummy");

            UbiArtConversionRequest req = new(temp, Path.Combine(temp, "out"), null)
            {
                SelectSongAsync = async (names) =>
                {
                    await Task.Yield();
                    return "songB";
                }
            };

            UbiArtJdiFormat format = new(
                songDataLoader: new DummySongDataLoader(),
                fileSystemFactory: (r, p) => new JustDanceUbiArtFileSystem(r, p, NullLogger<JustDanceUbiArtFileSystem>.Instance),
                engineDetector: new DummyEngineDetector(),
                audioConverter: null,
                textureService: null,
                assetWriter: null,
                logger: NullLogger<UbiArtJdiFormat>.Instance);

            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fs.Initialize();

            string chosen = await format.ResolveSongAsync(req, fs);
            Assert.Equal("songB", chosen);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch { }
        }
    }

    [Fact]
    public async Task ResolveSongAsync_ThrowsWhenSelectorCancels()
    {
        string temp = Path.Combine(Path.GetTempPath(), "jde_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string maps = Path.Combine(temp, "world", "maps");
            Directory.CreateDirectory(Path.Combine(maps, "songA"));
            Directory.CreateDirectory(Path.Combine(maps, "songB"));
            File.WriteAllText(Path.Combine(maps, "songA", "songdesc.tpl"), "dummy");
            File.WriteAllText(Path.Combine(maps, "songB", "songdesc.tpl"), "dummy");

            UbiArtConversionRequest req = new(temp, Path.Combine(temp, "out"), null)
            {
                SelectSongAsync = async (names) =>
                {
                    await Task.Yield();
                    return null; // cancel
                }
            };

            UbiArtJdiFormat format = new(
                songDataLoader: new DummySongDataLoader(),
                fileSystemFactory: (r, p) => new JustDanceUbiArtFileSystem(r, p, NullLogger<JustDanceUbiArtFileSystem>.Instance),
                engineDetector: new DummyEngineDetector(),
                audioConverter: null,
                textureService: null,
                assetWriter: null,
                logger: NullLogger<UbiArtJdiFormat>.Instance);

            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            JustDanceUbiArtFileSystem fs = new(req, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fs.Initialize();

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await format.ResolveSongAsync(req, fs));
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch { }
        }
    }

    class DummySongDataLoader : ISongDataLoader
    {
        public JDUbiArtSong LoadSongData(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem) => new() { Name = request.SongName ?? "", SongDesc = new SongDesc { Components = [new InfoComponent { JDVersion = 2022, OriginalJDVersion = 2022 }] } };
        public SongDesc LoadSongDesc(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem) => new() { Components = [new InfoComponent { JDVersion = 2022, OriginalJDVersion = 2022 }] };
    }

    class DummyEngineDetector : IUbiArtEngineDetector
    {
        public UbiArtVersionProfile Detect(string inputPath) => new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
    }
}
