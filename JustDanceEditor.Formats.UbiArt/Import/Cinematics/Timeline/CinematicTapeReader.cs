using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model.Clips;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal static class CinematicTapeReader
{
    public static CinematicTapeData ReadCinematicTapes(
        JustDanceUbiArtFileSystem fileSystem,
        double videoDurationSeconds,
        ILogger logger,
        IReadOnlyList<TapeVisit>? additionalRootVisits = null,
        CinematicScene? scene = null)
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<TapeClip>> tapeClipCache = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> tapeDurations = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, CinematicTapeCaseDefinition> tapeCasesByActorKey =
            CinematicTapeLauncherPlanner.BuildTapeCaseDefinitions(fileSystem, scene, logger);
        ClipTargetResolver? tapeLauncherTargetResolver = scene == null ? null : ClipTargetResolver.Create(scene);
        Dictionary<string, int> tapeLauncherSequenceIndices = new(StringComparer.OrdinalIgnoreCase);
        List<PropertyClip> propertyClips = [];
        List<SourceEvaluationClip> sourceEvaluationClips = [];
        List<CinematicSpawnActorRuntimeClip> spawnActorClips = [];
        int parsedTapeCount = 0;
        int order = 0;
        int renderStartFrame = 0;
        int materialTimeStartFrame = 0;

        foreach (string mainSequencePath in EnumerateMainSequenceTapePaths(fileSystem))
        {
            int parsedBefore = parsedTapeCount;
            VisitTape(new TapeVisit(CinematicNames.NormalizePath(mainSequencePath), 0));
            if (parsedTapeCount > parsedBefore)
                break;
        }

        if (additionalRootVisits != null)
        {
            foreach (TapeVisit additionalRootVisit in additionalRootVisits)
                VisitTape(additionalRootVisit with { Path = CinematicNames.NormalizePath(additionalRootVisit.Path) });
        }

        logger.LogDebug(
            "Parsed {TapeCount} cinematic tape(s) and {ClipCount} property clip(s); render start frame {RenderStartFrame}; material time start frame {MaterialTimeStartFrame}.",
            parsedTapeCount,
            propertyClips.Count,
            renderStartFrame,
            materialTimeStartFrame);

        return new CinematicTapeData(
            [.. propertyClips],
            [.. sourceEvaluationClips],
            [.. spawnActorClips],
            renderStartFrame,
            materialTimeStartFrame);

        void VisitTape(TapeVisit visit)
        {
            if (visited.Count >= CinematicConstants.MaxTapeVisits)
                return;

            string visitKey = CinematicTapeVisitPlanner.BuildVisitKey(visit, includeTargetFilter: true);
            if (!visited.Add(visitKey))
                return;

            if (!CinematicTapeClipReader.TryGetLocalTapeClips(fileSystem, tapeClipCache, visit.Path, logger, out IReadOnlyList<TapeClip> localClips))
                return;

            parsedTapeCount++;

            List<(TapeClip Clip, int SourceIndex)> templateLoadedOrder =
            [
                .. localClips
                .Select((clip, index) => (clip, index))
                .OrderBy(entry => entry.clip.StartFrame)
                .ThenBy(entry => entry.clip.DurationFrames)
                .ThenBy(entry => entry.index)
            ];
            IEnumerable<TapeClip> sourceSortedClips = templateLoadedOrder
                .OrderBy(entry => entry.Clip.StartFrame + Math.Max(entry.Clip.DurationFrames, 0))
                .Select(entry => entry.Clip);

            foreach (TapeClip localClip in sourceSortedClips)
            {
                if (!CinematicTapeVisitPlanner.TryApplyTapeVisit(localClip, visit, out TapeClip clip))
                    continue;

                materialTimeStartFrame = Math.Min(materialTimeStartFrame, clip.StartFrame);

                if (LegacyBinarySerializer.IsTypeId<CinematicTapeReferenceClipBinary>(clip.TypeId) && clip.Path != null)
                {
                    foreach (TapeVisit childVisit in CinematicTapeReferencePlanner.GetReferenceVisits(
                        tapeDurations,
                        fileSystem,
                        logger,
                        clip,
                        videoDurationSeconds,
                        visit.DurationFrames.HasValue))
                    {
                        VisitTape(childVisit with
                        {
                            TargetFilter = visit.TargetFilter,
                            EvaluationEndFrame = MergeEvaluationEndFrame(visit.EvaluationEndFrame, childVisit.EvaluationEndFrame)
                        });
                    }

                    sourceEvaluationClips.Add(new SourceEvaluationClip(null, clip.TypeId, clip.StartFrame, clip.DurationFrames, EvaluationEndFrame: visit.EvaluationEndFrame));
                    continue;
                }

                if (clip.SpawnActor != null)
                {
                    int spawnOrder = order++;
                    spawnActorClips.Add(new CinematicSpawnActorRuntimeClip(
                        clip.SpawnActor,
                        clip.StartFrame,
                        clip.DurationFrames,
                        spawnOrder));
                    AddSpawnActorVisibilityClips(
                        propertyClips,
                        sourceEvaluationClips,
                        clip,
                        spawnOrder,
                        visit.EvaluationEndFrame,
                        ref order);
                    sourceEvaluationClips.Add(new SourceEvaluationClip(null, clip.TypeId, clip.StartFrame, clip.DurationFrames, EvaluationEndFrame: visit.EvaluationEndFrame));
                    continue;
                }

                if (clip.TapeLauncher != null)
                {
                    foreach (TapeVisit launcherVisit in CinematicTapeLauncherPlanner.GetLauncherVisits(
                        fileSystem,
                        tapeCasesByActorKey,
                        tapeLauncherTargetResolver,
                        tapeLauncherSequenceIndices,
                        visit,
                        clip,
                        logger))
                    {
                        VisitTape(launcherVisit with
                        {
                            EvaluationEndFrame = MergeEvaluationEndFrame(visit.EvaluationEndFrame, launcherVisit.EvaluationEndFrame)
                        });
                    }

                    sourceEvaluationClips.Add(new SourceEvaluationClip(null, clip.TypeId, clip.StartFrame, clip.DurationFrames, EvaluationEndFrame: visit.EvaluationEndFrame));
                    continue;
                }

                if ((!CinematicTapeClipTypes.IsPropertyClip(clip.TypeId) && !LegacyBinarySerializer.IsTypeId<CinematicFxClipBinary>(clip.TypeId)) ||
                    clip.Targets.Count == 0)
                {
                    sourceEvaluationClips.Add(new SourceEvaluationClip(null, clip.TypeId, clip.StartFrame, clip.DurationFrames, EvaluationEndFrame: visit.EvaluationEndFrame));
                    continue;
                }

                CinematicVisualState state = CinematicVisualStateBuilder.BuildStateFromClip(clip.TypeId, clip.Curves, clip.LayerEnable, clip.MaterialGraphic, clip.Pivot, clip.Animation);
                foreach (ActorTargetPath target in clip.Targets)
                {
                    int propertyOrder = order++;
                    propertyClips.Add(new PropertyClip(
                        target,
                        clip.TypeId,
                        clip.StartFrame,
                        clip.DurationFrames,
                        state,
                        propertyOrder,
                        clip.FxNameId,
                        clip.KillParticlesOnEnd,
                        FxGenerationDurationFrames: clip.FxGenerationDurationFrames,
                        FxGenerationStartFrame: clip.FxGenerationStartFrame,
                        PersistentMaterialState: visit.PersistentMaterialState,
                        EvaluationEndFrame: visit.EvaluationEndFrame));
                    sourceEvaluationClips.Add(new SourceEvaluationClip(propertyOrder, clip.TypeId, clip.StartFrame, clip.DurationFrames, EvaluationEndFrame: visit.EvaluationEndFrame));
                }
            }
        }
    }

    private static int? MergeEvaluationEndFrame(int? parentEndFrame, int? childEndFrame) =>
        (parentEndFrame, childEndFrame) switch
        {
            ({ } parent, { } child) => Math.Min(parent, child),
            ({ } parent, null) => parent,
            (null, { } child) => child,
            _ => null
        };

    private static IEnumerable<string> EnumerateMainSequenceTapePaths(JustDanceUbiArtFileSystem fileSystem)
    {
        foreach (string mapWorldFolder in EnumerateMapWorldFolderPathVariants(fileSystem.InputFolders.MapWorldFolder))
        {
            foreach (string songName in EnumerateSongNamePathVariants(fileSystem.SongName))
                yield return Path.Combine(mapWorldFolder, "cinematics", $"{songName}_mainsequence.tape");
        }

        if (string.Equals(fileSystem.SongName, "_mashup", StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(fileSystem.InputFolders.MapWorldFolder, "cinematics", "mu_mainsequence.tape");
    }

    private static IEnumerable<string> EnumerateMapWorldFolderPathVariants(string mapWorldFolder)
    {
        if (string.IsNullOrWhiteSpace(mapWorldFolder))
            yield break;

        yield return mapWorldFolder;

        string? parent = Path.GetDirectoryName(mapWorldFolder);
        string folderName = Path.GetFileName(mapWorldFolder);
        string lowerFolderName = folderName.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(parent) &&
            !string.Equals(folderName, lowerFolderName, StringComparison.Ordinal))
        {
            yield return Path.Combine(parent, lowerFolderName);
        }
    }

    private static IEnumerable<string> EnumerateSongNamePathVariants(string songName)
    {
        if (string.IsNullOrWhiteSpace(songName))
            yield break;

        yield return songName;

        string lower = songName.ToLowerInvariant();
        if (!string.Equals(songName, lower, StringComparison.Ordinal))
            yield return lower;
    }

    public static IReadOnlyList<SoundSetClip> ReadSoundSetClips(
        JustDanceUbiArtFileSystem fileSystem,
        string mainSequencePath,
        ILogger logger)
    {
        Dictionary<string, IReadOnlyList<TapeClip>> tapeClipCache = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        List<SoundSetClip> clips = [];

        VisitTape(new TapeVisit(CinematicNames.NormalizePath(mainSequencePath), 0));
        return clips;

        void VisitTape(TapeVisit visit)
        {
            if (visited.Count >= CinematicConstants.MaxTapeVisits)
                return;

            string visitKey = CinematicTapeVisitPlanner.BuildVisitKey(visit, includeTargetFilter: false);
            if (!visited.Add(visitKey))
                return;

            if (!CinematicTapeClipReader.TryGetLocalTapeClips(fileSystem, tapeClipCache, visit.Path, logger, out IReadOnlyList<TapeClip> localClips))
                return;

            foreach (TapeClip localClip in localClips)
            {
                if (!CinematicTapeVisitPlanner.TryApplyTapeVisit(localClip, visit, out TapeClip clip))
                    continue;

                if (LegacyBinarySerializer.IsTypeId<CinematicTapeReferenceClipBinary>(clip.TypeId) && clip.Path != null)
                {
                    VisitTape(new TapeVisit(clip.Path, clip.StartFrame, clip.DurationFrames, clip.LoopingType == CinematicTapeReferenceLoopingType.Reverse));
                    continue;
                }

                if (LegacyBinarySerializer.IsTypeId<CinematicSoundSetClipBinary>(clip.TypeId) && !string.IsNullOrWhiteSpace(clip.Path))
                {
                    clips.Add(new SoundSetClip
                    {
                        StartTime = clip.StartFrame,
                        Duration = clip.DurationFrames,
                        SoundSetPath = clip.Path
                    });
                }
            }
        }
    }

    private static void AddSpawnActorVisibilityClips(
        List<PropertyClip> propertyClips,
        List<SourceEvaluationClip> sourceEvaluationClips,
        TapeClip clip,
        int spawnOrder,
        int? evaluationEndFrame,
        ref int order)
    {
        if (clip.SpawnActor == null || string.IsNullOrWhiteSpace(clip.SpawnActor.ActorName))
            return;

        ActorTargetPath target = new([clip.SpawnActor.ActorName]);
        uint alphaTypeId = LegacyBinarySerializer.GetTypeId<CinematicAlphaClipBinary>();
        CinematicVisualState visible = new(
            Transform: null,
            Material: new CinematicMaterial(null, null, null, CinematicVisualStateBuilder.CreateConstantCurve(1)));
        propertyClips.Add(new PropertyClip(
            target,
            alphaTypeId,
            clip.StartFrame,
            clip.DurationFrames,
            visible,
            spawnOrder,
            EvaluationEndFrame: evaluationEndFrame));
        sourceEvaluationClips.Add(new SourceEvaluationClip(spawnOrder, alphaTypeId, clip.StartFrame, clip.DurationFrames, EvaluationEndFrame: evaluationEndFrame));

        int hideFrame = clip.StartFrame + Math.Max(clip.DurationFrames, 0) + 1;
        int hideOrder = order++;
        CinematicVisualState hidden = new(
            Transform: null,
            Material: new CinematicMaterial(null, null, null, CinematicVisualStateBuilder.CreateConstantCurve(0)));
        propertyClips.Add(new PropertyClip(
            target,
            alphaTypeId,
            hideFrame,
            0,
            hidden,
            hideOrder,
            EvaluationEndFrame: evaluationEndFrame));
        sourceEvaluationClips.Add(new SourceEvaluationClip(hideOrder, alphaTypeId, hideFrame, 0, EvaluationEndFrame: evaluationEndFrame));
    }
}