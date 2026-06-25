using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupVideoRenderer
{
    public static async Task RenderAsync(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        TimelineStructureDocument timelineStructure,
        string tempFolder,
        string destination,
        ITextureService textureService,
        ILogger logger,
        IFileSystem io)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(songData);
        ArgumentNullException.ThrowIfNull(timelineStructure);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(textureService);
        ArgumentNullException.ThrowIfNull(io);

        LegacyMashupData mashup = songData.LegacyMashup
            ?? throw new InvalidOperationException("Mashup renderer was called for a non-mashup song.");

        io.CreateDirectory(tempFolder);
        io.CreateDirectory(Path.GetDirectoryName(destination) ?? tempFolder);

        TimelineStructureDocument renderTimelineStructure = MashupTiming.CreateRenderTimeline(timelineStructure);
        List<MaterializedMashupSegment> segments = MashupSegmentBuilder.MaterializeSegments(
            fileSystem,
            mashup,
            renderTimelineStructure,
            logger);
        double totalDurationSeconds = MashupTiming.GetOutputSeconds(renderTimelineStructure, mashup.DurationBeats);
        if (totalDurationSeconds <= 0.001)
            totalDurationSeconds = segments.Max(segment => segment.OutputStartSeconds + segment.OutputDurationSeconds);

        await RenderSharedSceneAsync(
            fileSystem,
            mashup,
            renderTimelineStructure,
            tempFolder,
            totalDurationSeconds,
            segments,
            destination,
            textureService,
            logger,
            io);

        logger.LogInformation(
            "Rendered mashup '{MashupName}' from {SegmentCount} renderer-composited source segment(s) to '{Destination}' ({Duration:0.###}s).",
            mashup.MapName,
            segments.Count,
            destination,
            totalDurationSeconds);
    }

    private static async Task RenderSharedSceneAsync(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        string tempFolder,
        double totalDurationSeconds,
        IReadOnlyList<MaterializedMashupSegment> segments,
        string destination,
        ITextureService textureService,
        ILogger logger,
        IFileSystem io)
    {
        if (totalDurationSeconds <= 0.001)
            throw new InvalidOperationException($"Mashup '{mashup.MapName}' has no positive output duration.");

        string originalSongName = fileSystem.SongName;
        MashupPleoTrackSet? pleoTracks = null;
        try
        {
            const string sharedMashupSceneSongName = "_mashup";
            fileSystem.UpdateSongName(sharedMashupSceneSongName);

            CinematicScene sharedScene = CinematicSceneReader.ReadSceneGraph(fileSystem, logger);
            MashupSceneActorFilter.ValidateSharedScene(sharedScene, sharedMashupSceneSongName, mashup.MapName);
            MashupCoachLayerPlacement coachPlacement = MashupSceneActorFilter.ResolveCoachLayerPlacement(sharedScene, logger);
            pleoTracks = MashupPleoTrackBuilder.Create(segments, tempFolder, coachPlacement);
            IReadOnlyList<TapeClip> localFxClips = MashupTransitionFxScheduler.ReadTransitionFxTapeClips(fileSystem, logger);
            IReadOnlySet<string> transitionFxActorKeys = MashupTransitionFxScheduler.BuildTransitionFxActorKeys(localFxClips);
            IReadOnlyList<TapeVisit> transitionTapeVisits = MashupTransitionTapeScheduler.BuildTransitionTapeVisits(fileSystem, mashup, timelineStructure, logger);
            IReadOnlyList<TapeVisit> transitionFxTapeVisits = MashupTransitionFxScheduler.BuildTransitionFxTapeVisits(fileSystem, mashup, timelineStructure, logger);
            IReadOnlyList<TapeVisit> sceneTapeVisits =
            [
                .. transitionTapeVisits,
                .. transitionFxTapeVisits
            ];

            await CinematicVisualRenderer.RenderSceneVideoAsync(
                fileSystem,
                io.Combine(tempFolder, "mashup_unified_scene_frames"),
                destination,
                totalDurationSeconds,
                timelineStructure,
                MashupVideoOutput.Width,
                MashupVideoOutput.Height,
                textureService,
                logger,
                io,
                sceneTapeVisits,
                actorFilter: actor => MashupSceneActorFilter.ShouldRenderSceneActor(actor, transitionFxActorKeys),
                preserveAlpha: false,
                externalPleoTracks: pleoTracks.CoachTracks,
                pleoFrameProvider: pleoTracks.SceneFrameProvider,
                actorMap: actor => MashupSceneActorFilter.ApplyTransitionOverlayPlane(actor, transitionFxActorKeys));
            logger.LogInformation(
                "Rendered mashup scene '{SceneSongName}' for '{MashupName}' with {SegmentCount} renderer-composited coach segment(s), {TransitionTapeCount} transition tape visit(s), and {FxTapeCount} FX tape visit(s).",
                sharedMashupSceneSongName,
                mashup.MapName,
                segments.Count,
                transitionTapeVisits.Count,
                transitionFxTapeVisits.Count);
        }
        finally
        {
            pleoTracks?.Dispose();
            fileSystem.UpdateSongName(originalSongName);
        }
    }
}