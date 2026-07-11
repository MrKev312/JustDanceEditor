using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupTransitionFxScheduler
{
    private const string LineActorKeyMarker = "x_lines_2x5";
    private const string FullScreenGodrayActorKeyMarker = "x_mashup_godrayscreen";
    private static readonly TapeVisitTargetFilter LineFxFilter = new(
        IncludeKeyContains: [LineActorKeyMarker],
        ExcludeKeyContains: []);
    private static readonly TapeVisitTargetFilter LateFxFilter = new(
        IncludeKeyContains: [],
        ExcludeKeyContains: [LineActorKeyMarker, FullScreenGodrayActorKeyMarker]);

    internal static IReadOnlyList<TapeClip> ReadTransitionFxTapeClips(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015)
            return [];

        string fxTapePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", "fx.tape");
        return CinematicTapeClipReader.ReadLocalTapeClips(fileSystem, fxTapePath, logger);
    }

    internal static IReadOnlyList<TapeVisit> BuildTransitionFxTapeVisits(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(mashup);
        ArgumentNullException.ThrowIfNull(timelineStructure);

        IReadOnlyList<TapeClip> localFxClips = ReadTransitionFxTapeClips(
            fileSystem,
            logger ?? NullLogger.Instance);
        int fxTapeDurationFrames = MashupTransitionTapeTiming.GetLocalDurationFrames(localFxClips);
        return BuildTransitionFxTapeVisits(
            Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics"),
            mashup,
            timelineStructure,
            fileSystem.VersionProfile.EngineVersion,
            fxTapeDurationFrames > 0 ? fxTapeDurationFrames : null);
    }

    internal static IReadOnlyList<TapeVisit> BuildTransitionFxTapeVisits(
        string cinematicsFolder,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        UbiArtEngineVersion engineVersion = UbiArtEngineVersion.JD2014,
        int? fxTapeDurationFrames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cinematicsFolder);
        ArgumentNullException.ThrowIfNull(mashup);
        ArgumentNullException.ThrowIfNull(timelineStructure);

        if (engineVersion == UbiArtEngineVersion.JD2015)
            return [];

        List<TapeVisit> visits = [];
        string fxTapePath = Path.Combine(cinematicsFolder, "fx.tape");
        foreach (LegacyMashupBlock block in MashupSegmentBuilder.BuildTransitionBlocks(mashup))
        {
            AddVisitPair(
                visits,
                fxTapePath,
                MashupTiming.GetLocalTapeFrame(timelineStructure, block.AbsoluteStartBeat) -
                    MashupTransitionTapeTiming.Jd2014TransitionLeadFrames,
                fxTapeDurationFrames);
        }

        return visits;
    }

    internal static IReadOnlySet<string> BuildTransitionFxActorKeys(IReadOnlyList<TapeClip> localFxClips)
    {
        ArgumentNullException.ThrowIfNull(localFxClips);

        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (TapeClip clip in localFxClips)
        {
            if (clip.Targets.Count == 0)
                continue;

            foreach (ActorTargetPath target in clip.Targets)
                keys.Add(target.Key);
        }

        return keys;
    }

    internal static IReadOnlySet<uint> BuildTransitionFxNameIds(IReadOnlyList<TapeClip> localFxClips)
    {
        ArgumentNullException.ThrowIfNull(localFxClips);

        HashSet<uint> nameIds = [];
        foreach (TapeClip clip in localFxClips)
        {
            if (!LegacyBinarySerializer.IsTypeId<CinematicFxClipBinary>(clip.TypeId) ||
                !CinematicFxIds.IsValid(clip.FxNameId))
            {
                continue;
            }

            nameIds.Add(clip.FxNameId);
        }

        return nameIds;
    }

    private static void AddVisitPair(
        List<TapeVisit> visits,
        string fxTapePath,
        int lineVisitOffsetFrame,
        int? fxTapeDurationFrames)
    {
        visits.Add(new TapeVisit(
            fxTapePath,
            lineVisitOffsetFrame,
            fxTapeDurationFrames,
            TargetFilter: LineFxFilter));
        visits.Add(new TapeVisit(
            fxTapePath,
            lineVisitOffsetFrame,
            fxTapeDurationFrames,
            TargetFilter: LateFxFilter));
    }
}
