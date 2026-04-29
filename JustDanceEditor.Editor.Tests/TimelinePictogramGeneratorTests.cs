using Avalonia;
using Avalonia.Media;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Reflection;

namespace JustDanceEditor.Editor.Tests;

public class TimelinePictogramGeneratorTests
{
    [Fact]
    public void GetTargetSize_UsesExpectedAspectRatioPerCoachCount()
    {
        PixelSize single = TimelinePictogramGenerator.GetTargetSize(1);
        PixelSize multi = TimelinePictogramGenerator.GetTargetSize(2);

        Assert.Equal(new PixelSize(512, 512), single);
        Assert.Equal(new PixelSize(512, 354), multi);
    }

    [Fact]
    public void CollectMoveMoments_GroupsByStartAndSkipsEmptyMoveIds()
    {
        MoveClipViewModel shortMove = new(new MoveClip { MoveId = "wave", StartTime = 24 }, null, null, isFullBody: false, fallbackDurationFrames: 24);
        MoveClipViewModel longMoveSameStart = new(new MoveClip { MoveId = "jump", StartTime = 24 }, null, null, isFullBody: false, fallbackDurationFrames: 48);
        MoveClipViewModel emptyId = new(new MoveClip { MoveId = "", StartTime = 96 }, null, null, isFullBody: false, fallbackDurationFrames: 24);

        IReadOnlyList<MoveMoment> moments = TimelinePictogramGenerator.CollectMoveMoments([shortMove, longMoveSameStart, emptyId]);

        MoveMoment moment = Assert.Single(moments);
        Assert.Equal(24, moment.StartFrame);
        Assert.Equal(48, moment.DurationFrames);
        Assert.Equal("jump", moment.MoveId);
    }

    [Fact]
    public async Task GenerateAsync_FromMoveNames_ReusesSingleGeneratedImagePerMove()
    {
        string root = CreateTempRoot();
        try
        {
            TimelineEditorViewModel timeline = CreateTimeline(root, coachCount: 1, [
                new MoveClip { MoveId = "wave", StartTime = 48 },
                new MoveClip { MoveId = "wave", StartTime = 96 },
                new MoveClip { MoveId = "jump", StartTime = 144 }
            ]);

            RecordingImageGenerator recorder = new();
            TimelinePictogramGenerator sut = new(recorder);

            GeneratedPictogramBatch batch = await sut.GenerateAsync(timeline, PictogramGenerationMode.MoveName);

            Assert.NotNull(batch.PictogramTrack);
            Assert.Equal(TrackType.Pictogram, batch.PictogramTrack?.TrackType);

            Assert.Equal(3, batch.Clips.Count);
            Assert.Equal(2.0, batch.Clips[0].StartBeat, 3);
            Assert.Equal(4.0, batch.Clips[1].StartBeat, 3);
            Assert.Equal(6.0, batch.Clips[2].StartBeat, 3);

            Assert.Equal(batch.Clips[0].PictogramId, batch.Clips[1].PictogramId);
            Assert.NotEqual(batch.Clips[0].PictogramId, batch.Clips[2].PictogramId);
            Assert.All(batch.Clips, c => Assert.Equal(1.0, c.DurationBeats, 3));

            Assert.Equal(2, recorder.MoveNameRequests.Count);
            Assert.All(recorder.MoveNameRequests, request => Assert.Equal(new PixelSize(512, 512), request.TargetSize));
            Assert.Contains(recorder.MoveNameRequests, request => request.MoveName == "wave");
            Assert.Contains(recorder.MoveNameRequests, request => request.MoveName == "jump");
            Assert.All(recorder.MoveNameRequests, request => Assert.True(File.Exists(request.OutputPath)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_FromVideoFrames_UsesVideoTimingAndMultiCoachRatio()
    {
        string root = CreateTempRoot();
        try
        {
            TimelineEditorViewModel timeline = CreateTimeline(root, coachCount: 3, [
                new MoveClip { MoveId = "spin", StartTime = 72 }
            ]);

            string fakeVideoPath = Path.Combine(root, "assets", "videos", "video.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeVideoPath) ?? root);
            File.WriteAllBytes(fakeVideoPath, [1, 2, 3]);
            SetAutoProperty(timeline, "VideoPath", fakeVideoPath);

            RecordingImageGenerator recorder = new();
            TimelinePictogramGenerator sut = new(recorder);

            GeneratedPictogramBatch batch = await sut.GenerateAsync(timeline, PictogramGenerationMode.VideoFrame, PictogramFrameLayoutMode.CropToFill);

            PictogramClipViewModel clip = Assert.Single(batch.Clips);
            Assert.Equal(3.0, clip.StartBeat, 3);

            VideoFrameRequest request = Assert.Single(recorder.VideoFrameRequests);
            Assert.Equal(fakeVideoPath, request.VideoPath);
            Assert.Equal(new PixelSize(512, 354), request.TargetSize);
            Assert.True(request.TimestampSeconds > clip.StartBeat, "Screenshot timestamp should be sampled after clip start (1/3 into clip).");
            Assert.True(request.TimestampSeconds < clip.StartBeat + 2.1, "Screenshot timestamp should remain near the first part of the clip.");
            Assert.Equal(PictogramFrameLayoutMode.CropToFill, request.FrameLayoutMode);
            Assert.True(File.Exists(request.OutputPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GenerateAsync_FromVideoFrames_ReturnsClips_WhenGeneratorThrowsAfterWritingFiles()
    {
        string root = CreateTempRoot();
        try
        {
            TimelineEditorViewModel timeline = CreateTimeline(root, coachCount: 1, [
                new MoveClip { MoveId = "spin", StartTime = 72 },
                new MoveClip { MoveId = "jump", StartTime = 120 }
            ]);

            string fakeVideoPath = Path.Combine(root, "assets", "videos", "video.mp4");
            Directory.CreateDirectory(Path.GetDirectoryName(fakeVideoPath) ?? root);
            File.WriteAllBytes(fakeVideoPath, [1, 2, 3]);
            SetAutoProperty(timeline, "VideoPath", fakeVideoPath);

            ThrowingAfterWriteImageGenerator recorder = new();
            TimelinePictogramGenerator sut = new(recorder);

            GeneratedPictogramBatch batch = await sut.GenerateAsync(timeline, PictogramGenerationMode.VideoFrame);

            Assert.Equal(2, batch.Clips.Count);
            Assert.Equal(2, recorder.VideoFrameRequests.Count);
            Assert.All(recorder.VideoFrameRequests, request => Assert.True(File.Exists(request.OutputPath)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static TimelineEditorViewModel CreateTimeline(string root, int coachCount, IEnumerable<MoveClip> clips)
    {
        IntermediateSongPackage package = new();
        package.Metadata.MapName = "Test Song";
        package.Metadata.CoachCount = coachCount;
        package.TimelineStructure.StartBeat = 0;
        package.TimelineStructure.EndBeat = 512;

        MoveTimeline handTimeline = new() { CoachId = 0, TrackId = 1 };
        package.CoachTimelines.Add(handTimeline);

        foreach (MoveClip clip in clips)
        {
            handTimeline.Clips.Add(clip);

            if (!string.IsNullOrWhiteSpace(clip.MoveId) && !package.HandCoachMoves.ContainsKey(clip.MoveId))
            {
                package.HandCoachMoves[clip.MoveId] = new CoachMoveDefinition
                {
                    Color = "#3366CC",
                    Duration = 24,
                    MoveType = CoachMoveType.HandTracking
                };
            }
        }

        return new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "jdi_picto_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets", "pictograms"));
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup in tests
        }
    }

    private static void SetAutoProperty<T>(object target, string propertyName, T value)
    {
        string backingFieldName = $"<{propertyName}>k__BackingField";
        FieldInfo field = target.GetType().GetField(backingFieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().FullName, backingFieldName);

        field.SetValue(target, value);
    }

    private sealed record MoveNameRequest(string OutputPath, string MoveName, Color BackgroundColor, PixelSize TargetSize);

    private sealed record VideoFrameRequest(string VideoPath, double TimestampSeconds, string OutputPath, PixelSize TargetSize, PictogramFrameLayoutMode FrameLayoutMode, PictogramHorizontalFocus HorizontalFocus);

    private sealed class RecordingImageGenerator : IPictogramImageGenerator
    {
        public List<MoveNameRequest> MoveNameRequests { get; } = [];
        public List<VideoFrameRequest> VideoFrameRequests { get; } = [];

        public Task GenerateMoveNamePictogramAsync(string outputPath, string moveName, Color backgroundColor, PixelSize targetSize, CancellationToken cancellationToken = default)
        {
            MoveNameRequests.Add(new MoveNameRequest(outputPath, moveName, backgroundColor, targetSize));
            WritePlaceholder(outputPath);
            return Task.CompletedTask;
        }

        public Task GenerateVideoFramePictogramAsync(string videoPath, double timestampSeconds, string outputPath, PixelSize targetSize, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default)
        {
            VideoFrameRequests.Add(new VideoFrameRequest(videoPath, timestampSeconds, outputPath, targetSize, frameLayoutMode, horizontalFocus));
            WritePlaceholder(outputPath);
            return Task.CompletedTask;
        }

        private static void WritePlaceholder(string outputPath)
        {
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(outputPath, "placeholder");
        }
    }

    private sealed class ThrowingAfterWriteImageGenerator : IPictogramImageGenerator
    {
        public List<VideoFrameRequest> VideoFrameRequests { get; } = [];

        public Task GenerateMoveNamePictogramAsync(string outputPath, string moveName, Color backgroundColor, PixelSize targetSize, CancellationToken cancellationToken = default)
        {
            WritePlaceholder(outputPath);
            return Task.CompletedTask;
        }

        public Task GenerateVideoFramePictogramAsync(string videoPath, double timestampSeconds, string outputPath, PixelSize targetSize, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default)
        {
            VideoFrameRequests.Add(new VideoFrameRequest(videoPath, timestampSeconds, outputPath, targetSize, frameLayoutMode, horizontalFocus));
            WritePlaceholder(outputPath);
            throw new InvalidOperationException("Simulated encoder failure after writing output.");
        }

        private static void WritePlaceholder(string outputPath)
        {
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(outputPath, "placeholder");
        }
    }
}
