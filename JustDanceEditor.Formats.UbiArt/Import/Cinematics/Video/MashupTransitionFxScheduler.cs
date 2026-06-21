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
    // The active fade window starts after the delay and lasts for the remaining duration.
    private const double MashupVideoFadeBrickDurationBeats = 1.0;
    private const double MashupVideoFadeOffsetBeats = 0.75;
    private const double MashupVideoFadeActiveBeats = MashupVideoFadeBrickDurationBeats - MashupVideoFadeOffsetBeats;
    private const int FlashFxEmitterLeadFrames = 8;
    private const string LineActorKeyMarker = "x_lines_2x5";
    private const string FlashActorKeyMarker = "x_flashcoach";
    private static readonly TapeVisitTargetFilter LineFxFilter = new(
        IncludeKeyContains: [LineActorKeyMarker],
        ExcludeKeyContains: []);
    private static readonly TapeVisitTargetFilter LateFxFilter = new(
        IncludeKeyContains: [],
        ExcludeKeyContains: [LineActorKeyMarker]);

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
        int fxTapeLeadFrames = MashupTransitionTapeTiming.GetLocalLeadFrames(localFxClips);
        int fxTapeFlashLeadFrames = MashupTransitionTapeTiming.GetLocalLeadFrames(localFxClips, TargetsTransitionFlashFx);
        return BuildTransitionFxTapeVisits(
            Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics"),
            mashup,
            timelineStructure,
            fileSystem.VersionProfile.EngineVersion,
            fxTapeDurationFrames > 0 ? fxTapeDurationFrames : null,
            fxTapeLeadFrames,
            fxTapeFlashLeadFrames);
    }

    internal static IReadOnlyList<TapeVisit> BuildTransitionFxTapeVisits(
        string cinematicsFolder,
        LegacyMashupData mashup,
        TimelineStructureDocument timelineStructure,
        UbiArtEngineVersion engineVersion = UbiArtEngineVersion.JD2014,
        int? fxTapeDurationFrames = null,
        int fxTapeLeadFrames = 0,
        int fxTapeFlashLeadFrames = 0)
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
            int visitOffsetFromBlock = GetVisitOffsetFromBlock(
                fxTapeLeadFrames,
                fxTapeFlashLeadFrames);
            AddVisitPair(
                visits,
                fxTapePath,
                MashupTiming.GetLocalTapeFrame(timelineStructure, block.AbsoluteStartBeat) + visitOffsetFromBlock,
                fxTapeDurationFrames);
        }

        return visits;
    }

    internal static int GetCoachRevealDelayFrames(IReadOnlyList<TapeClip> localFxClips)
    {
        ArgumentNullException.ThrowIfNull(localFxClips);

        uint alphaTypeId = LegacyBinarySerializer.GetTypeId<CinematicAlphaClipBinary>();
        double revealFrame = double.NaN;
        foreach (TapeClip clip in localFxClips)
        {
            if (clip.TypeId != alphaTypeId ||
                clip.DurationFrames <= 0 ||
                !clip.Targets.Any(target => target.Key.Contains("x_mashup_godrayscreen", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            double candidate = clip.StartFrame + Math.Max(0, clip.DurationFrames);
            if (!double.IsFinite(revealFrame) || candidate < revealFrame)
                revealFrame = candidate;
        }

        return double.IsFinite(revealFrame) && revealFrame > 0.0
            ? Math.Max(0, (int)Math.Round(revealFrame))
            : 0;
    }

    internal static int GetVisitOffsetFromBlock(
        int fxTapeLeadFrames,
        int fxTapeFlashLeadFrames = 0)
    {
        int anchorLeadFrames = fxTapeFlashLeadFrames > 0 ? fxTapeFlashLeadFrames : fxTapeLeadFrames;
        int emitterLeadFrames = fxTapeFlashLeadFrames > 0 ? FlashFxEmitterLeadFrames : 0;
        return -Math.Max(0, anchorLeadFrames) - GetCoachFadeActiveFrames() - emitterLeadFrames;
    }

    internal static int GetCoachFadeActiveFrames() =>
        Math.Max(1, (int)Math.Round(MashupVideoFadeActiveBeats * CinematicConstants.TapeFramesPerBeat));

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

    private static bool TargetsTransitionFlashFx(TapeClip clip) =>
        clip.Targets.Any(target => target.Key.Contains(FlashActorKeyMarker, StringComparison.OrdinalIgnoreCase));
}
