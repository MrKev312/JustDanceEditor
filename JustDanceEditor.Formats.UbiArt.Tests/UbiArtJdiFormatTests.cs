using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.Audio.NAudio;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using NAudio.Wave;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class UbiArtJdiFormatTests
{
    [Fact]
    public async Task ImportAsync_WhenOutputParentIsInputParent_RedirectsMaterializedRootAndPreservesSource()
    {
        string parentRoot = CreateTempDirectory();
        string inputRoot = Path.Combine(parentRoot, "makeba");
        string? materializedRoot = null;

        try
        {
            CreateUbiArtInput(inputRoot, "makeba");
            File.WriteAllText(Path.Combine(inputRoot, "source.keep"), "keep");

            UbiArtVersionProfile profile = new(
                UbiArtPlatform.Uncooked,
                UbiArtEngineVersion.JD2022,
                new UbiArtLayoutResolver(),
                new JsonUbiArtSerializer());

            UbiArtJdiFormat format = new(
                new FakeSongDataLoader(CreateSong("makeba")),
                (request, detectedProfile) => new JustDanceUbiArtFileSystem(request, detectedProfile, NullLogger<JustDanceUbiArtFileSystem>.Instance),
                new FakeEngineDetector(profile),
                new ThrowingAudioConverter(),
                new FakeTextureService(),
                assetWriter: null,
                NullLogger<UbiArtJdiFormat>.Instance);

            JdiImportResult result = await format.ImportAsync(
                new UbiArtConversionRequest(inputRoot, parentRoot, "makeba")
                {
                    Type = CookedType.Uncooked
                },
                TestContext.Current.CancellationToken);

            materializedRoot = result.MaterializedRoot;
            Assert.NotNull(materializedRoot);
            Assert.False(MaterializedOutputPathResolver.PathsOverlap(inputRoot, materializedRoot));
            Assert.True(File.Exists(Path.Combine(materializedRoot, "metadata.json")));
            Assert.True(File.Exists(Path.Combine(inputRoot, "world", "maps", "makeba", "songdesc.tpl")));
            Assert.True(File.Exists(Path.Combine(inputRoot, "source.keep")));
        }
        finally
        {
            if (Directory.Exists(parentRoot))
                Directory.Delete(parentRoot, true);

            if (!string.IsNullOrWhiteSpace(materializedRoot) && Directory.Exists(materializedRoot))
                Directory.Delete(materializedRoot, true);
        }
    }

    private static JDUbiArtSong CreateSong(string songName) =>
        new()
        {
            Name = songName,
            JDVersion = 2022,
            SongDesc = new SongDesc
            {
                Components =
                [
                    new InfoComponent
                    {
                        MapName = songName,
                        Title = "Makeba",
                        Artist = "Test Artist",
                        NumCoach = 1,
                        Difficulty = 1,
                        SweatDifficulty = 1,
                        OriginalJDVersion = 2022
                    }
                ]
            },
            MusicTrack = new MusicTrack
            {
                Components =
                [
                    new TrackDataHolder
                    {
                        TrackData = new TrackData
                        {
                            Structure = new Structure
                            {
                                Markers = [0, 48000],
                                EndBeat = 1,
                                PreviewDuration = 30
                            }
                        }
                    }
                ]
            }
        };

    private static void CreateUbiArtInput(string inputRoot, string songName)
    {
        string mapRoot = Path.Combine(inputRoot, "world", "maps", songName);
        Directory.CreateDirectory(mapRoot);
        File.WriteAllText(Path.Combine(mapRoot, "songdesc.tpl"), """
        { "COMPONENTS": [ { "MapName": "makeba", "JDVersion": 4884 } ] }
        """);

        string flatRoot = Path.Combine(inputRoot, songName);
        Directory.CreateDirectory(flatRoot);
        File.WriteAllText(Path.Combine(flatRoot, "songdesc.tpl"), """
        { "COMPONENTS": [ { "MapName": "makeba", "JDVersion": 4884 } ] }
        """);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "JustDanceEditor.UbiArt.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeSongDataLoader(JDUbiArtSong song) : ISongDataLoader
    {
        public JDUbiArtSong LoadSongData(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem) => song;

        public SongDesc LoadSongDesc(UbiArtConversionRequest request, JustDanceUbiArtFileSystem fileSystem) => song.SongDesc;
    }

    private sealed class FakeEngineDetector(UbiArtVersionProfile profile) : IUbiArtEngineDetector
    {
        public UbiArtVersionProfile Detect(string inputPath, string? mapName = null) => profile;
    }

    private sealed class ThrowingAudioConverter : IAudioConverter
    {
        public Task<WaveStream> ConvertAsync(Stream source, string sourceFileName) =>
            throw new NotSupportedException("The path-safety test should not transcode audio.");
    }

    private sealed class FakeTextureService : ITextureService
    {
        public Image<Bgra32>? ConvertToImage(Stream stream) => null;

        public async Task ConvertTextureAsync(Stream inputStream, string outputPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));
            await using FileStream output = File.Create(outputPath);
            await inputStream.CopyToAsync(output, cancellationToken);
        }
    }
}
