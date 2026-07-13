using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupTransitionTapeScheduler
{
    private const string InitTape = "init.tape";
    private const string FallbackInitialColorTape = "color_green.tape";
    private static readonly string[] FallbackTransitionColorTapes =
    [
        "color_purple.tape",
        "color_blue.tape",
        "color_orange.tape",
        "color_green.tape"
    ];
    private static readonly string[] FallbackUvScrollTapes =
    [
        "uv_left.tape",
        "uv_right.tape"
    ];

    internal static IReadOnlyList<TapeVisit> BuildTransitionTapeVisits(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(mashup);
        ArgumentNullException.ThrowIfNull(timelineStructure);

        ILogger resolvedLogger = logger ?? NullLogger.Instance;
        MashupTransitionTapeTimingData timing = MashupTransitionTapeTiming.Build(fileSystem, mashup, resolvedLogger);
        MashupTransitionColorTapeSequence colorTapeSequence = MashupTransitionTapeDiscovery.ResolveColorTapeSequence(
            fileSystem,
            resolvedLogger);
        IReadOnlyList<string> uvScrollTapeNames = MashupTransitionTapeDiscovery.ResolveUvScrollTapeSequence(
            fileSystem,
            resolvedLogger);
        IReadOnlyDictionary<string, int> uvScrollTapeDurationFrames = MashupTransitionTapeTiming.BuildUvScrollTapeDurations(
            fileSystem,
            uvScrollTapeNames,
            resolvedLogger);
        return BuildTransitionTapeVisits(
            Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics"),
            mashup,
            timelineStructure,
            fileSystem.VersionProfile.EngineVersion,
            timing.Durations,
            timing.LeadFrames,
            colorTapeSequence.TransitionTapeNames,
            colorTapeSequence.InitialTapeName,
            uvScrollTapeNames,
            uvScrollTapeDurationFrames);
    }

    internal static IReadOnlyList<TapeVisit> BuildTransitionTapeVisits(
        string cinematicsFolder,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        UbiArtEngineVersion engineVersion = UbiArtEngineVersion.JD2014,
        IReadOnlyDictionary<string, int>? transitionTapeDurationFrames = null,
        IReadOnlyDictionary<string, int>? transitionTapeLeadFrames = null,
        IReadOnlyList<string>? transitionColorTapeNames = null,
        string? initialColorTapeName = null,
        IReadOnlyList<string>? uvScrollTapeNames = null,
        IReadOnlyDictionary<string, int>? uvScrollTapeDurationFrames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cinematicsFolder);
        ArgumentNullException.ThrowIfNull(mashup);
        ArgumentNullException.ThrowIfNull(timelineStructure);

        List<TapeVisit> visits = [];
        IReadOnlyList<LegacyMashupBlock> selectedBlocks = MashupSegmentBuilder.BuildTransitionBlocks(mashup);
        List<MashupTapeInterval> stateIntervals = [];

        int firstSelectedStartBeat = selectedBlocks.Count > 0
            ? selectedBlocks[0].AbsoluteStartBeat
            : mashup.DurationBeats;
        int initialStateDuration = MashupTiming.GetLocalTapeFrame(timelineStructure, firstSelectedStartBeat) -
            MashupTiming.GetLocalTapeFrame(timelineStructure, 0);
        int initialColorDuration = Math.Max(1, initialStateDuration);
        string resolvedInitialColorTape = string.IsNullOrWhiteSpace(initialColorTapeName)
            ? FallbackInitialColorTape
            : initialColorTapeName;
        IReadOnlyList<string> resolvedTransitionColorTapes = transitionColorTapeNames is { Count: > 0 }
            ? transitionColorTapeNames
            : FallbackTransitionColorTapes;
        visits.Add(new TapeVisit(
            MashupTransitionTapeDiscovery.BuildCinematicsTapePath(cinematicsFolder, InitTape),
            MashupTiming.GetLocalTapeFrame(timelineStructure, 0),
            PersistentMaterialState: true));
        visits.Add(new TapeVisit(
            MashupTransitionTapeDiscovery.BuildCinematicsTapePath(cinematicsFolder, resolvedInitialColorTape),
            MashupTiming.GetLocalTapeFrame(timelineStructure, 0),
            initialColorDuration));
        if (initialStateDuration > 0)
        {
            stateIntervals.Add(new MashupTapeInterval(
                MashupTiming.GetLocalTapeFrame(timelineStructure, 0),
                initialStateDuration));
        }

        for (int transitionIndex = 0; transitionIndex < selectedBlocks.Count; transitionIndex++)
        {
            LegacyMashupBlock block = selectedBlocks[transitionIndex];
            int timeOffsetFrames = MashupTiming.GetLocalTapeFrame(timelineStructure, block.AbsoluteStartBeat);
            int nextStateStartBeat = transitionIndex + 1 < selectedBlocks.Count
                ? selectedBlocks[transitionIndex + 1].AbsoluteStartBeat
                : mashup.DurationBeats;
            int colorDurationFrames = Math.Max(
                1,
                MashupTiming.GetLocalTapeFrame(timelineStructure, nextStateStartBeat) - timeOffsetFrames);
            string colorTape = resolvedTransitionColorTapes[transitionIndex % resolvedTransitionColorTapes.Count];
            visits.Add(new TapeVisit(MashupTransitionTapeDiscovery.BuildCinematicsTapePath(cinematicsFolder, colorTape), timeOffsetFrames, colorDurationFrames));
            stateIntervals.Add(new MashupTapeInterval(timeOffsetFrames, colorDurationFrames));

            if (engineVersion == UbiArtEngineVersion.JD2015)
            {
                const string pulseTape = "pulse_fg.tape";
                visits.Add(new TapeVisit(
                    Path.Combine(cinematicsFolder, pulseTape),
                    timeOffsetFrames - MashupTransitionTapeTiming.GetLead(transitionTapeLeadFrames, pulseTape),
                    MashupTransitionTapeTiming.GetDuration(transitionTapeDurationFrames, pulseTape)));
            }
            else
            {
                int coachMoveIndex = (transitionIndex % 18) + 1;
                string coachMoveTape = $"coach_move_{coachMoveIndex}.tape";
                int transitionStartFrames = timeOffsetFrames - MashupTransitionTapeTiming.Jd2014TransitionLeadFrames;
                visits.Add(new TapeVisit(
                    Path.Combine(cinematicsFolder, coachMoveTape),
                    transitionStartFrames,
                    MashupTransitionTapeTiming.GetDuration(transitionTapeDurationFrames, coachMoveTape)));
            }
        }

        AddUvScrollTapeVisits(
            visits,
            cinematicsFolder,
            stateIntervals,
            engineVersion,
            uvScrollTapeNames,
            uvScrollTapeDurationFrames);
        return visits;
    }

    private static void AddUvScrollTapeVisits(
        List<TapeVisit> visits,
        string cinematicsFolder,
        IReadOnlyList<MashupTapeInterval> stateIntervals,
        UbiArtEngineVersion engineVersion,
        IReadOnlyList<string>? uvScrollTapeNames,
        IReadOnlyDictionary<string, int>? uvScrollTapeDurationFrames)
    {
        if (engineVersion != UbiArtEngineVersion.JD2015 || stateIntervals.Count == 0)
            return;

        IReadOnlyList<string> resolvedUvScrollTapeNames = uvScrollTapeNames is { Count: > 0 }
            ? uvScrollTapeNames
            : FallbackUvScrollTapes;
        if (resolvedUvScrollTapeNames.Count == 0)
            return;

        for (int intervalIndex = 0; intervalIndex < stateIntervals.Count; intervalIndex++)
        {
            MashupTapeInterval interval = stateIntervals[intervalIndex];
            if (interval.DurationFrames <= 0)
                continue;

            string tapeName = resolvedUvScrollTapeNames[intervalIndex % resolvedUvScrollTapeNames.Count];
            int localTapeDuration = GetUvScrollTapeDuration(uvScrollTapeDurationFrames, tapeName);
            AddLoopedTapeVisits(
                visits,
                MashupTransitionTapeDiscovery.BuildCinematicsTapePath(cinematicsFolder, tapeName),
                interval.StartFrame,
                interval.DurationFrames,
                localTapeDuration);
        }
    }

    private static void AddLoopedTapeVisits(
        List<TapeVisit> visits,
        string tapePath,
        int startFrame,
        int durationFrames,
        int localTapeDurationFrames)
    {
        if (durationFrames <= 0)
            return;

        int iterationDuration = localTapeDurationFrames > 0
            ? localTapeDurationFrames
            : (int)Math.Round(CinematicConstants.TapeFramesPerBeat);
        int endFrame = startFrame + durationFrames;
        for (int frame = startFrame; frame < endFrame; frame += iterationDuration)
        {
            int visitDuration = Math.Min(iterationDuration, endFrame - frame);
            visits.Add(new TapeVisit(tapePath, frame, visitDuration));
        }
    }

    private static int GetUvScrollTapeDuration(IReadOnlyDictionary<string, int>? durations, string tapeName)
    {
        if (durations == null)
            return 0;

        string normalizedTapeName = MashupTransitionTapeDiscovery.GetTapeName(tapeName);
        return durations.TryGetValue(normalizedTapeName, out int duration) && duration > 0
            ? duration
            : 0;
    }

    private sealed record MashupTapeInterval(int StartFrame, int DurationFrames);
}