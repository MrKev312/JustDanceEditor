using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Scoring;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Text.Json;

using Xunit;

namespace JustDanceEditor.Formats.JDNextPC.Tests;

public class JDNextPCJdiFormatTests
{
    [Fact]
    public async Task ExportAsync_WritesExpectedCoreJDNextFiles()
    {
        string packageRoot = CreateTempDirectory();
        string outputRoot = CreateTempDirectory();

        try
        {
            IntermediateSongPackage package = CreateJdiPackage("Makeba");
            CreateJdiExportAssets(packageRoot, coachCount: 3);
            JDNextPCJdiFormat format = CreateFormat();

            await format.ExportAsync(
                new JdiImportResult(package, "JDI", packageRoot, MaterializedRootIsTemporary: false),
                new JDNextPCConversionRequest(packageRoot, outputRoot),
                cancellationToken: TestContext.Current.CancellationToken);

            string songRoot = Path.Combine(outputRoot, "makeba");
            Assert.True(Directory.Exists(songRoot));
            Assert.True(File.Exists(Path.Combine(songRoot, "songdesc.json")));
            Assert.True(File.Exists(Path.Combine(songRoot, "timeline.json")));
            Assert.True(File.Exists(Path.Combine(songRoot, "musictrack.json")));
            Assert.True(File.Exists(Path.Combine(songRoot, "media", "makeba.ogg")));
            Assert.True(File.Exists(Path.Combine(songRoot, "media", "makeba.webm")));
            Assert.True(File.Exists(Path.Combine(songRoot, "menuart", "cover.png")));
            Assert.True(File.Exists(Path.Combine(songRoot, "menuart", "title.png")));
            Assert.True(File.Exists(Path.Combine(songRoot, "menuart", "bkg.png")));
            Assert.True(File.Exists(Path.Combine(songRoot, "menuart", "coach01.png")));
            Assert.True(File.Exists(Path.Combine(songRoot, "menuart", "coach02.png")));
            Assert.True(File.Exists(Path.Combine(songRoot, "menuart", "coach03.png")));

            using JsonDocument songDesc = JsonDocument.Parse(File.ReadAllText(Path.Combine(songRoot, "songdesc.json")));
            Assert.Equal("Makeba", songDesc.RootElement.GetProperty("title").GetString());
            Assert.Equal(3, songDesc.RootElement.GetProperty("numCoach").GetInt32());

            using JsonDocument timeline = JsonDocument.Parse(File.ReadAllText(Path.Combine(songRoot, "timeline.json")));
            Assert.True(timeline.RootElement.GetProperty("lyrics").GetArrayLength() > 0);
            Assert.True(timeline.RootElement.GetProperty("pictos").GetArrayLength() > 0);
            Assert.True(timeline.RootElement.GetProperty("moves").GetArrayLength() > 0);

            JsonElement firstMove = timeline.RootElement.GetProperty("moves")[0];
            Assert.Equal("makeba_tui", firstMove.GetProperty("name").GetString());
            Assert.Equal(0, firstMove.GetProperty("coachID").GetInt32());
        }
        finally
        {
            Directory.Delete(packageRoot, true);
            Directory.Delete(outputRoot, true);
        }
    }

    [Fact]
    public async Task ImportAsync_BuildsJdiPackageFromJDNextCoreFiles()
    {
        string jdNextRoot = CreateTempDirectory();
        string outputRoot = CreateTempDirectory();

        try
        {
            CreateJdNextInput(jdNextRoot);
            JDNextPCJdiFormat format = CreateFormat();
            JdiImportResult result = await format.ImportAsync(new JDNextPCConversionRequest(jdNextRoot, outputRoot), cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("JDNext PC", result.SourceFormat);
            Assert.Equal("Synthetic Song", result.Package.Metadata.Title);
            Assert.Equal("Synthetic Artist", result.Package.Metadata.Artist);
            Assert.Equal(2, result.Package.Metadata.CoachCount);
            Assert.Empty(result.Package.FullBodyCoachTimelines);
            Assert.True(result.Package.Lyrics.Clips.Count > 0);
            Assert.True(result.Package.Pictograms.Clips.Count > 0);
            Assert.Equal(2, result.Package.CoachTimelines.Count);
            Assert.Contains("synthetic_move", result.Package.HandCoachMoves.Keys);
            string materializedRoot = result.MaterializedRoot ?? throw new InvalidOperationException("Expected a materialized root for the import result.");
            Assert.True(File.Exists(Path.Combine(materializedRoot, "metadata.json")));
            foreach (MotionClassifierFormatVersion version in JdiMotionClassifierStorage.StoredVersions)
                Assert.True(File.Exists(JdiMotionClassifierStorage.GetVersionPath(materializedRoot, "synthetic_move.msm", version)));
            Assert.True(File.Exists(Path.Combine(materializedRoot, "assets", "coverAssets", "cover.webp")));
        }
        finally
        {
            Directory.Delete(jdNextRoot, true);
            Directory.Delete(outputRoot, true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData("/escape")]
    [InlineData(@"C:\escape")]
    public async Task ImportAsync_RejectsUnsafePictogramIdsBeforeMaterializingAssets(string unsafePictogramId)
    {
        string jdNextRoot = CreateTempDirectory();
        string outputRoot = CreateTempDirectory();

        try
        {
            CreateJdNextInput(jdNextRoot);
            ReplaceTimelinePictogramId(jdNextRoot, "picto_a", unsafePictogramId);
            WriteBytes(Path.Combine(jdNextRoot, "escape.png"), "ESCAPE"u8.ToArray());

            JDNextPCJdiFormat format = CreateFormat();

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await format.ImportAsync(
                    new JDNextPCConversionRequest(jdNextRoot, outputRoot),
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal("pictogramId", exception.ParamName);
            Assert.Empty(Directory.EnumerateFileSystemEntries(outputRoot));
        }
        finally
        {
            Directory.Delete(jdNextRoot, true);
            Directory.Delete(outputRoot, true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData("/escape")]
    [InlineData(@"C:\escape")]
    public async Task ExportAsync_RejectsUnsafePictogramIdsBeforeWritingOutput(string unsafePictogramId)
    {
        string packageRoot = CreateTempDirectory();
        string outputRoot = CreateTempDirectory();

        try
        {
            IntermediateSongPackage package = CreateMinimalPackage("Traversal");
            package.Pictograms.Clips.Add(new PictogramClip
            {
                Id = 1,
                StartTime = 0,
                Duration = 24,
                PictogramId = unsafePictogramId,
                CoachCount = -1
            });
            WriteBytes(Path.Combine(packageRoot, "assets", "escape.webp"), "ESCAPE"u8.ToArray());

            JDNextPCJdiFormat format = CreateFormat();

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await format.ExportAsync(
                    new JdiImportResult(package, "JDI", packageRoot, MaterializedRootIsTemporary: false),
                    new JDNextPCConversionRequest(packageRoot, outputRoot),
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal("pictogramId", exception.ParamName);
            Assert.Empty(Directory.EnumerateFileSystemEntries(outputRoot));
        }
        finally
        {
            Directory.Delete(packageRoot, true);
            Directory.Delete(outputRoot, true);
        }
    }

    [Fact]
    public async Task ImportAsync_WhenOutputParentIsInputParent_RedirectsMaterializedRootAndPreservesSource()
    {
        string parentRoot = CreateTempDirectory();
        string jdNextRoot = Path.Combine(parentRoot, "makeba");
        string? materializedRoot = null;

        try
        {
            Directory.CreateDirectory(jdNextRoot);
            CreateJdNextInput(jdNextRoot);
            File.WriteAllText(Path.Combine(jdNextRoot, "source.keep"), "keep");

            JDNextPCJdiFormat format = CreateFormat();
            JdiImportResult result = await format.ImportAsync(
                new JDNextPCConversionRequest(jdNextRoot, parentRoot),
                cancellationToken: TestContext.Current.CancellationToken);

            materializedRoot = result.MaterializedRoot;
            Assert.NotNull(materializedRoot);
            Assert.False(MaterializedOutputPathResolver.PathsOverlap(jdNextRoot, materializedRoot));
            Assert.True(File.Exists(Path.Combine(materializedRoot, "metadata.json")));
            Assert.True(File.Exists(Path.Combine(jdNextRoot, "songdesc.json")));
            Assert.True(File.Exists(Path.Combine(jdNextRoot, "timeline.json")));
            Assert.True(File.Exists(Path.Combine(jdNextRoot, "musictrack.json")));
            Assert.True(File.Exists(Path.Combine(jdNextRoot, "source.keep")));
        }
        finally
        {
            if (Directory.Exists(parentRoot))
                Directory.Delete(parentRoot, true);

            if (!string.IsNullOrWhiteSpace(materializedRoot) && Directory.Exists(materializedRoot))
                Directory.Delete(materializedRoot, true);
        }
    }

    [Fact]
    public void Check_ReturnsTrueForJDNextSongFolder()
    {
        string jdNextRoot = CreateTempDirectory();

        try
        {
            CreateJdNextInput(jdNextRoot);

            JDNextPCJdiFormat format = CreateFormat();

            Assert.True(format.Check(jdNextRoot));
        }
        finally
        {
            Directory.Delete(jdNextRoot, true);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenOutputParentIsSourceParent_RedirectsOutputRootAndPreservesSource()
    {
        string parentRoot = CreateTempDirectory();
        string packageRoot = Path.Combine(parentRoot, "makeba");

        try
        {
            Directory.CreateDirectory(packageRoot);
            IntermediateSongPackage package = CreateJdiPackage("Makeba");
            CreateJdiExportAssets(packageRoot, coachCount: 3);
            File.WriteAllText(Path.Combine(packageRoot, "source.keep"), "keep");

            JDNextPCJdiFormat format = CreateFormat();
            await format.ExportAsync(
                new JdiImportResult(package, "JDI", packageRoot, MaterializedRootIsTemporary: false),
                new JDNextPCConversionRequest(packageRoot, parentRoot),
                cancellationToken: TestContext.Current.CancellationToken);

            string outputRoot = Assert.Single(
                Directory.GetDirectories(parentRoot),
                path => !Path.GetFileName(path).Equals("makeba", StringComparison.OrdinalIgnoreCase));

            Assert.False(MaterializedOutputPathResolver.PathsOverlap(packageRoot, outputRoot));
            Assert.True(File.Exists(Path.Combine(outputRoot, "songdesc.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "timeline.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "musictrack.json")));
            Assert.True(File.Exists(Path.Combine(packageRoot, "source.keep")));
            Assert.True(File.Exists(Path.Combine(packageRoot, "assets", "audio", "master.opus")));
        }
        finally
        {
            if (Directory.Exists(parentRoot))
                Directory.Delete(parentRoot, true);
        }
    }

    [Fact]
    public async Task ExportAsync_RequestsCachedVp9ServerVideo()
    {
        string packageRoot = CreateTempDirectory();
        string outputRoot = CreateTempDirectory();

        try
        {
            RecordingMediaProcessor mediaProcessor = new();
            JDNextPCJdiFormat format = new(mediaProcessor, new FakeTextureService(), NullLogger<JDNextPCJdiFormat>.Instance);
            IntermediateSongPackage package = CreateMinimalPackage("VideoPassThrough");

            string videoRoot = Path.Combine(packageRoot, "assets", "video");
            Directory.CreateDirectory(videoRoot);
            byte[] smallVideo = CreatePseudoWebmBytes(32, isVp8: true);
            byte[] largeVideo = CreatePseudoWebmBytes(96, isVp8: true);
            File.WriteAllBytes(Path.Combine(videoRoot, "small.webm"), smallVideo);
            string largeVideoPath = Path.Combine(videoRoot, "large.webm");
            File.WriteAllBytes(largeVideoPath, largeVideo);

            await format.ExportAsync(
                new JdiImportResult(package, "JDI", packageRoot, MaterializedRootIsTemporary: false),
                new JDNextPCConversionRequest(packageRoot, outputRoot),
                cancellationToken: TestContext.Current.CancellationToken);

            string exportedVideoPath = Path.Combine(outputRoot, "videopassthrough", "media", "videopassthrough.webm");
            Assert.True(File.Exists(exportedVideoPath));
            Assert.Equal(largeVideo, File.ReadAllBytes(exportedVideoPath));
            MediaCall videoCall = Assert.Single(mediaProcessor.Calls, call => call.VideoRequest is not null);
            Assert.Equal(largeVideoPath, videoCall.Input);
            Assert.EndsWith(Path.Combine("scratch", "video", "jdnextpc_vp9.webm"), videoCall.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("vp9", videoCall.VideoRequest!.Codec);
        }
        finally
        {
            Directory.Delete(packageRoot, true);
            Directory.Delete(outputRoot, true);
        }
    }

    [Fact]
    public async Task ExportAsync_UsesServerVp9SettingsForNonVp9Video()
    {
        string packageRoot = CreateTempDirectory();
        string outputRoot = CreateTempDirectory();

        try
        {
            RecordingMediaProcessor mediaProcessor = new();
            JDNextPCJdiFormat format = new(mediaProcessor, new FakeTextureService(), NullLogger<JDNextPCJdiFormat>.Instance);
            IntermediateSongPackage package = CreateMinimalPackage("VideoTranscode");

            string videoRoot = Path.Combine(packageRoot, "assets", "video");
            Directory.CreateDirectory(videoRoot);
            File.WriteAllBytes(Path.Combine(videoRoot, "small.webm"), CreatePseudoWebmBytes(24, isVp8: true));
            byte[] largeNonVp8Video = CreatePseudoWebmBytes(88, isVp8: false);
            File.WriteAllBytes(Path.Combine(videoRoot, "large.webm"), largeNonVp8Video);

            await format.ExportAsync(
                new JdiImportResult(package, "JDI", packageRoot, MaterializedRootIsTemporary: false),
                new JDNextPCConversionRequest(packageRoot, outputRoot),
                cancellationToken: TestContext.Current.CancellationToken);

            MediaCall videoCall = Assert.Single(mediaProcessor.Calls, call => call.Output.EndsWith(".webm", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.Combine(videoRoot, "large.webm"), videoCall.Input);
            Assert.NotNull(videoCall.VideoRequest);
            Assert.Equal("vp9", videoCall.VideoRequest.Codec);
            Assert.Equal(0, videoCall.VideoRequest.Encoding.Bitrate);
            Assert.Equal(10, videoCall.VideoRequest.Encoding.ConstantRateFactor);
            Assert.Equal("yuv420p", videoCall.VideoRequest.Encoding.PixelFormat);
            Assert.Equal(4, videoCall.VideoRequest.Encoding.Speed);
            Assert.True(videoCall.VideoRequest.Encoding.RowMultithreading);
        }
        finally
        {
            Directory.Delete(packageRoot, true);
            Directory.Delete(outputRoot, true);
        }
    }

    private static JDNextPCJdiFormat CreateFormat() =>
        new(new FakeMediaProcessor(), new FakeTextureService(), NullLogger<JDNextPCJdiFormat>.Instance);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "JustDanceEditor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static IntermediateSongPackage CreateJdiPackage(string mapName)
    {
        TimelineStructureDocument structure = new()
        {
            Markers = [0, 48000, 96000, 144000, 192000],
            StartBeat = 0,
            EndBeat = 4,
            VideoStartOffset = 0,
            PreviewEntryBeat = 0,
            PreviewLoopStartBeat = 0,
            PreviewLoopEndBeat = 4,
            PreviewDuration = 30
        };

        return new IntermediateSongPackage
        {
            Metadata = new IntermediateMetadata
            {
                MapName = mapName,
                ParentMapName = mapName,
                Title = mapName,
                Artist = "Test Artist",
                Credits = "Synthetic Credits",
                CoachCount = 3,
                Difficulty = 2,
                OriginalJDVersion = 2024,
                LyricsColor = "#FFFFFFFF"
            },
            TimelineStructure = structure,
            Lyrics = new Timeline<KaraokeClip>
            {
                Clips =
                [
                    new KaraokeClip { Id = 1, StartTime = 24, Duration = 12, Lyrics = "Hel", ContentType = 1 },
                    new KaraokeClip { Id = 2, StartTime = 36, Duration = 12, Lyrics = "lo", ContentType = 1, IsEndOfLine = true }
                ]
            },
            Pictograms = new Timeline<PictogramClip>
            {
                Clips =
                [
                    new PictogramClip { Id = 10, StartTime = 24, Duration = 24, PictogramId = "picto_a", CoachCount = -1 },
                    new PictogramClip { Id = 11, StartTime = 48, Duration = 24, PictogramId = "picto_b", CoachCount = -1 }
                ]
            },
            CoachTimelines =
            [
                new MoveTimeline
                {
                    CoachId = 0,
                    TrackId = 100,
                    Clips = [ new MoveClip { Id = 20, StartTime = 24, MoveId = "makeba_tui" } ]
                },
                new MoveTimeline
                {
                    CoachId = 1,
                    TrackId = 101,
                    Clips = [ new MoveClip { Id = 21, StartTime = 24, MoveId = "makeba_tui" } ]
                },
                new MoveTimeline
                {
                    CoachId = 2,
                    TrackId = 102,
                    Clips = [ new MoveClip { Id = 22, StartTime = 24, MoveId = "makeba_tui" } ]
                }
            ],
            HandCoachMoves = new Dictionary<string, CoachMoveDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["makeba_tui"] = new CoachMoveDefinition { Color = "#CCCCCC", Duration = 24, MoveType = CoachMoveType.HandTracking }
            }
        };
    }

    private static IntermediateSongPackage CreateMinimalPackage(string mapName)
    {
        return new IntermediateSongPackage
        {
            Metadata = new IntermediateMetadata
            {
                MapName = mapName,
                ParentMapName = mapName,
                Title = mapName,
                Artist = "Test Artist",
                CoachCount = 1,
                LyricsColor = "#FFFFFFFF"
            },
            TimelineStructure = new TimelineStructureDocument
            {
                Markers = [0, 48000],
                StartBeat = 0,
                EndBeat = 1,
                VideoStartOffset = 0,
                PreviewEntryBeat = 0,
                PreviewLoopStartBeat = 0,
                PreviewLoopEndBeat = 1,
                PreviewDuration = 30
            }
        };
    }

    private static void CreateJdiExportAssets(string packageRoot, int coachCount)
    {
        WriteBytes(Path.Combine(packageRoot, "assets", "audio", "master.opus"), "AUDIO"u8.ToArray());
        WriteBytes(Path.Combine(packageRoot, "assets", "video", "small.webm"), CreatePseudoWebmBytes(24, isVp8: true));
        WriteBytes(Path.Combine(packageRoot, "assets", "video", "large.webm"), CreatePseudoWebmBytes(64, isVp8: true));
        WriteBytes(Path.Combine(packageRoot, "assets", "coverAssets", "cover.webp"), "COVER"u8.ToArray());
        WriteBytes(Path.Combine(packageRoot, "assets", "coverAssets", "songTitleLogo.webp"), "TITLE"u8.ToArray());
        WriteBytes(Path.Combine(packageRoot, "assets", "backgrounds", "mapBackground.webp"), "BKG"u8.ToArray());
        WriteBytes(Path.Combine(packageRoot, "assets", "pictograms", "picto_a.webp"), "P1"u8.ToArray());
        WriteBytes(Path.Combine(packageRoot, "assets", "pictograms", "picto_b.webp"), "P2"u8.ToArray());
        WriteBytes(
            IntermediatePackageLayout.Resolve(packageRoot, $"{IntermediatePackageLayout.Assets.MovesV7Folder}/makeba_tui.msm"),
            CreateVersion7Classifier("makeba_tui"));

        for (int coachIndex = 1; coachIndex <= coachCount; coachIndex++)
            WriteBytes(Path.Combine(packageRoot, "assets", "coaches", $"coach_{coachIndex:D2}.webp"), [(byte)coachIndex]);
    }

    private static void CreateJdNextInput(string jdNextRoot)
    {
        WriteJson(Path.Combine(jdNextRoot, "songdesc.json"), """
        {
          "title": "Synthetic Song",
          "artist": "Synthetic Artist",
          "credits": "Synthetic Credits",
          "jdVersion": 2024,
          "numCoach": 2,
          "difficulty": 3,
          "lyricColor": [1.0, 1.0, 1.0, 1.0]
        }
        """);

        WriteJson(Path.Combine(jdNextRoot, "musictrack.json"), """
        {
          "videoStartTime": 0.0,
          "videoEndTime": 4.0,
          "startBeat": 0,
          "endBeat": 4,
          "beats": [0.0, 1.0, 2.0, 3.0, 4.0]
        }
        """);

        WriteJson(Path.Combine(jdNextRoot, "timeline.json"), """
        {
          "lyricColor": [1.0, 1.0, 1.0, 1.0],
          "lyrics": [
            { "time": 1.0, "duration": 0.5, "text": "Hi", "isLineEnding": 1 }
          ],
          "pictos": [
            { "time": 1.0, "name": "picto_a" },
            { "time": 2.0, "name": "picto_b" }
          ],
          "moves": [
            { "time": 1.0, "duration": 1.0, "name": "synthetic_move", "goldMove": 0, "coachID": 0 },
            { "time": 1.0, "duration": 1.0, "name": "synthetic_move", "goldMove": 1, "coachID": 1 }
          ]
        }
        """);

        WriteBytes(Path.Combine(jdNextRoot, "media", "small.webm"), CreatePseudoWebmBytes(16, isVp8: true));
        WriteBytes(Path.Combine(jdNextRoot, "media", "large.webm"), CreatePseudoWebmBytes(48, isVp8: true));
        WriteBytes(Path.Combine(jdNextRoot, "media", "song.ogg"), "OGGDATA"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "moves", "synthetic_move.msm"), CreateVersion7Classifier("synthetic_move"));
        WriteBytes(Path.Combine(jdNextRoot, "pictos", "picto_a.png"), "PA"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "pictos", "picto_b.png"), "PB"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "menuart", "cover.png"), "COVER"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "menuart", "title.png"), "TITLE"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "menuart", "bkg.png"), "BKG"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "menuart", "coach01.png"), "C1"u8.ToArray());
        WriteBytes(Path.Combine(jdNextRoot, "menuart", "coach02.png"), "C2"u8.ToArray());
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Could not determine the directory for '{path}'."));
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] CreateVersion7Classifier(string moveName)
    {
        const float duration = 1.0f;
        List<MotionSample> samples = [];
        for (int i = 0; i <= 60; i++)
        {
            float time = i / 60.0f;
            samples.Add(new MotionSample(time, MathF.Sin(time), MathF.Cos(time), time));
        }

        return new MotionClassifierGenerator().BuildClassifier(new MotionClassifierBuildRequest
        {
            SongName = "synthetic",
            MoveName = moveName,
            Examples = [new MotionExample(duration, samples)]
        });
    }

    private static void WriteJson(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Could not determine the directory for '{path}'."));
        File.WriteAllText(path, json);
    }

    private static void ReplaceTimelinePictogramId(string jdNextRoot, string oldId, string newId)
    {
        string timelinePath = Path.Combine(jdNextRoot, "timeline.json");
        string timelineJson = File.ReadAllText(timelinePath)
            .Replace(JsonSerializer.Serialize(oldId), JsonSerializer.Serialize(newId), StringComparison.Ordinal);
        File.WriteAllText(timelinePath, timelineJson);
    }

    private static byte[] CreatePseudoWebmBytes(int totalLength, bool isVp8)
    {
        byte[] bytes = [.. Enumerable.Repeat((byte)'A', totalLength)];
        byte[] marker = isVp8 ? "V_VP8"u8.ToArray() : "V_VP9"u8.ToArray();
        int offset = Math.Min(8, Math.Max(0, totalLength - marker.Length));
        Array.Copy(marker, 0, bytes, offset, Math.Min(marker.Length, totalLength - offset));
        return bytes;
    }

    private sealed class FakeMediaProcessor : IMediaProcessor
    {
        public Task EncodeAudioAsync(JdiAudioEncodeRequest request, string outputPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));
            File.Copy(request.SourcePath, outputPath, true);
            return Task.CompletedTask;
        }

        public async Task<MemoryStream> EncodeAudioToMemoryAsync(JdiAudioEncodeRequest request, CancellationToken cancellationToken = default)
        {
            MemoryStream output = new(await File.ReadAllBytesAsync(request.SourcePath, cancellationToken));
            output.Position = 0;
            return output;
        }

        public Task<string?> GetOrCreateVideoAsync(JdiVideoEncodeRequest request, ILogger logger, CancellationToken cancellationToken = default)
        {
            string outputPath = GetCachedVideoPath(request);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));
            File.Copy(ResolveVideoSource(request), outputPath, true);
            return Task.FromResult<string?>(outputPath);
        }
    }

    private sealed class FakeTextureService : ITextureService
    {
        public Image<Bgra32>? ConvertToImage(Stream stream)
        {
            try
            {
                return Image.Load<Bgra32>(stream);
            }
            catch
            {
                return null;
            }
        }

        public async Task ConvertTextureAsync(Stream inputStream, string outputPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));
            await using FileStream output = File.Create(outputPath);
            await inputStream.CopyToAsync(output, cancellationToken);
        }
    }

    private sealed class RecordingMediaProcessor : IMediaProcessor
    {
        public List<MediaCall> Calls { get; } = [];

        public Task EncodeAudioAsync(JdiAudioEncodeRequest request, string outputPath, CancellationToken cancellationToken = default)
        {
            Calls.Add(new MediaCall(request.SourcePath, outputPath, [], AudioRequest: request));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));
            File.Copy(request.SourcePath, outputPath, true);
            return Task.CompletedTask;
        }

        public async Task<MemoryStream> EncodeAudioToMemoryAsync(JdiAudioEncodeRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(new MediaCall(request.SourcePath, $"memory:{request.OutputFormat ?? request.Codec}", [], AudioRequest: request));
            MemoryStream output = new(await File.ReadAllBytesAsync(request.SourcePath, cancellationToken));
            output.Position = 0;
            return output;
        }

        public Task<string?> GetOrCreateVideoAsync(JdiVideoEncodeRequest request, ILogger logger, CancellationToken cancellationToken = default)
        {
            string outputPath = GetCachedVideoPath(request);
            string input = ResolveVideoSource(request);
            Calls.Add(new MediaCall(input, outputPath, [], VideoRequest: request));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{outputPath}'."));
            File.Copy(input, outputPath, true);
            return Task.FromResult<string?>(outputPath);
        }
    }

    private sealed record MediaCall(
        string Input,
        string Output,
        string[] ExtraArgs,
        JdiAudioEncodeRequest? AudioRequest = null,
        JdiVideoEncodeRequest? VideoRequest = null);

    private static string GetCachedVideoPath(JdiVideoEncodeRequest request) =>
        Path.Combine(request.PackageRoot, "scratch", "video", request.CacheFileName);

    private static string ResolveVideoSource(JdiVideoEncodeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath))
            return request.SourcePath;

        string videoRoot = Path.Combine(request.PackageRoot, "assets", "video");
        return Directory.EnumerateFiles(videoRoot)
            .OrderByDescending(path => new FileInfo(path).Length)
            .First();
    }
}