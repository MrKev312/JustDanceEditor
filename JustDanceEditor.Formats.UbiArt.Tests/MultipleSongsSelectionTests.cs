using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public class MultipleSongsSelectionTests
{
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
                fileSystemFactory: (r, p) => new LayeredFileSystem(r, p, NullLogger<LayeredFileSystem>.Instance),
                engineDetector: new DummyEngineDetector(),
                audioConverter: null!,
                mediaProcessor: null!,
                textureService: null!,
                assetWriter: null!,
                logger: NullLogger<UbiArtJdiFormat>.Instance);

            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            LayeredFileSystem fs = new(req, profile, NullLogger<LayeredFileSystem>.Instance);
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
                fileSystemFactory: (r, p) => new LayeredFileSystem(r, p, NullLogger<LayeredFileSystem>.Instance),
                engineDetector: new DummyEngineDetector(),
                audioConverter: null!,
                mediaProcessor: null!,
                textureService: null!,
                assetWriter: null!,
                logger: NullLogger<UbiArtJdiFormat>.Instance);

            UbiArtVersionProfile profile = new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
            LayeredFileSystem fs = new(req, profile, NullLogger<LayeredFileSystem>.Instance);
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
        public JDUbiArtSong LoadSongData(UbiArtConversionRequest request, LayeredFileSystem fileSystem) => new() { Name = request.SongName ?? "", SongDesc = new SongDesc { Components = [new InfoComponent { JDVersion = 2022, OriginalJDVersion = 2022 }] } };
        public SongDesc LoadSongDesc(UbiArtConversionRequest request, LayeredFileSystem fileSystem) => new() { Components = [new InfoComponent { JDVersion = 2022, OriginalJDVersion = 2022 }] };
    }

    class DummyEngineDetector : IUbiArtEngineDetector
    {
        public UbiArtVersionProfile Detect(string inputPath) => new(UbiArtPlatform.Uncooked, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new LuaUbiArtSerializer());
    }
}