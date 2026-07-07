using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.Export;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Export.Platform;
using JustDanceEditor.Formats.UbiArt.Import;
using JustDanceEditor.Formats.UbiArt.Import.Layouts;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging.Abstractions;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Xunit;
namespace JustDanceEditor.Formats.UbiArt.Tests;

public class DurangoExportTests
{
    [Fact]
    public void ModernGenerator_Writes_HandMsm_And_FullBodyGesture_Clips()
    {
        IntermediateSongPackage package = CreateMotionPackage();
        ModernEngineContentGenerator generator = new(UbiArtEngineVersion.JD2021);

        byte[] bytes = UbiArtEngineContentSerializer.Serialize(generator.GenerateDanceTape(package));
        using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));

        JsonElement clips = GetRequiredProperty(document.RootElement, "Clips", "clips");
        Assert.Contains(clips.EnumerateArray(), clip =>
            IsMotionClip(clip, 0, "hand_move.msm"));
        Assert.Contains(clips.EnumerateArray(), clip =>
            IsMotionClip(clip, 1, "full_move.gesture"));
    }

    [Fact]
    public async Task AssetWriter_Durango_Exports_Msm_To_Wiiu_And_Gestures_To_Durango()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string materializedRoot = Path.Combine(root, "jdi");
        string outputRoot = Path.Combine(root, "out");

        try
        {
            Directory.CreateDirectory(Path.Combine(materializedRoot, "assets", "moves"));
            string gesturesRoot = IntermediatePackageLayout.Resolve(materializedRoot, UbiArtGestureFolders.PackageFolder(UbiArtGestureFolders.Durango));
            Directory.CreateDirectory(gesturesRoot);
            File.WriteAllText(Path.Combine(materializedRoot, "assets", "moves", "hand_move.msm"), "MSM");
            File.WriteAllText(Path.Combine(gesturesRoot, "full_move.gesture"), "GESTURE");

            UbiArtAssetWriter writer = new(NullLogger<UbiArtAssetWriter>.Instance);
            await writer.ExportAsync(
                CreateMotionPackage(),
                materializedRoot,
                outputRoot,
                UbiArtPlatform.Durango,
                UbiArtEngineVersion.JD2021,
                io: new SystemFileSystem());

            Assert.True(File.Exists(Path.Combine(outputRoot, "world", "maps", "testmap", "timeline", "moves", "wiiu", "hand_move.msm")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "world", "maps", "testmap", "timeline", "moves", "durango", "full_move.gesture")));

            string danceTape = File.ReadAllText(Path.Combine(outputRoot, "cache", "itf_cooked", "durango", "world", "maps", "testmap", "timeline", "testmap_tml_dance.dtape.ckd")).TrimEnd('\0');
            Assert.Contains("hand_move.msm", danceTape, StringComparison.Ordinal);
            Assert.Contains("full_move.gesture", danceTape, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DurangoExporter_Writes_Audio_As_PcmRaki()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string sourceWav = Path.Combine(root, "source.wav");

        try
        {
            Directory.CreateDirectory(root);
            WriteSilentPcmWave(sourceWav);

            DurangoCookedPlatformExporter exporter = new();
            ExportContext context = new(root, new UbiArtLayoutResolver(), new SystemFileSystem());

            await exporter.WriteAudioAsync(context, Path.Combine("cache", "itf_cooked", "durango", "world", "maps", "song", "audio", "song.wav"), new UbiArtAudioExportSource(sourceWav));

            byte[] output = File.ReadAllBytes(Path.Combine(root, "cache", "itf_cooked", "durango", "world", "maps", "song", "audio", "song.wav.ckd"));
            Assert.Equal("RAKI", Encoding.ASCII.GetString(output, 0, 4));
            Assert.Equal("Dura", Encoding.ASCII.GetString(output, 8, 4));
            Assert.Equal("pcm ", Encoding.ASCII.GetString(output, 12, 4));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static bool IsMotionClip(JsonElement clip, int moveType, string classifierSuffix)
    {
        return clip.TryGetProperty("__class", out JsonElement className) &&
            className.GetString() == "MotionClip" &&
            TryGetProperty(clip, out JsonElement clipMoveType, "MoveType", "moveType") &&
            clipMoveType.GetInt32() == moveType &&
            TryGetProperty(clip, out JsonElement classifierPath, "ClassifierPath", "classifierPath") &&
            classifierPath.GetString()?.EndsWith(classifierSuffix, StringComparison.Ordinal) == true;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, params string[] names)
    {
        if (TryGetProperty(element, out JsonElement value, names))
            return value;

        throw new InvalidOperationException($"None of the expected properties were present: {string.Join(", ", names)}");
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out value))
                return true;
        }

        value = default;
        return false;
    }

    private static void WriteSilentPcmWave(string path)
    {
        const ushort channels = 2;
        const uint sampleRate = 48000;
        const ushort bitsPerSample = 16;
        const ushort blockAlign = channels * bitsPerSample / 8;
        const uint byteRate = sampleRate * blockAlign;
        const uint dataSize = blockAlign * 16u;

        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream, Encoding.ASCII);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36u + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16u);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[(int)dataSize]);
    }

    private static IntermediateSongPackage CreateMotionPackage()
    {
        return new IntermediateSongPackage
        {
            Metadata = new IntermediateMetadata
            {
                SongID = Guid.NewGuid(),
                MapName = "testmap",
                ParentMapName = "testmap",
                Title = "Test Map",
                Artist = "Test Artist",
                OriginalJDVersion = 2021,
                CoachCount = 0,
                LyricsColor = "#FFFFFFFF"
            },
            TimelineStructure = new TimelineStructureDocument
            {
                StartBeat = 0,
                EndBeat = 8,
                Markers = [0, 48000, 96000]
            },
            CoachTimelines =
            [
                new MoveTimeline
                {
                    CoachId = 0,
                    TrackId = 1,
                    Clips =
                    [
                        new MoveClip
                        {
                            Id = 1,
                            StartTime = 0,
                            MoveId = "hand_move"
                        }
                    ]
                }
            ],
            FullBodyCoachTimelines =
            [
                new MoveTimeline
                {
                    CoachId = 0,
                    TrackId = 2,
                    Clips =
                    [
                        new MoveClip
                        {
                            Id = 2,
                            StartTime = 24,
                            MoveId = "full_move"
                        }
                    ]
                }
            ],
            HandCoachMoves =
            {
                ["hand_move"] = new CoachMoveDefinition
                {
                    Color = "#FF0000FF",
                    Duration = 24,
                    MoveType = CoachMoveType.HandTracking
                }
            },
            FullBodyCoachMoves =
            {
                ["full_move"] = new CoachMoveDefinition
                {
                    Color = "#00FF00FF",
                    Duration = 24,
                    MoveType = CoachMoveType.FullBodyTracking
                }
            }
        };
    }
}
