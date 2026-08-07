using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class CinematicPrerenderedVideoAnalyzer
{
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
            logger.LogDebug(ex, "Could not read the scene graph; the pre-rendered video fast path is unsafe.");
            return false;
        }

        if (!TryReadTapeData(fileSystem, scene, outputDurationSeconds, logger, out CinematicTapeData? tapeData))
            return false;

        return CinematicSingleVideoSceneAnalyzer.TryAnalyze(
            scene,
            tapeData,
            sourceFile.RelativePath,
            CinematicJustDanceActorFilter.ShouldRenderActor,
            logger,
            out result);
    }

    internal static bool MatchesSourceVideo(CookedFile sourceFile, string pleoVideoPath) =>
        CinematicSingleVideoSceneAnalyzer.MatchesSourceVideo(sourceFile.RelativePath, pleoVideoPath);

    private static bool TryReadTapeData(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicScene scene,
        double outputDurationSeconds,
        ILogger logger,
        [NotNullWhen(true)] out CinematicTapeData? tapeData)
    {
        try
        {
            tapeData = CinematicTapeReader.ReadCinematicTapes(
                fileSystem,
                outputDurationSeconds,
                logger,
                scene: scene);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read cinematic tapes; the pre-rendered video fast path is unsafe.");
            tapeData = null;
            return false;
        }
    }
}
