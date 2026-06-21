using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Video;

using KevInc.UbiArt.FileSystem;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupVideoOutput
{
    public const int Width = 1920;
    public const int Height = 1080;
}

internal readonly record struct MashupSourceVideo(CookedFile File, string SourceSongName, bool IsFullMapVideo);

internal readonly record struct MashupCoachLayerPlacement(
    float Depth,
    int ScenePriority,
    int PrimitiveTieBreak)
{
    public static MashupCoachLayerPlacement Fallback { get; } = new(0.0f, 0, -100000);
}

internal sealed record MaterializedMashupSegment(
    string SourcePath,
    int BlockIndex,
    int AbsoluteStartBeat,
    int DurationBeats,
    int SourceFirstBeat,
    int SourceLastBeat,
    string SourceSongName,
    int SourceWidth,
    int VisibleHeight,
    int AlphaHeight,
    bool HasStackedAlpha,
    double SourceStartSeconds,
    double SourceDurationSeconds,
    double OutputStartSeconds,
    double OutputDurationSeconds,
    float OffsetX,
    float OffsetY,
    float Scale,
    Func<Stream>? SourceStreamFactory = null,
    double FadeInDurationSeconds = 0.0,
    double FadeInDelaySeconds = 0.0,
    double FadeOutDurationSeconds = 0.0);

internal sealed record MashupPleoTrackSet(
    IReadOnlyList<CinematicExternalPleoTrack> CoachTracks,
    MashupTimelinePleoFrameProvider SceneFrameProvider) : IDisposable
{
    public void Dispose()
    {
        foreach (CinematicExternalPleoTrack track in CoachTracks)
            track.Dispose();
        SceneFrameProvider.Dispose();
    }
}

internal sealed record MashupSegmentPleoSource(
    PleoFrameProvider Provider,
    int OutputStartFrame,
    int OutputFrameCount);

internal sealed class MashupTimelinePleoFrameProvider : IPleoFrameProvider
{
    private const int PrefetchLeadFrames = 18;
    private readonly IReadOnlyList<MashupSegmentPleoSource> sources;
    private int activeSourceIndex;
    private int prefetchSourceIndex;

    public MashupTimelinePleoFrameProvider(IReadOnlyList<MashupSegmentPleoSource> sources)
    {
        this.sources = sources;
        PrefetchAround(outputFrame: 0);
    }

    public bool ReturnsReusableFrames => sources.Any(source => source.Provider.ReturnsReusableFrames);

    public PleoFrameSource? TryLoad(int outputFrame)
    {
        PrefetchAround(outputFrame);

        int startIndex = GetSourceCursorStart(activeSourceIndex, outputFrame);
        for (int sourceIndex = startIndex; sourceIndex < sources.Count; sourceIndex++)
        {
            MashupSegmentPleoSource source = sources[sourceIndex];
            int localFrame = outputFrame - source.OutputStartFrame;
            if (localFrame < 0 || localFrame >= source.OutputFrameCount)
                continue;

            activeSourceIndex = sourceIndex;
            return source.Provider.TryLoadDecodedFrame(localFrame);
        }

        for (int sourceIndex = 0; sourceIndex < startIndex; sourceIndex++)
        {
            MashupSegmentPleoSource source = sources[sourceIndex];
            int localFrame = outputFrame - source.OutputStartFrame;
            if (localFrame < 0 || localFrame >= source.OutputFrameCount)
                continue;

            activeSourceIndex = sourceIndex;
            return source.Provider.TryLoadDecodedFrame(localFrame);
        }

        return null;
    }

    public PleoFrameSource? TryLoadDecodedFrame(int zeroBasedFrame) => TryLoad(zeroBasedFrame);

    public void PrefetchDecodedFrames(int zeroBasedFrame, int frameCount)
    {
        if (frameCount <= 0)
            return;

        for (int frameOffset = 0; frameOffset < frameCount; frameOffset++)
            PrefetchAround(zeroBasedFrame + frameOffset);
    }

    public void Dispose()
    {
        foreach (MashupSegmentPleoSource source in sources)
            source.Provider.Dispose();
    }

    private void PrefetchAround(int outputFrame)
    {
        int startIndex = GetSourceCursorStart(prefetchSourceIndex, outputFrame);
        PrefetchSourcesAround(outputFrame, startIndex, sources.Count);
        if (startIndex > 0)
            PrefetchSourcesAround(outputFrame, 0, startIndex);
    }

    private void PrefetchSourcesAround(int outputFrame, int startIndex, int endIndex)
    {
        for (int sourceIndex = startIndex; sourceIndex < endIndex; sourceIndex++)
        {
            MashupSegmentPleoSource source = sources[sourceIndex];
            int localFrame = outputFrame - source.OutputStartFrame;
            if (localFrame < -PrefetchLeadFrames || localFrame >= source.OutputFrameCount)
                continue;

            prefetchSourceIndex = sourceIndex;
            int startFrame = Math.Max(0, localFrame);
            int remainingFrames = source.OutputFrameCount - startFrame;
            int framesToPrefetch = Math.Min(PrefetchLeadFrames, remainingFrames);
            source.Provider.PrefetchDecodedFrames(startFrame, framesToPrefetch);
        }
    }

    private int GetSourceCursorStart(int cursor, int outputFrame)
    {
        if ((uint)cursor >= (uint)sources.Count)
            return 0;

        MashupSegmentPleoSource source = sources[cursor];
        return outputFrame < source.OutputStartFrame
            ? 0
            : cursor;
    }
}
