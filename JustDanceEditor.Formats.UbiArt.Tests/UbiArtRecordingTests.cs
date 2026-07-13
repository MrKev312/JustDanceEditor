using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;
using JustDanceEditor.Formats.UbiArt.Import.Recordings;
using JustDanceEditor.Formats.UbiArt.Recordings;
using JustDanceEditor.Formats.UbiArt.Serialization.Binary;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace JustDanceEditor.Formats.UbiArt.Tests;

public sealed class UbiArtRecordingTests
{
    [Fact]
    public async Task UncookedRecordingImporter_ImportsRecAndSnippingTable()
    {
        string root = CreateTempRoot();
        string output = CreateTempRoot();
        try
        {
            string timeline = Path.Combine(root, "world", "maps", "testmap", "timeline");
            string recordingFolder = Path.Combine(timeline, "Recording");
            string snippingFolder = Path.Combine(timeline, "Snipping");
            Directory.CreateDirectory(recordingFolder);
            Directory.CreateDirectory(snippingFolder);

            string recordingName = "Moves2_TestMap_Dancer_2026-07-13_12-30-45.rec";
            await using (FileStream stream = File.Create(Path.Combine(recordingFolder, recordingName)))
            {
                RecMotionRecordingCodec.Write(stream, new MotionRecordingDocument
                {
                    MapName = "TestMap",
                    Samples =
                    {
                        new() { MapTime = 1, AccX = 1, AccY = 2, AccZ = 3 },
                        new() { MapTime = 2, AccX = 4, AccY = 5, AccZ = 6 }
                    }
                }, RecMotionFormat.Modern);
            }
            await File.WriteAllTextAsync(
                Path.Combine(snippingFolder, "Moves2_TestMap_validation.csv"),
                $";move_a;move_b\nMarker;24;48\n{recordingName};1;0\n",
                TestContext.Current.CancellationToken);

            UbiArtConversionRequest request = new(root, output, "testmap") { Type = CookedType.Uncooked };
            UbiArtVersionProfile profile = new(
                UbiArtPlatform.Uncooked,
                UbiArtEngineVersion.JD2022,
                new UbiArtLayoutResolver(),
                new LuaUbiArtSerializer());
            using JustDanceUbiArtFileSystem fileSystem = new(request, profile, NullLogger<JustDanceUbiArtFileSystem>.Instance);
            fileSystem.Initialize();
            ConversionContext context = new(request, fileSystem)
            {
                IntermediatePackage = new IntermediateSongPackage()
            };
            context.IntermediatePackage.Metadata.MapName = "TestMap";
            context.IntermediatePackage.CoachTimelines.Add(new MoveTimeline
            {
                CoachId = 1,
                Clips =
                {
                    new MoveClip { Id = 101, MoveId = "move_a", StartTime = 24 },
                    new MoveClip { Id = 102, MoveId = "move_b", StartTime = 48 }
                }
            });

            await UbiArtRecordingImporter.ImportAsync(
                context,
                output,
                NullLogger.Instance,
                TestContext.Current.CancellationToken);

            JsonMotionRecordingRepository repository = new();
            string importedPath = Assert.Single(repository.ListRecordingFiles(output, coachId: 1));
            MotionRecordingDocument imported = await repository.LoadAsync(importedPath, TestContext.Current.CancellationToken);
            Assert.Equal(2, imported.Samples.Count);
            MotionTrainingSelectionDocument selection = await new JsonMotionTrainingSelectionRepository()
                .LoadAsync(output, TestContext.Current.CancellationToken);
            MotionTrainingExclusion exclusion = Assert.Single(selection.Exclusions);
            Assert.Equal(imported.RecordingId, exclusion.RecordingId);
            Assert.Equal(1, exclusion.CoachId);
            Assert.Equal(102, exclusion.TimelineClipId);
            Assert.Equal("move_b", exclusion.MoveId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(output, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, "NX_ACCQD")]
    [InlineData(true, "WII_ACCQ")]
    public void RecCodec_RoundTripsNormalizedMotionSamples(bool isWii, string expectedFormatName)
    {
        MotionRecordingDocument source = new()
        {
            CoachId = 1,
            MapName = "TestMap",
            Samples =
            {
                new() { MapTime = 1.25, AccX = 1, AccY = 2, AccZ = 3, GyroX = 4, GyroY = 5, GyroZ = 6 },
                new() { MapTime = 1.25, AccX = 7, AccY = 8, AccZ = 9, GyroX = 10, GyroY = 11, GyroZ = 12 },
                new() { MapTime = 1.30, AccX = 13, AccY = 14, AccZ = 15, GyroX = 16, GyroY = 17, GyroZ = 18 }
            }
        };

        using MemoryStream stream = new();
        RecMotionRecordingCodec.Write(stream, source, isWii ? RecMotionFormat.Wii : RecMotionFormat.Modern);
        Assert.Equal(expectedFormatName, Encoding.ASCII.GetString(stream.ToArray(), 4, 8));
        stream.Position = 0;
        MotionRecordingDocument imported = Assert.Single(RecMotionRecordingCodec.Read(
            stream,
            firstCoachId: 1,
            sourceFileName: "Moves2_TestMap_Dancer_2026-07-13_12-30-45.rec"));

        Assert.Equal(1, imported.CoachId);
        Assert.Equal("TestMap", imported.MapName);
        Assert.Equal(3, imported.Samples.Count);
        Assert.Equal(1.25, imported.Samples[0].MapTime, 6);
        Assert.Equal(7, imported.Samples[1].AccX);
        Assert.Equal(18, imported.Samples[2].GyroZ);
        Assert.Equal(1.30, imported.TimelineEndSeconds, 6);
    }

    [Theory]
    [InlineData("Moves1_Map_Dancer.rec", 0)]
    [InlineData("Moves_Map_Dancer.rec", 0)]
    [InlineData("moves4_map.rec", 3)]
    [InlineData("Map_Dancer.rec", null)]
    public void RecCodec_InfersCoachFromMoveLayer(string fileName, int? expectedCoach)
    {
        Assert.Equal(expectedCoach, RecMotionRecordingCodec.InferCoachId(fileName));
    }

    [Fact]
    public async Task UncookedExport_WritesRecAndSnippingFiles()
    {
        string packageRoot = CreateTempRoot();
        string output = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = new();
            package.Metadata.MapName = "TestMap";
            MoveTimeline coach = new() { CoachId = 0 };
            coach.Clips.Add(new MoveClip { Id = 101, MoveId = "move_a", StartTime = 24 });
            package.CoachTimelines.Add(coach);

            JsonMotionRecordingRepository repository = new();
            MotionRecordingDocument recording = new()
            {
                CoachId = 0,
                MapName = "TestMap",
                StartedAtUtc = new DateTimeOffset(2026, 7, 13, 12, 30, 45, TimeSpan.Zero),
                Samples =
                {
                    new() { MapTime = 1, AccX = 1, AccY = 2, AccZ = 3 },
                    new() { MapTime = 2, AccX = 4, AccY = 5, AccZ = 6 }
                }
            };
            await repository.SaveAsync(packageRoot, recording, TestContext.Current.CancellationToken);
            MotionTrainingSelectionDocument selection = new();
            selection.SetExcluded(recording.RecordingId, 0, 101, "move_a", 0, isExcluded: true);
            await new JsonMotionTrainingSelectionRepository().SaveAsync(
                packageRoot,
                selection,
                TestContext.Current.CancellationToken);

            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);
            await writer.ExportAsync(
                package,
                packageRoot,
                output,
                UbiArtPlatform.Uncooked,
                UbiArtEngineVersion.JD2022,
                new UbiArtLayoutResolver());

            string recordingFolder = Path.Combine(output, "world", "maps", "testmap", "timeline", "Recording");
            string recPath = Assert.Single(Directory.GetFiles(recordingFolder, "*.rec"));
            await using FileStream recStream = File.OpenRead(recPath);
            MotionRecordingDocument roundTrip = Assert.Single(RecMotionRecordingCodec.Read(recStream, 0, Path.GetFileName(recPath)));
            Assert.Equal(2, roundTrip.Samples.Count);

            string snippingPath = Path.Combine(output, "world", "maps", "testmap", "timeline", "Snipping", "Moves1_TestMap_validation.csv");
            Assert.True(File.Exists(snippingPath));
            string snipping = await File.ReadAllTextAsync(snippingPath, TestContext.Current.CancellationToken);
            Assert.Contains("move_a", snipping);
            Assert.Contains(";0", snipping);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
            Directory.Delete(output, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "jde_rec_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}