using Avalonia;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Utilities;

using SixLabors.ImageSharp.Processing;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelinePictogramActions(
    TimelineEditorViewModel timeline,
    ITimelinePictogramGenerator? pictogramGenerator)
{
    private ITimelinePictogramGenerator? _pictogramGenerator = pictogramGenerator;

    public async Task GeneratePictogramsAsync(
        PictogramGenerationMode mode,
        PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars,
        PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center,
        CancellationToken cancellationToken = default)
    {
        ITimelinePictogramGenerator generator = _pictogramGenerator ??= new TimelinePictogramGenerator();

        GeneratedPictogramBatch batch;
        try
        {
            batch = await generator.GenerateAsync(timeline, mode, frameLayoutMode, horizontalFocus, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Generate timeline pictograms");
            return;
        }

        if (batch.PictogramTrack == null || batch.Clips.Count == 0)
            return;

        TrackViewModel targetTrack = batch.PictogramTrack;
        IReadOnlyList<PictogramClipViewModel> generatedClips = batch.Clips;

        timeline.PushUndo(
            undo: () =>
            {
                foreach (PictogramClipViewModel clip in generatedClips)
                {
                    if (targetTrack.Clips.Contains(clip))
                        targetTrack.Clips.Remove(clip);
                }
            },
            redo: () =>
            {
                foreach (PictogramClipViewModel clip in generatedClips)
                {
                    if (!targetTrack.Clips.Contains(clip))
                        targetTrack.Clips.Add(clip);
                }
            });

        foreach (PictogramClipViewModel clip in generatedClips)
        {
            if (!targetTrack.Clips.Contains(clip))
                targetTrack.Clips.Add(clip);
        }

        SkiaPictogramImageCache.Preload(generatedClips.Select(clip => clip.ImagePath));
        timeline.NotifyPictogramAssetsChanged();
    }

    public async Task GenerateNewPictogramAsync(
        double playheadBeat,
        PictogramScreenshotOptionsResult options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        TrackViewModel? pictogramTrack = timeline.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Pictogram);
        if (pictogramTrack == null)
            return;

        if (string.IsNullOrWhiteSpace(timeline.VideoPath) || !File.Exists(timeline.VideoPath))
            return;

        string pictogramDirectory = Path.Combine(timeline.RootPath, "assets", "pictograms");
        Directory.CreateDirectory(pictogramDirectory);

        string baseId = BuildNewPictogramBaseId(options);
        string pictogramId = EnsureUniquePictogramId(baseId, new HashSet<string>(timeline.AvailablePictograms, StringComparer.OrdinalIgnoreCase));

        string outputPath = Path.Combine(pictogramDirectory, pictogramId + ".webp");
        PixelSize targetSize = TimelinePictogramGenerator.GetTargetSize(timeline.CoachCount);
        double videoTimestamp = GetVideoTimestampSecondsFromBeat(playheadBeat);

        PictogramImageGenerator imageGenerator = new();
        await imageGenerator.GenerateVideoFramePictogramAsync(
            timeline.VideoPath,
            videoTimestamp,
            outputPath,
            targetSize,
            options.FrameLayoutMode,
            options.HorizontalFocus,
            cancellationToken);

        ImageBitmapCache.Invalidate(outputPath);

        IEnumerable<MoveClipViewModel> moveClips = timeline.Tracks
            .Where(t => t.TrackType is TrackType.CoachHand or TrackType.CoachFullBody)
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>();

        List<double> insertionBeats = BuildPictogramInsertionBeats(moveClips, playheadBeat, options);

        List<PictogramClipViewModel> created = [];
        foreach (double beat in insertionBeats)
        {
            double clampedBeat = ClampBeatForDuration(beat, 24);
            PictogramClip raw = new()
            {
                PictogramId = pictogramId,
                StartTime = (int)Math.Round(clampedBeat * 24d),
                Duration = 24
            };

            created.Add(new PictogramClipViewModel(raw, timeline.RootPath, timeline));
        }

        if (created.Count == 0)
            return;

        timeline.PushUndo(
            undo: () =>
            {
                foreach (PictogramClipViewModel clip in created)
                {
                    if (pictogramTrack.Clips.Contains(clip))
                        pictogramTrack.Clips.Remove(clip);
                }
            },
            redo: () =>
            {
                foreach (PictogramClipViewModel clip in created)
                {
                    if (!pictogramTrack.Clips.Contains(clip))
                        pictogramTrack.Clips.Add(clip);
                }
            });

        foreach (PictogramClipViewModel clip in created)
        {
            if (!pictogramTrack.Clips.Contains(clip))
                pictogramTrack.Clips.Add(clip);
        }

        SkiaPictogramImageCache.Preload(created.Select(clip => clip.ImagePath));
        timeline.NotifyPictogramAssetsChanged();
    }

    public static List<double> BuildPictogramInsertionBeats(
        IEnumerable<MoveClipViewModel> moveClips,
        double playheadBeat,
        PictogramScreenshotOptionsResult options)
    {
        ArgumentNullException.ThrowIfNull(moveClips);
        ArgumentNullException.ThrowIfNull(options);

        if (options.InsertionMode != PictogramInsertionMode.AllInstancesOfSelectedMove
            || string.IsNullOrWhiteSpace(options.ReferenceMoveId)
            || !options.ReferenceMoveStartFrame.HasValue)
        {
            return [playheadBeat];
        }

        double offset = playheadBeat - (options.ReferenceMoveStartFrame.Value / 24d);

        List<double> beats = [.. moveClips
            .Where(c => string.Equals(c.MoveId, options.ReferenceMoveId, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.RawClip.StartTime)
            .Distinct()
            .OrderBy(startFrame => startFrame)
            .Select(startFrame => (startFrame / 24d) + offset)];

        return beats.Count > 0 ? beats : [playheadBeat];
    }

    public static string BuildNewPictogramBaseId(PictogramScreenshotOptionsResult options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string seed = string.IsNullOrWhiteSpace(options.ReferenceMoveId)
            ? "screenshot"
            : options.ReferenceMoveId;

        return $"auto_{TimelinePictogramGenerator.SanitizeId(seed)}";
    }

    public bool RenamePictogramId(string oldId, string newId)
    {
        oldId = oldId?.Trim() ?? string.Empty;
        newId = newId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
            return false;

        if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            return false;

        HashSet<string> usedIds = new(timeline.AvailablePictograms, StringComparer.OrdinalIgnoreCase);
        if (usedIds.Contains(newId))
            return false;

        string oldPath = ResolvePictogramPath(oldId);
        if (!File.Exists(oldPath))
            return false;

        string extension = Path.GetExtension(oldPath);
        string directory = Path.GetDirectoryName(oldPath) ?? Path.Combine(timeline.RootPath, "assets", "pictograms");
        string newPath = Path.Combine(directory, newId + extension);
        if (File.Exists(newPath))
            return false;

        List<PictogramClipViewModel> affected = [.. timeline.Tracks
            .SelectMany(t => t.Clips)
            .OfType<PictogramClipViewModel>()
            .Where(c => string.Equals(c.PictogramId, oldId, StringComparison.OrdinalIgnoreCase))];

        void ApplyRename(string fromId, string toId, string fromPath, string toPath)
        {
            if (File.Exists(fromPath) && !File.Exists(toPath))
                File.Move(fromPath, toPath);

            foreach (PictogramClipViewModel clip in affected)
                clip.PictogramId = toId;

            ImageBitmapCache.Invalidate(fromPath);
            ImageBitmapCache.Invalidate(toPath);

            timeline.NotifyPictogramAssetsChanged();
            SkiaPictogramImageCache.Preload(affected.Select(c => c.ImagePath));
        }

        ApplyRename(oldId, newId, oldPath, newPath);

        timeline.PushUndo(
            undo: () => ApplyRename(newId, oldId, newPath, oldPath),
            redo: () => ApplyRename(oldId, newId, oldPath, newPath));

        return true;
    }

    public async Task<bool> FlipPictogramAsync(
        string pictogramId,
        PictogramClipViewModel? specificClip = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pictogramId))
            return false;

        string pictogramDirectory = Path.Combine(timeline.RootPath, "assets", "pictograms");
        Directory.CreateDirectory(pictogramDirectory);

        string? currentPath = GetExistingPictogramFilePath(pictogramId);
        if (currentPath == null || !File.Exists(currentPath))
            return false;

        bool isFlipped = pictogramId.EndsWith("_flipped", StringComparison.OrdinalIgnoreCase);
        string counterpartId = isFlipped ? pictogramId[..^"_flipped".Length] : pictogramId + "_flipped";
        string? counterpartPath = GetExistingPictogramFilePath(counterpartId);

        List<PictogramClipViewModel> affectedClips = specificClip != null
            ? [specificClip]
            : [.. timeline.Tracks.SelectMany(t => t.Clips).OfType<PictogramClipViewModel>().Where(c => string.Equals(c.PictogramId, pictogramId, StringComparison.OrdinalIgnoreCase))];

        if (!string.IsNullOrWhiteSpace(counterpartPath) && File.Exists(counterpartPath))
        {
            void ApplyRelink(string fromId, string toId)
            {
                foreach (PictogramClipViewModel c in affectedClips)
                    c.PictogramId = toId;

                ImageBitmapCache.Invalidate(currentPath);
                ImageBitmapCache.Invalidate(counterpartPath);
                timeline.NotifyPictogramAssetsChanged();
                SkiaPictogramImageCache.Preload(affectedClips.Select(c => c.ImagePath));
            }

            ApplyRelink(pictogramId, Path.GetFileNameWithoutExtension(counterpartPath));

            timeline.PushUndo(
                undo: () => ApplyRelink(Path.GetFileNameWithoutExtension(counterpartPath), pictogramId),
                redo: () => ApplyRelink(pictogramId, Path.GetFileNameWithoutExtension(counterpartPath)));

            return true;
        }

        string destPath = Path.Combine(pictogramDirectory, counterpartId + ".webp");
        if (File.Exists(destPath))
            counterpartPath = destPath;

        bool created = false;
        try
        {
            if (!File.Exists(destPath))
            {
                try
                {
                    using SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Bgra32> image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Bgra32>(currentPath);
                    image.Mutate(x => x.Flip(FlipMode.Horizontal));

                    await using FileStream outFs = File.Create(destPath);
                    image.Save(outFs, WebpSettings.LosslessWebpEncoder);

                    created = File.Exists(destPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SixLabors.ImageSharp.UnknownImageFormatException)
                {
                    EditorLog.Unexpected(ex, $"Create flipped pictogram '{destPath}'");
                    created = false;
                }
            }

            if (!File.Exists(destPath))
                return false;

            counterpartPath = destPath;
            string toId = Path.GetFileNameWithoutExtension(counterpartPath);

            void ApplyRelinkCreated(string fromId, string toIdLocal)
            {
                foreach (PictogramClipViewModel c in affectedClips)
                    c.PictogramId = toIdLocal;

                ImageBitmapCache.Invalidate(currentPath);
                ImageBitmapCache.Invalidate(counterpartPath);
                timeline.NotifyPictogramAssetsChanged();
                SkiaPictogramImageCache.Preload(affectedClips.Select(c => c.ImagePath));
            }

            ApplyRelinkCreated(pictogramId, toId);

            timeline.PushUndo(
                undo: () =>
                {
                    foreach (PictogramClipViewModel c in affectedClips)
                        c.PictogramId = pictogramId;

                    try
                    {
                        if (File.Exists(destPath))
                            File.Delete(destPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        EditorLog.Unexpected(ex, $"Delete flipped pictogram '{destPath}' during undo");
                    }

                    ImageBitmapCache.Invalidate(destPath);
                    ImageBitmapCache.Invalidate(currentPath);
                    timeline.NotifyPictogramAssetsChanged();
                    SkiaPictogramImageCache.Preload(affectedClips.Select(c => c.ImagePath));
                },
                redo: () => ApplyRelinkCreated(pictogramId, toId));

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            EditorLog.Unexpected(ex, $"Flip pictogram '{pictogramId}'");
            if (created && File.Exists(destPath))
            {
                try
                {
                    File.Delete(destPath);
                }
                catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                {
                    EditorLog.Unexpected(cleanupException, $"Clean up flipped pictogram '{destPath}'");
                }
            }

            return false;
        }
    }

    public async Task RegeneratePictogramAsync(
        PictogramClipViewModel clip,
        PictogramScreenshotOptionsResult options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(timeline.VideoPath) || !File.Exists(timeline.VideoPath))
            return;

        string pictogramId = clip.PictogramId;
        if (string.IsNullOrWhiteSpace(pictogramId))
            return;

        string outputPath = ResolvePictogramPath(pictogramId);
        PixelSize targetSize = TimelinePictogramGenerator.GetTargetSize(timeline.CoachCount);
        double videoTimestamp = GetVideoTimestampSecondsFromBeat(clip.StartBeat);

        PictogramImageGenerator imageGenerator = new();
        await imageGenerator.GenerateVideoFramePictogramAsync(
            timeline.VideoPath,
            videoTimestamp,
            outputPath,
            targetSize,
            options.FrameLayoutMode,
            options.HorizontalFocus,
            cancellationToken);

        ImageBitmapCache.Invalidate(outputPath);
        if (!string.IsNullOrWhiteSpace(clip.ImagePath))
            ImageBitmapCache.Invalidate(clip.ImagePath);

        SkiaPictogramImageCache.Preload([clip.ImagePath]);
        timeline.NotifyPictogramAssetsChanged();
    }

    private string? GetExistingPictogramFilePath(string pictogramId)
    {
        if (string.IsNullOrWhiteSpace(pictogramId))
            return null;

        string path = Path.Combine(timeline.RootPath, "assets", "pictograms", pictogramId + ".webp");
        return File.Exists(path) ? path : null;
    }

    private double GetVideoTimestampSecondsFromBeat(double beatLabel)
    {
        double playbackSeconds = timeline.GetPlaybackSecondsAtBeatLabel(beatLabel);
        double songStartOffset = timeline.TimelineStructure.GetSongStartOffset();
        return Math.Max(0, playbackSeconds + songStartOffset + timeline.VideoOffset);
    }

    private double ClampBeatForDuration(double beat, int durationFrames)
    {
        double minBeat = timeline.TimelineStructure?.StartBeat ?? 0;
        double maxBeat = timeline.TimelineStructure?.EndBeat ?? double.MaxValue;
        return Math.Max(minBeat, Math.Min(beat, maxBeat - (durationFrames / 24d)));
    }

    private string ResolvePictogramPath(string pictogramId)
    {
        return Path.Combine(timeline.RootPath, "assets", "pictograms", pictogramId + ".webp");
    }

    private static string EnsureUniquePictogramId(string baseId, ISet<string> usedIds)
    {
        if (!usedIds.Contains(baseId))
            return baseId;

        int suffix = 2;
        while (true)
        {
            string candidate = $"{baseId}_{suffix}";
            if (!usedIds.Contains(candidate))
                return candidate;

            suffix++;
        }
    }
}
