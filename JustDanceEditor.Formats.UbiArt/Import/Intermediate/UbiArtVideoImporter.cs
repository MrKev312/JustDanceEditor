using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Assets;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class UbiArtVideoImporter
{
    private const int CinematicOutputHeight = 1080;

    public static async Task ImportMasterAsync(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        IntermediateSongPackage package,
        string destinationFolder,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io,
        bool renderVideoSpeedTest)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(songData);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(textureService);

        io.CreateDirectory(destinationFolder);
        string destination = io.Combine(destinationFolder, $"master{GetRenderedVideoExtension(renderVideoSpeedTest)}");
        if (songData.LegacyMashup != null)
        {
            await MashupVideoRenderer.RenderAsync(
                fileSystem,
                songData,
                package.TimelineStructure,
                fileSystem.TempFolders.MapFolder,
                destination,
                textureService,
                logger,
                io);
            return;
        }

        CookedFile? sourceFile = UbiArtVideoFileSelector.FindPreferredVideoFile(
            fileSystem,
            allowContentSongFallback: !songData.IsCommunityMashup);
        if (sourceFile == null)
        {
            if (songData.IsCommunityMashup)
            {
                string remixType = songData.IsStarRemix ? "Star Remix" : "community mashup";
                logger.LogWarning(
                    "No authored {RemixType} video was found for '{SongName}'. " +
                    "It may have been supplied by Ubisoft's unavailable streaming service; exporting the song without a master video.",
                    remixType,
                    songData.Name);
                return;
            }

            logger.LogWarning("No video file found in UbiArt input; skipping video copy.");
            return;
        }

        logger.LogInformation("Selected UbiArt master video '{VideoPath}'.", sourceFile.RelativePath);
        await ImportMasterFileAsync(fileSystem, sourceFile, songData, package, destination, logger, textureService, io, renderVideoSpeedTest);
    }

    private static async Task ImportMasterFileAsync(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        JDUbiArtSong songData,
        IntermediateSongPackage package,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io,
        bool renderVideoSpeedTest)
    {
        string? tempSource = null;
        try
        {
            tempSource = await MaterializeCookedFileAsync(fileSystem, sourceFile, io);
            if (songData.IsCommunityMashup)
            {
                await RenderCommunityMashupAsync(
                    fileSystem,
                    songData,
                    package,
                    tempSource,
                    destination,
                    logger,
                    textureService,
                    io);
                return;
            }

            GraphVideoImportResult graphResult = await GraphVideoTransformPlanner.TryApplyAsync(
                fileSystem,
                sourceFile,
                package,
                tempSource,
                destination,
                logger);
            if (graphResult == GraphVideoImportResult.AppliedTransform)
                return;

            if (graphResult == GraphVideoImportResult.DirectSource)
            {
                await CopyCookedFileAsync(fileSystem, sourceFile, destination);
                logger.LogInformation("Copied graph-proven direct master video into the intermediate package.");
                return;
            }

            LegacyCutoutVideoLayout? cutout = await TryGetLegacyCutoutLayoutAsync(fileSystem, sourceFile, tempSource, logger);
            if (cutout != null)
            {
                string tempFolder = Path.GetDirectoryName(tempSource) ?? fileSystem.TempFolders.MapFolder;
                double duration = IntermediateVideoTiming.GetCinematicRenderDurationSeconds(package, cutout.Value.DurationSeconds);
                string renderDestination = renderVideoSpeedTest
                    ? Path.ChangeExtension(destination, GetRenderedVideoExtension(true))
                    : destination;
                await CinematicVisualRenderer.RenderCutoutVideoAsync(
                    fileSystem,
                    tempFolder,
                    tempSource,
                    renderDestination,
                    duration,
                    package.TimelineStructure,
                    cutout.Value.Width,
                    cutout.Value.VisibleHeight,
                    cutout.Value.AlphaHeight,
                    cutout.Value.OutputWidth,
                    cutout.Value.OutputHeight,
                    textureService,
                    logger,
                    io);
                logger.LogInformation("Imported legacy cutout video into intermediate package with unified cinematic renderer.");
                return;
            }

            await RenderFullGraphAsync(
                fileSystem,
                package,
                tempSource,
                destination,
                logger,
                textureService,
                io);
        }
        finally
        {
            IntermediateAssetPaths.TryDeleteFile(tempSource, io);
        }
    }

    private static async Task RenderCommunityMashupAsync(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        IntermediateSongPackage package,
        string tempSource,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        JdiVideoInfo? videoInfo = await JdiVideoConverter.TryInspectVideoAsync(tempSource);
        if (videoInfo == null || videoInfo.Width <= 0 || videoInfo.Height <= 0)
            throw new InvalidDataException("The CMU source video could not be inspected.");

        CinematicScene scene = CinematicSceneReader.ReadCommunityMashupSceneGraph(fileSystem, logger);
        string activationLabel = songData.IsStarRemix ? "ENTER_FORWARD_DWS" : "ENTER_FORWARD";
        IReadOnlyList<KevInc.UbiArt.Cinematics.Timeline.TapeVisit> activationVisits =
            CinematicTapeLauncherPlanner.FindSequenceVisits(fileSystem, scene, activationLabel, logger);
        if (activationVisits.Count == 0)
        {
            string activationPath = Path.Combine(
                "world",
                "jd2015",
                "_ui",
                "screens",
                "cmu_ingame",
                "animations",
                $"{activationLabel.ToLowerInvariant()}.tape");
            if (fileSystem.GetFilePath(activationPath, out _))
                activationVisits = [new KevInc.UbiArt.Cinematics.Timeline.TapeVisit(activationPath, 0)];
        }

        double duration = IntermediateVideoTiming.GetCinematicRenderDurationSeconds(
            package,
            videoInfo.Duration.TotalSeconds);
        CommunityMashupRenderPlan renderPlan = CommunityMashupRenderPlanner.Create(
            fileSystem,
            songData,
            scene,
            activationVisits,
            (int)Math.Ceiling(duration * CinematicConstants.TapeTicksPerSecond),
            logger);

        logger.LogInformation(
            "Rendering community mashup through the JD generic CMU stage with {ActivationTapeCount} {ActivationLabel} tape(s).",
            activationVisits.Count,
            activationLabel);
        bool usesLegacyUiVideoSlot =
            fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015;
        await CinematicVisualRenderer.RenderSceneVideoAsync(
            fileSystem,
            Path.GetDirectoryName(tempSource) ?? fileSystem.TempFolders.MapFolder,
            destination,
            duration,
            package.TimelineStructure,
            outputWidth: 1920,
            outputHeight: CinematicOutputHeight,
            textureService,
            logger,
            io,
            additionalTapeVisits: renderPlan.TapeVisits,
            pleoVideoSourcePath: tempSource,
            pleoSourceWidth: videoInfo.Width,
            pleoVisibleHeight: videoInfo.Height,
            pleoAlphaHeight: 0,
            actorFilter: actor =>
                !usesLegacyUiVideoSlot || !CinematicActorBuilder.IsVideoOutputActor(actor.Actor),
            actorMap: actor =>
                usesLegacyUiVideoSlot &&
                string.Equals(actor.Actor.Key, "video", StringComparison.OrdinalIgnoreCase)
                    ? actor with
                    {
                        Image = null,
                        RenderKind = CinematicRenderKind.PleoVideo
                    }
                    : actor,
            sceneOverride: renderPlan.Scene,
            additionalPropertyClips: renderPlan.PropertyClips);
    }

    private static async Task<string> MaterializeCookedFileAsync(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        IFileSystem io)
    {
        string tempFolder = io.Combine(fileSystem.TempFolders.MapFolder, "video");
        io.CreateDirectory(tempFolder);
        string extension = Path.GetExtension(sourceFile.RelativePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".webm";
        string tempPath = io.Combine(tempFolder, $"source_{Guid.NewGuid():N}{extension}");
        await using Stream source = fileSystem.GetFileStream(sourceFile);
        await using FileStream destination = File.Open(tempPath, FileMode.Create, FileAccess.Write);
        await source.CopyToAsync(destination);
        return tempPath;
    }

    private static async Task CopyCookedFileAsync(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        string destination)
    {
        await using Stream source = fileSystem.GetFileStream(sourceFile);
        await using FileStream output = File.Open(destination, FileMode.Create, FileAccess.Write);
        await source.CopyToAsync(output);
    }

    private static async Task RenderFullGraphAsync(
        JustDanceUbiArtFileSystem fileSystem,
        IntermediateSongPackage package,
        string tempSource,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        JdiVideoInfo? videoInfo = await JdiVideoConverter.TryInspectVideoAsync(tempSource);
        if (videoInfo == null || videoInfo.Width <= 0 || videoInfo.Height <= 0)
            throw new InvalidDataException("The source video could not be inspected for full scene-graph rendering.");

        double duration = IntermediateVideoTiming.GetCinematicRenderDurationSeconds(
            package,
            videoInfo.Duration.TotalSeconds);
        await CinematicVisualRenderer.RenderSceneVideoAsync(
            fileSystem,
            Path.GetDirectoryName(tempSource) ?? fileSystem.TempFolders.MapFolder,
            destination,
            duration,
            package.TimelineStructure,
            outputWidth: 1920,
            outputHeight: CinematicOutputHeight,
            textureService,
            logger,
            io,
            pleoVideoSourcePath: tempSource,
            pleoSourceWidth: videoInfo.Width,
            pleoVisibleHeight: videoInfo.Height,
            pleoAlphaHeight: 0);
        logger.LogInformation("Rendered the complete UbiArt scene graph because a direct video transform could not be proven safe.");
    }

    private static async Task<LegacyCutoutVideoLayout?> TryGetLegacyCutoutLayoutAsync(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        string sourcePath,
        ILogger logger)
    {
        try
        {
            JdiVideoInfo? videoInfo = await JdiVideoConverter.TryInspectVideoAsync(sourcePath);
            if (videoInfo == null || videoInfo.Width <= 0 || videoInfo.Height <= 0)
                return null;

            CinematicScene scene = CinematicSceneReader.ReadSceneGraph(fileSystem, logger);
            if (!CinematicPleoLayoutAnalyzer.UsesStackedAlphaSource(
                    scene,
                    fileSystem,
                    logger,
                    CinematicJustDanceActorFilter.ShouldRenderActor) ||
                !CinematicPleoLayoutAnalyzer.TrySplitStackedFrame(
                    videoInfo.Width,
                    videoInfo.Height,
                    out int visibleHeight,
                    out int alphaHeight))
            {
                return null;
            }

            const int outputWidth = 1920;
            const int outputHeight = CinematicOutputHeight;

            logger.LogInformation(
                "Detected graph-authored stacked-alpha Pleo video '{Video}' ({Width}x{Height}); rendering visible {VisibleWidth}x{VisibleHeight} plus {AlphaHeight}-pixel mask through the scene graph at {OutputWidth}x{OutputHeight}.",
                Path.GetFileName(sourceFile.RelativePath),
                videoInfo.Width,
                videoInfo.Height,
                videoInfo.Width,
                visibleHeight,
                alphaHeight,
                outputWidth,
                outputHeight);
            return new(
                videoInfo.Width,
                visibleHeight,
                alphaHeight,
                outputWidth,
                outputHeight,
                videoInfo.Duration.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not inspect legacy video '{Video}' for stacked alpha layout.", sourceFile.RelativePath);
            return null;
        }
    }

    private static string GetRenderedVideoExtension(bool renderVideoSpeedTest) =>
        renderVideoSpeedTest ? ".speedtest" : ".webm";

    private readonly record struct LegacyCutoutVideoLayout(
        int Width,
        int VisibleHeight,
        int AlphaHeight,
        int OutputWidth,
        int OutputHeight,
        double DurationSeconds);
}
