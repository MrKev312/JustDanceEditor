using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
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