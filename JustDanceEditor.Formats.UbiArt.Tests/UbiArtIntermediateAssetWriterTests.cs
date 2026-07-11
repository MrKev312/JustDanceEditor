using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Import.Intermediate;
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

public sealed class UbiArtIntermediateAssetWriterTests
{
    [Theory]
    [InlineData(UbiArtPlatform.Durango, UbiArtGestureFolders.Durango)]
    [InlineData(UbiArtPlatform.Orbis, UbiArtGestureFolders.Orbis)]
    [InlineData(UbiArtPlatform.Xenon, UbiArtGestureFolders.X360)]
    [InlineData(UbiArtPlatform.Uncooked, "wii")]
    public async Task PopulateFromUbiArtAsync_CopiesGesturesToSourcePlatformFolder(UbiArtPlatform platform, string sourcePlatformFolder)
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string inputRoot = Path.Combine(root, "input");
        string outputRoot = Path.Combine(root, "jdi");

        try
        {
            string sourceGesturesFolder = Path.Combine(inputRoot, "world", "maps", "song", "timeline", "moves", sourcePlatformFolder);
            Directory.CreateDirectory(sourceGesturesFolder);
            await File.WriteAllTextAsync(Path.Combine(sourceGesturesFolder, "full_move.gesture"), "GESTURE", TestContext.Current.CancellationToken);

            UbiArtVersionProfile profile = new(platform, UbiArtEngineVersion.JD2022, new UbiArtLayoutResolver(), new JsonUbiArtSerializer());
            UbiArtConversionRequest request = new(inputRoot, root, "song") { Type = CookedType.Cooked };
            using JustDanceUbiArtFileSystem fileSystem = new(request, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fileSystem.Initialize();
            fileSystem.UpdateSongName("song");
            fileSystem.TempFolders.CreateTempFolders();

            IntermediateSongPackage package = new();
            ConversionContext context = new(request, fileSystem)
            {
                IntermediatePackage = package,
                SongData = CreateSong("song")
            };

            await IntermediateAssetWriter.PopulateFromUbiArtAsync(
                context,
                package,
                outputRoot,
                NullLogger.Instance,
                new NullTextureService(),
                new ThrowingAudioConverter());

            Assert.True(File.Exists(Path.Combine(IntermediatePackageLayout.Resolve(outputRoot, UbiArtGestureFolders.PackageFolder(sourcePlatformFolder)), "full_move.gesture")));
            Assert.False(File.Exists(Path.Combine(outputRoot, "assets", "gestures", "full_move.gesture")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static JDUbiArtSong CreateSong(string songName) =>
        new()
        {
            Name = songName,
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
                                EndBeat = 1
                            }
                        }
                    }
                ]
            }
        };

    private sealed class NullTextureService : ITextureService
    {
        public Image<Bgra32>? ConvertToImage(Stream stream) => null;

        public Task ConvertTextureAsync(Stream inputStream, string outputPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingAudioConverter : IAudioConverter
    {
        public Task<WaveStream> ConvertAsync(Stream source, string sourceFileName) =>
            throw new NotSupportedException("The gesture import test should not transcode audio.");
    }
}
