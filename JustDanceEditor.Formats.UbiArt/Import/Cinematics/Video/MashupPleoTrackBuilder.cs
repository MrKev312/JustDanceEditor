using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.Cinematics.Video;

using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupPleoTrackBuilder
{
    internal static MashupPleoTrackSet Create(
        IReadOnlyList<MaterializedMashupSegment> segments,
        string tempFolder,
        MashupCoachLayerPlacement placement)
    {
        List<CinematicExternalPleoTrack> tracks = new(segments.Count);
        List<MashupSegmentPleoSource> sceneSources = new(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            MaterializedMashupSegment segment = segments[i];
            double sourceInputDuration = Math.Max(0.001, segment.SourceDurationSeconds);
            double speedFactor = Math.Max(0.001, segment.OutputDurationSeconds / Math.Max(0.001, segment.SourceDurationSeconds));
            string pipeFilter = string.Create(
                CultureInfo.InvariantCulture,
                $"trim=start={segment.SourceStartSeconds:0.######}:duration={sourceInputDuration:0.######},setpts=(PTS-STARTPTS)*{speedFactor:0.######},fps={CinematicConstants.OutputFramesPerSecond},format=bgra");
            PleoFrameProvider provider = PleoFrameProvider.Create(
                tempFolder,
                rawPath: null,
                segment.SourceWidth,
                segment.VisibleHeight,
                segment.AlphaHeight,
                pipeSourcePath: segment.SourceStreamFactory == null ? segment.SourcePath : null,
                pipeVideoFilter: pipeFilter,
                pipeInputFactory: segment.SourceStreamFactory);

            ProjectedQuad quad = CinematicExternalPleoTrack.CreateScreenQuad(0, 0, MashupVideoOutput.Width, MashupVideoOutput.Height);
            CinematicUvRect uvOverride = CreateUvOverride(segment.OffsetX, segment.OffsetY, segment.Scale);
            int outputStartFrame = Math.Max(0, (int)Math.Round(segment.OutputStartSeconds * CinematicConstants.OutputFramesPerSecond));
            int outputFrameCount = Math.Max(1, (int)Math.Ceiling(segment.OutputDurationSeconds * CinematicConstants.OutputFramesPerSecond));
            sceneSources.Add(new MashupSegmentPleoSource(provider, outputStartFrame, outputFrameCount));
            float depth = placement.Depth;
            CinematicActor actor = MashupSceneActorFilter.CreateSyntheticCoachActor(i, placement);
            RenderableCinematicActor renderable = new(
                actor,
                Image: null,
                CinematicGeometryProjector.CreatePleoVideoGeometry(),
                CinematicLayerPlane.Composite,
                placement.ScenePriority,
                depth,
                placement.PrimitiveTieBreak - i,
                CinematicRenderKind.PleoVideo);
            ResolvedActorState state = new(
                PositionX: 0,
                PositionY: 0,
                PositionZ: depth,
                ScaleX: 1,
                ScaleY: 1,
                Angle: 0,
                Alpha: 1,
                RgbTint.White,
                XFlipped: false);
            tracks.Add(new CinematicExternalPleoTrack(
                renderable,
                state,
                quad,
                provider,
                outputStartFrame,
                outputFrameCount,
                segment.OutputDurationSeconds,
                segment.FadeInDurationSeconds,
                segment.FadeOutDurationSeconds,
                segment.FadeInDelaySeconds,
                useFullAlphaBounds: true,
                uvOverride: uvOverride,
                disposeFrameProvider: false));
        }

        return new MashupPleoTrackSet(
            tracks,
            new MashupTimelinePleoFrameProvider(sceneSources));
    }

    internal static CinematicUvRect CreateUvOverride(double offsetX, double offsetY, double scale)
    {
        double safeScale = scale == 0 ? 1.0 : scale;
        double inverseScale = 1.0 / safeScale;
        double renderQuadRatio = MashupVideoOutput.Width / (double)MashupVideoOutput.Height;
        double scaleOffsetU = 0.5 - (0.5 / renderQuadRatio);
        double scaleOffsetV = 0.0;
        double translationU = (-offsetX / renderQuadRatio) / safeScale;
        double translationV = -offsetY / safeScale;
        double left = (scaleOffsetU * (1.0 - inverseScale)) + translationU;
        double top = (scaleOffsetV * (1.0 - inverseScale)) + translationV;
        double right = inverseScale + (scaleOffsetU * (1.0 - inverseScale)) + translationU;
        double bottom = inverseScale + (scaleOffsetV * (1.0 - inverseScale)) + translationV;

        return new CinematicUvRect((float)left, (float)top, (float)right, (float)bottom);
    }
}
