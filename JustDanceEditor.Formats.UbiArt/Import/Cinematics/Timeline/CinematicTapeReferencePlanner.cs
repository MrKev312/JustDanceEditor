using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class CinematicTapeReferencePlanner
{
    internal static IEnumerable<TapeVisit> GetReferenceVisits(
        Dictionary<string, int> tapeDurations,
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger,
        TapeClip clip,
        double videoDurationSeconds,
        bool parentVisitHasDuration)
    {
        if (clip.Path == null)
            yield break;

        int tapeDuration = CinematicTapeClipReader.GetDurationFrames(fileSystem, tapeDurations, clip.Path, logger);
        bool loopsUntilTimelineEnd = clip.LoopingType is
            CinematicTapeReferenceLoopingType.Infinite or
            CinematicTapeReferenceLoopingType.PingPongInfinite;
        int timelineDuration = Math.Max(1, (int)Math.Ceiling(videoDurationSeconds * CinematicConstants.TapeTicksPerSecond));
        int referenceDuration = loopsUntilTimelineEnd
            ? parentVisitHasDuration && clip.DurationFrames > 0
                ? clip.DurationFrames
                : Math.Max(0, timelineDuration - clip.StartFrame)
            : clip.DurationFrames > 0
                ? clip.DurationFrames
                : timelineDuration;
        int visitDuration = tapeDuration > 0
            ? Math.Min(tapeDuration, referenceDuration)
            : referenceDuration;

        if (clip.LoopingType is CinematicTapeReferenceLoopingType.Off or
            CinematicTapeReferenceLoopingType.Reverse)
        {
            yield return new TapeVisit(
                clip.Path,
                clip.StartFrame,
                visitDuration,
                clip.LoopingType == CinematicTapeReferenceLoopingType.Reverse);
            yield break;
        }

        if (tapeDuration <= 0)
        {
            yield return new TapeVisit(clip.Path, clip.StartFrame, visitDuration);
            yield break;
        }

        int iterationCount = Math.Max(1, (int)Math.Ceiling(referenceDuration / (double)tapeDuration));
        int maxTimelineFrame = Math.Max(
            clip.StartFrame + referenceDuration,
            (int)Math.Ceiling(videoDurationSeconds * CinematicConstants.TapeTicksPerSecond));
        for (int iteration = 0; iteration < iterationCount; iteration++)
        {
            int iterationStart = iteration * tapeDuration;
            int offset = clip.StartFrame + iterationStart;
            if (offset > maxTimelineFrame)
                break;

            bool reversedIteration =
                (clip.LoopingType is CinematicTapeReferenceLoopingType.PingPong or CinematicTapeReferenceLoopingType.PingPongInfinite) &&
                (iteration & 1) != 0;
            int iterationDuration = Math.Min(tapeDuration, Math.Max(0, referenceDuration - iterationStart));
            if (iterationDuration <= 0)
                continue;

            int? evaluationEndFrame = IsLoopIterationEvaluatedAtReferenceEnd(iteration, referenceDuration, tapeDuration)
                ? null
                : offset + (2 * tapeDuration);
            yield return new TapeVisit(clip.Path, offset, iterationDuration, reversedIteration, EvaluationEndFrame: evaluationEndFrame);
        }
    }

    private static bool IsLoopIterationEvaluatedAtReferenceEnd(int iteration, int referenceDuration, int tapeDuration)
    {
        if (referenceDuration <= 0 || tapeDuration <= 0)
            return false;

        int currentIteration = referenceDuration / tapeDuration;
        int currentIterationLength = referenceDuration % tapeDuration;
        if (currentIterationLength > 0 && iteration == currentIteration)
            return true;

        return currentIteration > 0 && iteration == currentIteration - 1;
    }
}