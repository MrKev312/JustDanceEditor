using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupSegmentBuilder
{
    // The active fade window starts after the delay and lasts for the remaining duration.
    private const double MashupVideoFadeBrickDurationBeats = 1.0;
    private const double MashupVideoFadeOffsetBeats = 0.75;
    private const double MashupVideoFadeActiveBeats = MashupVideoFadeBrickDurationBeats - MashupVideoFadeOffsetBeats;

    internal static bool ShouldRenderSourceSegment(LegacyMashupBlock block) =>
        block.DurationBeats > 0 && block.UsesAlternativeBlock && !block.SourceBlock.IsEmptyBlock;

    internal static bool ShouldRunTransition(LegacyMashupBlock block) =>
        ShouldRenderSourceSegment(block) && block.UsesAlternativeBlock;

    internal static bool ShouldApplyVideoFade(LegacyMashupBlockDescriptor? previous, LegacyMashupBlock block) =>
        ShouldRunTransition(block) && !MashupTiming.IsConsecutiveBlock(previous, block.SourceBlock);

    internal static IReadOnlyList<LegacyMashupBlock> BuildTransitionBlocks(LegacyMashupData mashup)
    {
        ArgumentNullException.ThrowIfNull(mashup);

        List<LegacyMashupBlock> transitionBlocks = [];
        LegacyMashupBlockDescriptor? previousBlock = null;
        foreach (LegacyMashupBlock block in mashup.Blocks)
        {
            if (ShouldApplyVideoFade(previousBlock, block))
                transitionBlocks.Add(block);

            previousBlock = block.SourceBlock;
        }

        return transitionBlocks;
    }

    internal static List<MaterializedMashupSegment> MaterializeSegments(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        ILogger logger)
    {
        List<MaterializedMashupSegment> segments = [];
        foreach (LegacyMashupBlock block in mashup.Blocks)
        {
            if (!ShouldRenderSourceSegment(block))
                continue;

            if (!TryMaterializeSegment(fileSystem, mashup, block, timelineStructure, logger, out MaterializedMashupSegment? segment))
            {
                throw new FileNotFoundException(
                    $"Mashup block {block.Index} references '{block.SourceBlock.SongName}', but no source coach video was found.");
            }

            ArgumentNullException.ThrowIfNull(segment);
            if (segment.OutputDurationSeconds > 0.001)
                segments.Add(segment);
        }

        if (segments.Count == 0)
            throw new InvalidOperationException($"Mashup '{mashup.MapName}' has no renderable video blocks.");

        return ApplyVideoFadePolicy(mashup, timelineStructure, segments);
    }

    internal static List<MaterializedMashupSegment> ApplyVideoFadePolicy(
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        IReadOnlyList<MaterializedMashupSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(mashup);
        ArgumentNullException.ThrowIfNull(timelineStructure);
        ArgumentNullException.ThrowIfNull(segments);

        Dictionary<int, bool> fadeInByBlockIndex = [];
        LegacyMashupBlockDescriptor? previousBlock = null;
        foreach (LegacyMashupBlock block in mashup.Blocks)
        {
            if (ShouldRenderSourceSegment(block))
                fadeInByBlockIndex[block.Index] = ShouldApplyVideoFade(previousBlock, block);

            previousBlock = block.SourceBlock;
        }

        List<MaterializedMashupSegment> updated = new(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            MaterializedMashupSegment segment = segments[i];
            double fadeInDurationSeconds = 0.0;
            double fadeOutDurationSeconds = 0.0;

            if (fadeInByBlockIndex.TryGetValue(segment.BlockIndex, out bool fadeIn) && fadeIn)
            {
                double fadeInEndSeconds = MashupTiming.GetOutputSeconds(
                    timelineStructure,
                    segment.AbsoluteStartBeat + MashupVideoFadeActiveBeats);
                fadeInDurationSeconds = ClampDuration(
                    fadeInEndSeconds - segment.OutputStartSeconds,
                    segment.OutputDurationSeconds);
            }

            double fadeInDelaySeconds = 0.0;

            if (i + 1 < segments.Count)
            {
                MaterializedMashupSegment nextSegment = segments[i + 1];
                if (fadeInByBlockIndex.TryGetValue(nextSegment.BlockIndex, out bool nextFadeIn) && nextFadeIn)
                {
                    double segmentEndSeconds = segment.OutputStartSeconds + segment.OutputDurationSeconds;
                    double fadeOutStartSeconds = MashupTiming.GetOutputSeconds(
                        timelineStructure,
                        nextSegment.AbsoluteStartBeat - MashupVideoFadeActiveBeats);
                    fadeOutDurationSeconds = ClampDuration(
                        segmentEndSeconds - fadeOutStartSeconds,
                        segment.OutputDurationSeconds);
                }
            }

            updated.Add(segment with
            {
                FadeInDurationSeconds = fadeInDurationSeconds,
                FadeInDelaySeconds = fadeInDelaySeconds,
                FadeOutDurationSeconds = fadeOutDurationSeconds
            });
        }

        return updated;
    }

    internal static float ComputeVideoFadeAlpha(
        double localSeconds,
        double outputDurationSeconds,
        double fadeInDurationSeconds,
        double fadeOutDurationSeconds,
        double fadeInDelaySeconds = 0.0)
    {
        if (outputDurationSeconds <= 0.0)
            return 0.0f;

        double alpha = 1.0;
        if (fadeInDelaySeconds > 0.0 && localSeconds < fadeInDelaySeconds)
            alpha = 0.0;
        else if (fadeInDurationSeconds > 0.0)
        {
            double fadeLocalSeconds = localSeconds - Math.Max(0.0, fadeInDelaySeconds);
            if (fadeLocalSeconds < fadeInDurationSeconds)
                alpha = Math.Min(alpha, Math.Max(0.0, fadeLocalSeconds / fadeInDurationSeconds));
        }

        if (fadeOutDurationSeconds > 0.0)
        {
            double remainingSeconds = outputDurationSeconds - localSeconds;
            if (remainingSeconds <= fadeOutDurationSeconds)
                alpha = Math.Min(alpha, Math.Max(0.0, remainingSeconds / fadeOutDurationSeconds));
        }

        return (float)Math.Clamp(alpha, 0.0, 1.0);
    }

    internal static bool IsLikelyStackedAlphaVideo(int width, int height)
    {
        if (width <= 0 || height <= 0 || height % 3 != 0)
            return false;

        int visibleHeight = height * 2 / 3;
        if (visibleHeight <= 0)
            return false;

        double visibleAspect = width / (double)visibleHeight;
        return visibleAspect <= 2.05;
    }

    private static bool TryMaterializeSegment(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        LegacyMashupBlock block,
        TimelineStructureDocument timelineStructure,
        ILogger logger,
        out MaterializedMashupSegment? segment)
    {
        segment = null;
        if (!MashupSourceVideoResolver.TryFindSourceVideo(fileSystem, mashup, block, out MashupSourceVideo sourceVideo))
            return false;

        double outputStart = MashupTiming.GetOutputSeconds(timelineStructure, block.AbsoluteStartBeat);
        double outputEnd = MashupTiming.GetOutputSeconds(timelineStructure, block.AbsoluteStartBeat + block.DurationBeats);
        double outputDuration = Math.Max(0.0, outputEnd - outputStart);
        if (outputDuration <= 0.001)
            return false;

        JdiVideoInfo? videoInfo = JdiVideoConverter.TryInspectVideoAsync(
            () => fileSystem.GetFileStream(sourceVideo.File)).GetAwaiter().GetResult();
        if (videoInfo == null || videoInfo.Width <= 0 || videoInfo.Height <= 0)
            throw new InvalidDataException($"Could not inspect mashup source video '{sourceVideo.File.RelativePath}'.");

        int sourceWidth = videoInfo.Width;
        int visibleHeight = videoInfo.Height;
        int alphaHeight = 0;
        bool hasStackedAlpha = IsLikelyStackedAlphaVideo(videoInfo.Width, videoInfo.Height);
        if (hasStackedAlpha)
        {
            visibleHeight = videoInfo.Height * 2 / 3;
            alphaHeight = videoInfo.Height - visibleHeight;
        }

        double sourceStart = 0.0;
        double sourceDuration = videoInfo.Duration.TotalSeconds;
        if (sourceVideo.IsFullMapVideo)
        {
            if (MashupSourceVideoResolver.TryLoadSourceTimeline(fileSystem, sourceVideo, out TimelineStructureDocument? sourceTimeline))
            {
                sourceStart = Math.Max(0.0, MashupTiming.GetVideoSecondsForBeat(sourceTimeline!, block.SourceBlock.FirstBeat));
                double sourceEnd = Math.Max(sourceStart, MashupTiming.GetVideoSecondsForBeat(sourceTimeline!, block.SourceBlock.LastBeat));
                sourceDuration = sourceEnd - sourceStart;
            }
            else
            {
                logger.LogWarning(
                    "Could not load musictrack for full-map mashup source '{SourceSongName}'; using source video from the beginning.",
                    block.SourceBlock.SongName);
            }
        }

        if (sourceDuration <= 0.001)
            sourceDuration = Math.Max(0.001, videoInfo.Duration.TotalSeconds - sourceStart);

        double cappedSourceDuration = Math.Min(sourceDuration, Math.Max(0.001, videoInfo.Duration.TotalSeconds - sourceStart));

        segment = new MaterializedMashupSegment(
            sourceVideo.File.RelativePath,
            block.Index,
            block.AbsoluteStartBeat,
            block.DurationBeats,
            block.SourceBlock.FirstBeat,
            block.SourceBlock.LastBeat,
            block.SourceBlock.SongName,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            hasStackedAlpha,
            sourceStart,
            cappedSourceDuration,
            outputStart,
            outputDuration,
            block.SourceBlock.VideoCoachOffsetX,
            block.SourceBlock.VideoCoachOffsetY,
            block.SourceBlock.VideoCoachScale == 0 ? 1.0f : block.SourceBlock.VideoCoachScale,
            SourceStreamFactory: () => fileSystem.GetFileStream(sourceVideo.File));
        return true;
    }

    private static double ClampDuration(double value, double maxDuration) =>
        Math.Clamp(value, 0.0, Math.Max(0.0, maxDuration));
}
