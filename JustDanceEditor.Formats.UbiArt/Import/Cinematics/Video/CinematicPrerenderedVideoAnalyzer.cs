using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal readonly record struct CinematicSingleVideoScene(
    string SourceVideoPath,
    string OutputActorKey,
    Rectangle OutputBounds);

internal static class CinematicPrerenderedVideoAnalyzer
{
    private const int AnalysisOutputWidth = 1920;
    private const int AnalysisOutputHeight = 1080;

    public static bool TryAnalyzeSingleVideoScene(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        double outputDurationSeconds,
        ILogger logger,
        out CinematicSingleVideoScene result)
    {
        result = default;

        CinematicScene scene;
        try
        {
            scene = CinematicSceneReader.ReadSceneGraph(fileSystem, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read cinematic scene graph for pre-rendered video fast path.");
            return false;
        }

        if (scene.Actors.Count == 0)
            return false;

        CinematicTapeData tapeData = ReadTapeDataOrEmpty(fileSystem, scene, outputDurationSeconds, logger);
        if (tapeData.SpawnActorClips.Count > 0 || HasMeaningfulPropertyClips(tapeData.PropertyClips))
        {
            logger.LogDebug(
                "Pre-rendered video fast path rejected because cinematic tapes add scene changes: spawn clips={SpawnClipCount}, property clips={PropertyClipCount}.",
                tapeData.SpawnActorClips.Count,
                tapeData.PropertyClips.Count);
            return false;
        }

        IReadOnlyList<CinematicActor> videoOutputs = CinematicActorBuilder.SelectVideoOutputActors(scene.Actors);
        if (videoOutputs.Count != 1)
            return false;

        CinematicActor videoOutput = videoOutputs[0];
        CinematicActor[] visualActors = [.. scene.Actors.Where(IsSceneVisualActor)];
        if (visualActors.Any(actor => !string.Equals(actor.Key, videoOutput.Key, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogDebug(
                "Pre-rendered video fast path rejected because the scene contains {VisualActorCount} cinematic visual actor(s).",
                visualActors.Length);
            return false;
        }

        string[] pleoVideoPaths =
        [
            .. scene.Actors
                .Select(actor => actor.PleoVideoPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
        string sceneSourceVideoPath = NormalizeVideoPath(sourceFile.RelativePath);
        if (pleoVideoPaths.Length > 1)
            return false;

        if (pleoVideoPaths.Length == 1)
        {
            if (!MatchesSourceVideo(sourceFile, pleoVideoPaths[0]))
                return false;

            sceneSourceVideoPath = pleoVideoPaths[0];
        }

        CinematicRenderRuntime runtime = new(scene);
        ResolvedActorState state = CinematicActorStateResolver.ResolveActorState(
            videoOutput,
            runtime,
            PropertyClipIndex.Empty,
            frame: 0);
        if (!IsPlainOpaqueVideoOutput(state))
            return false;

        ProjectedQuad quad = CinematicGeometryProjector.ProjectQuad(
            CinematicGeometryProjector.CreatePleoVideoGeometry(),
            state,
            AnalysisOutputWidth,
            AnalysisOutputHeight,
            videoOutput.CustomAnchorX,
            videoOutput.CustomAnchorY,
            videoOutput.Anchor);
        if (!CoversOutputFrame(quad, AnalysisOutputWidth, AnalysisOutputHeight))
            return false;

        result = new CinematicSingleVideoScene(
            sceneSourceVideoPath,
            videoOutput.Key,
            quad.Bounds);
        return true;
    }

    private static CinematicTapeData ReadTapeDataOrEmpty(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicScene scene,
        double outputDurationSeconds,
        ILogger logger)
    {
        try
        {
            return CinematicTapeReader.ReadCinematicTapes(
                fileSystem,
                outputDurationSeconds,
                logger,
                scene: scene);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read cinematic tapes while checking pre-rendered video fast path.");
            return new CinematicTapeData(
                PropertyClips: [],
                SourceEvaluationClips: [],
                SpawnActorClips: [],
                RenderStartFrame: 0,
                MaterialTimeStartFrame: 0);
        }
    }

    private static bool HasMeaningfulPropertyClips(IEnumerable<PropertyClip> propertyClips) =>
        propertyClips.Any(clip =>
            clip.State.Transform != null ||
            clip.State.Material != null ||
            clip.State.LayerEnable != null ||
            clip.State.MaterialGraphic != null ||
            clip.State.Pivot != null ||
            clip.State.Animation != null);

    private static bool IsSceneVisualActor(CinematicActor actor)
    {
        if (!CinematicJustDanceActorFilter.ShouldRenderActor(actor))
            return false;

        if (CinematicActorBuilder.IsVideoOutputActor(actor))
            return true;

        return actor.VisualComponentTypeId != null ||
            actor.TexturePath != null ||
            actor.TexturePaths.Count > 0 ||
            actor.MaterialPath != null ||
            actor.MeshPath != null ||
            actor.ParticleTemplate != null ||
            actor.FxTemplate != null ||
            actor.AnimLightTemplate != null ||
            actor.GeometryOverride != null ||
            CinematicActorBuilder.UsesDynamicPleoTexture(actor) ||
            CinematicActorBuilder.UsesImplicitPleoTargetMaterial(actor);
    }

    private static bool MatchesSourceVideo(CookedFile sourceFile, string pleoVideoPath)
    {
        string normalizedSource = NormalizeVideoPath(sourceFile.RelativePath);
        string normalizedPleo = NormalizeVideoPath(pleoVideoPath);
        if (string.Equals(normalizedSource, normalizedPleo, StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(
            Path.GetFileNameWithoutExtension(normalizedSource),
            Path.GetFileNameWithoutExtension(normalizedPleo),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVideoPath(string path)
    {
        string normalized = CinematicNames.NormalizePath(path);
        return normalized.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }

    private static bool IsPlainOpaqueVideoOutput(ResolvedActorState state) =>
        Math.Abs(state.Alpha - 1.0f) <= 0.001f &&
        state.Tint.IsWhite &&
        Math.Abs(state.Angle) <= 0.001f &&
        Math.Abs(state.RotationX) <= 0.001f &&
        Math.Abs(state.RotationY) <= 0.001f &&
        !state.XFlipped;

    private static bool CoversOutputFrame(ProjectedQuad quad, int outputWidth, int outputHeight)
    {
        if (quad.Bounds.IsEmpty)
            return false;

        const float edgeTolerance = 1.5f;
        bool axisAligned =
            Math.Abs(quad.TopLeft.Y - quad.TopRight.Y) <= edgeTolerance &&
            Math.Abs(quad.BottomLeft.Y - quad.BottomRight.Y) <= edgeTolerance &&
            Math.Abs(quad.TopLeft.X - quad.BottomLeft.X) <= edgeTolerance &&
            Math.Abs(quad.TopRight.X - quad.BottomRight.X) <= edgeTolerance;
        if (!axisAligned)
            return false;

        return quad.Bounds.Left <= 1 &&
            quad.Bounds.Top <= 1 &&
            quad.Bounds.Right >= outputWidth - 1 &&
            quad.Bounds.Bottom >= outputHeight - 1;
    }
}