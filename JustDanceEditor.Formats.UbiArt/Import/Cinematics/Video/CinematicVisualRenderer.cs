using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.Cinematics.Video;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp.PixelFormats;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class CinematicVisualRenderer
{
    public static async Task RenderCutoutVideoAsync(
        JustDanceUbiArtFileSystem fileSystem,
        string tempFolder,
        string sourcePath,
        string destination,
        double outputDurationSeconds,
        TimelineStructureDocument timelineStructure,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight,
        int outputWidth,
        int outputHeight,
        ITextureService textureService,
        ILogger logger,
        IFileSystem io)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(timelineStructure);
        ArgumentNullException.ThrowIfNull(textureService);
        ArgumentNullException.ThrowIfNull(io);

        if (sourceWidth <= 0 ||
            visibleHeight <= 0 ||
            alphaHeight <= 0 ||
            outputWidth <= 0 ||
            outputHeight <= 0 ||
            outputDurationSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid cinematic cutout/video layout: source={sourceWidth}x{visibleHeight}+{alphaHeight}, output={outputWidth}x{outputHeight}, duration={outputDurationSeconds:0.###}s.");
        }

        Stopwatch totalStopwatch = Stopwatch.StartNew();

        try
        {
            Stopwatch stageStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Starting unified cinematic render to '{Destination}' at {OutputWidth}x{OutputHeight} for {Duration:0.###}s.",
                destination,
                outputWidth,
                outputHeight,
                outputDurationSeconds);

            CinematicScene scene = CinematicSceneReader.ReadSceneGraph(fileSystem, logger);
            if (scene.Actors.Count == 0)
            {
                throw new InvalidOperationException("No cinematic scene actors were loaded.");
            }

            CinematicTapeData tapeData = CinematicTapeReader.ReadCinematicTapes(
                fileSystem,
                outputDurationSeconds,
                logger,
                scene: scene);
            scene = CinematicSceneReader.AddSpawnedActors(
                scene,
                fileSystem,
                tapeData.SpawnActorClips,
                logger);

            Dictionary<string, MaterializedCinematicImage> images = await CinematicImageLoader.LoadActorImagesAsync(
                scene,
                fileSystem,
                textureService,
                logger);
            logger.LogInformation(
                "Cinematic scene/image load took {Elapsed:0.###}s: {SceneActorCount} scene actor(s), {ImageCount} decoded image(s).",
                stageStopwatch.Elapsed.TotalSeconds,
                scene.Actors.Count,
                images.Count);

            try
            {
                stageStopwatch.Restart();
                IReadOnlyList<RenderableCinematicActor> renderableActors = CinematicFrameRenderer.BuildRenderableActors(
                    scene,
                    images,
                    includeVideoOutput: true,
                    actorFilter: CinematicJustDanceActorFilter.ShouldRenderActor);

                int videoOutputActorCount = renderableActors.Count(actor => actor.RenderKind == CinematicRenderKind.PleoVideo);
                if (renderableActors.Count == 0 || videoOutputActorCount == 0)
                {
                    throw new InvalidOperationException(
                        $"Cinematic renderable actor discovery failed: scene actors={scene.Actors.Count}, decoded images={images.Count}, renderable actors={renderableActors.Count}, video output actors={videoOutputActorCount}.");
                }

                PropertyClipIndex clipIndex = CinematicPropertyClipIndexBuilder.Build(
                    tapeData.PropertyClips,
                    scene,
                    logger,
                    tapeData.SourceEvaluationClips);

                double materialTimeOffsetSeconds = ComputeMaterialTimeOffsetSeconds(timelineStructure, logger);
                CinematicTimeline timeline = CinematicTimeline.Create(
                    timelineStructure.Markers,
                    timelineStructure.StartBeat,
                    timelineStructure.VideoStartOffset);
                logger.LogInformation(
                    "Cinematic actor/tape preparation took {Elapsed:0.###}s: {RenderableCount} renderable actor(s), {VideoOutputCount} video output actor(s), {ClipCount} property clip(s).",
                    stageStopwatch.Elapsed.TotalSeconds,
                    renderableActors.Count,
                    videoOutputActorCount,
                    tapeData.PropertyClips.Count);

                int frameCount = GetRenderFrameCount(outputDurationSeconds);

                logger.LogInformation("Cinematic Pleo input will be decoded through an FFmpeg pipe.");
                stageStopwatch.Restart();
                await EncodeRawFramesAsync(
                    destination,
                    outputWidth,
                    outputHeight,
                    frameCount,
                    (stream, discardOutput) => CinematicFrameRenderer.RenderCompositeFrameSequenceToRawStream(
                        scene,
                        images,
                        clipIndex,
                        tempFolder,
                        stream,
                        sourceWidth,
                        visibleHeight,
                        alphaHeight,
                        outputWidth,
                        outputHeight,
                        timelineStructure.VideoStartOffset,
                        outputWidth,
                        outputHeight,
                        frameCount,
                        tapeData.RenderStartFrame,
                        tapeData.MaterialTimeStartFrame,
                        materialTimeOffsetSeconds,
                        logger,
                        timeline,
                        sourceFramesRawPath: null,
                        pleoVideoSourcePath: sourcePath,
                        actorFilter: actor => CinematicJustDanceActorFilter.ShouldRenderActor(actor.Actor),
                        discardOutput: discardOutput),
                    logger);

                logger.LogInformation(
                    "Cinematic raw render/encode stage took {Elapsed:0.###}s for {FrameCount} frame(s) ({FramesPerSecond:0.###} fps).",
                    stageStopwatch.Elapsed.TotalSeconds,
                    frameCount,
                    frameCount / Math.Max(0.001, stageStopwatch.Elapsed.TotalSeconds));

                logger.LogInformation(
                    "Rendered unified cinematic video with {RenderableCount} renderable actor(s), {VideoOutputCount} video output actor(s), {ImageCount} decoded image(s), {ClipCount} property clip(s), render start frame {RenderStartFrame}, material time start frame {MaterialTimeStartFrame}, material time offset {MaterialTimeOffsetSeconds}, {FrameCount} frame(s), and total time {Elapsed:0.###}s ({FramesPerSecond:0.###} fps).",
                    renderableActors.Count,
                    videoOutputActorCount,
                    images.Count,
                    tapeData.PropertyClips.Count,
                    tapeData.RenderStartFrame,
                    tapeData.MaterialTimeStartFrame,
                    materialTimeOffsetSeconds,
                    frameCount,
                    totalStopwatch.Elapsed.TotalSeconds,
                    frameCount / Math.Max(0.001, totalStopwatch.Elapsed.TotalSeconds));
                return;
            }
            finally
            {
                foreach (MaterializedCinematicImage image in images.Values)
                    image.Dispose();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to render unified cinematic video.");
            throw;
        }
    }

    public static async Task RenderSceneVideoAsync(
        JustDanceUbiArtFileSystem fileSystem,
        string tempFolder,
        string destination,
        double outputDurationSeconds,
        TimelineStructureDocument timelineStructure,
        int outputWidth,
        int outputHeight,
        ITextureService textureService,
        ILogger logger,
        IFileSystem io,
        IReadOnlyList<TapeVisit>? additionalTapeVisits = null,
        Func<RenderableCinematicActor, bool>? actorFilter = null,
        bool preserveAlpha = false,
        Bgra32? clearColorOverride = null,
        string? pleoVideoSourcePath = null,
        int pleoSourceWidth = 1,
        int pleoVisibleHeight = 1,
        int pleoAlphaHeight = 1,
        IReadOnlyList<CinematicExternalPleoTrack>? externalPleoTracks = null,
        Func<RenderableCinematicActor, RenderableCinematicActor>? actorMap = null,
        IPleoFrameProvider? pleoFrameProvider = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(timelineStructure);
        ArgumentNullException.ThrowIfNull(textureService);
        ArgumentNullException.ThrowIfNull(io);

        if (outputWidth <= 0 || outputHeight <= 0 || outputDurationSeconds <= 0)
            throw new InvalidOperationException($"Invalid cinematic scene render layout: output={outputWidth}x{outputHeight}, duration={outputDurationSeconds:0.###}s.");

        io.CreateDirectory(tempFolder);
        io.CreateDirectory(Path.GetDirectoryName(destination) ?? tempFolder);

        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch stageStopwatch = Stopwatch.StartNew();
        logger.LogInformation(
            "Starting cinematic scene-only render for '{SongName}' to '{Destination}' at {OutputWidth}x{OutputHeight} for {Duration:0.###}s.",
            fileSystem.SongName,
            destination,
            outputWidth,
            outputHeight,
            outputDurationSeconds);

        CinematicScene scene = CinematicSceneReader.ReadSceneGraph(fileSystem, logger);
        if (scene.Actors.Count == 0)
            throw new InvalidOperationException("No cinematic scene actors were loaded.");

        CinematicTapeData tapeData = CinematicTapeReader.ReadCinematicTapes(
            fileSystem,
            outputDurationSeconds,
            logger,
            additionalTapeVisits,
            scene);
        scene = CinematicSceneReader.AddSpawnedActors(
            scene,
            fileSystem,
            tapeData.SpawnActorClips,
            logger);

        Dictionary<string, MaterializedCinematicImage> images = await CinematicImageLoader.LoadActorImagesAsync(
            scene,
            fileSystem,
            textureService,
            logger);
        try
        {
            bool includeVideoOutput =
                !string.IsNullOrWhiteSpace(pleoVideoSourcePath) ||
                pleoFrameProvider != null;
            IReadOnlyList<RenderableCinematicActor> renderableActors = CinematicFrameRenderer.BuildRenderableActors(
                scene,
                images,
                includeVideoOutput,
                CinematicJustDanceActorFilter.ShouldRenderActor);
            if (actorFilter != null)
                renderableActors = [.. renderableActors.Where(actorFilter)];

            if (renderableActors.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Cinematic scene-only renderable actor discovery failed: scene actors={scene.Actors.Count}, decoded images={images.Count}.");
            }

            PropertyClipIndex clipIndex = CinematicPropertyClipIndexBuilder.Build(
                tapeData.PropertyClips,
                scene,
                logger,
                tapeData.SourceEvaluationClips);
            CinematicTimeline timeline = CinematicTimeline.Create(
                timelineStructure.Markers,
                timelineStructure.StartBeat,
                timelineStructure.VideoStartOffset);
            double materialTimeOffsetSeconds = ComputeMaterialTimeOffsetSeconds(timelineStructure, logger);
            int frameCount = GetRenderFrameCount(outputDurationSeconds);

            logger.LogInformation(
                "Cinematic scene-only preparation took {Elapsed:0.###}s: {ActorCount} actor(s), {RenderableCount} renderable actor(s), {ImageCount} decoded image(s), {ClipCount} property clip(s).",
                stageStopwatch.Elapsed.TotalSeconds,
                scene.Actors.Count,
                renderableActors.Count,
                images.Count,
                tapeData.PropertyClips.Count);

            stageStopwatch.Restart();
            await EncodeRawFramesAsync(
                destination,
                outputWidth,
                outputHeight,
                frameCount,
                (stream, discardOutput) => CinematicFrameRenderer.RenderCompositeFrameSequenceToRawStream(
                    scene,
                    images,
                    clipIndex,
                    tempFolder,
                    stream,
                    sourceWidth: pleoSourceWidth,
                    visibleHeight: pleoVisibleHeight,
                    alphaHeight: pleoAlphaHeight,
                    videoOutputWidth: outputWidth,
                    videoOutputHeight: outputHeight,
                    videoStartOffsetSeconds: timelineStructure.VideoStartOffset,
                    outputWidth,
                    outputHeight,
                    frameCount,
                    tapeData.RenderStartFrame,
                    tapeData.MaterialTimeStartFrame,
                    materialTimeOffsetSeconds,
                    logger,
                    timeline,
                    sourceFramesRawPath: null,
                    pleoVideoSourcePath: pleoVideoSourcePath,
                    actorFilter: actor =>
                        CinematicJustDanceActorFilter.ShouldRenderActor(actor.Actor) &&
                        (actorFilter?.Invoke(actor) ?? true),
                    clearColorOverride: clearColorOverride,
                    externalPleoTracks: externalPleoTracks,
                    actorMap: actorMap,
                    pleoFrameProviderOverride: pleoFrameProvider,
                    discardOutput: discardOutput),
                logger,
                preserveAlpha);

            logger.LogInformation(
                "Rendered cinematic scene-only video for '{SongName}' with {FrameCount} frame(s) in {Elapsed:0.###}s.",
                fileSystem.SongName,
                frameCount,
                totalStopwatch.Elapsed.TotalSeconds);
        }
        finally
        {
            foreach (MaterializedCinematicImage image in images.Values)
                image.Dispose();
        }
    }

    internal static int GetRenderFrameCount(double outputDurationSeconds)
    {
        return Math.Max(1, (int)Math.Ceiling(outputDurationSeconds * CinematicConstants.OutputFramesPerSecond));
    }

    private static double ComputeMaterialTimeOffsetSeconds(TimelineStructureDocument timelineStructure, ILogger logger)
    {
        if (timelineStructure.StartBeat >= 0)
            return 0.0;

        int markerIndex = Math.Abs(timelineStructure.StartBeat);
        if (timelineStructure.Markers.Count == 0 ||
            markerIndex >= timelineStructure.Markers.Count)
        {
            return 0.0;
        }

        double seconds = timelineStructure.Markers[markerIndex] / 48000.0;
        logger.LogDebug(
            "Using legacy material time offset {MaterialTimeOffsetSeconds:0.######}s from marker {MarkerIndex} / 48000.",
            seconds,
            markerIndex);
        return seconds;
    }

    private static string GetRawPipeCodecName() => "vp8";

    private static async Task EncodeRawFramesAsync(
        string destination,
        int outputWidth,
        int outputHeight,
        int frameCount,
        Action<Stream, bool> renderFrames,
        ILogger logger,
        bool preserveAlpha = false)
    {
        if (Path.GetExtension(destination).Equals(".speedtest", StringComparison.OrdinalIgnoreCase))
        {
            Stopwatch nullOutputStopwatch = Stopwatch.StartNew();
            renderFrames(Stream.Null, true);
            nullOutputStopwatch.Stop();
            double renderElapsedSeconds = nullOutputStopwatch.Elapsed.TotalSeconds;
            logger.LogInformation(
                "Rendered {FrameCount} cinematic frame(s) to null output in {RenderElapsed:0.###}s ({FramesPerSecond:0.###} fps).",
                frameCount,
                renderElapsedSeconds,
                frameCount / Math.Max(0.001, renderElapsedSeconds));
            logger.LogDebug("Discarded cinematic speedtest output for '{Destination}'.", destination);
            await Task.CompletedTask.ConfigureAwait(false);
            return;
        }

        string codecArgs = CreateRawPipeCodecArgs(preserveAlpha);
        string args =
            "-v error " +
            $"-f rawvideo -pix_fmt bgra -s {outputWidth}x{outputHeight} -r {CinematicConstants.OutputFramesPerSecond} -i pipe:0 " +
            codecArgs +
            $"-y \"{destination}\"";

        ProcessStartInfo startInfo = new(JdiFfmpegResolver.GetFfmpegPath(), args)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        StringBuilder stderr = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stderr.AppendLine(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start ffmpeg raw video writer.");

        process.BeginErrorReadLine();
        Stopwatch renderStopwatch = Stopwatch.StartNew();
        try
        {
            Stream outputStream = process.StandardInput.BaseStream;
            renderFrames(outputStream, false);
            outputStream.Flush();
            process.StandardInput.Close();
        }
        catch
        {
            TryKill(process);
            throw;
        }

        renderStopwatch.Stop();
        Stopwatch ffmpegDrainStopwatch = Stopwatch.StartNew();
        await process.WaitForExitAsync().ConfigureAwait(false);
        ffmpegDrainStopwatch.Stop();
        if (process.ExitCode != 0)
        {
            string error = stderr.ToString();
            throw new InvalidOperationException(
                $"ffmpeg raw video writer exited with code {process.ExitCode}: {error}");
        }

        logger.LogInformation(
            "Piped {FrameCount} raw cinematic frame(s) in {RenderElapsed:0.###}s ({FramesPerSecond:0.###} fps); ffmpeg drain took {FfmpegElapsed:0.###}s.",
            frameCount,
            renderStopwatch.Elapsed.TotalSeconds,
            frameCount / Math.Max(0.001, renderStopwatch.Elapsed.TotalSeconds),
            ffmpegDrainStopwatch.Elapsed.TotalSeconds);
        logger.LogDebug("Encoded raw cinematic frames directly to '{Destination}'.", destination);
    }

    private static string CreateRawPipeCodecArgs(bool preserveAlpha = false)
    {
        if (preserveAlpha)
        {
            int alphaThreads = GetRenderThreadCount();
            return $"-an -c:v ffv1 -level 3 -g 1 -slices {ChooseFfv1SliceCount(alphaThreads)} -slicecrc 0 -threads {alphaThreads} -pix_fmt bgra ";
        }

        string codec = GetRawPipeCodecName();
        int threads = GetRenderThreadCount();

        return codec switch
        {
            "ffv1" => $"-an -c:v ffv1 -level 3 -g 1 -slices {ChooseFfv1SliceCount(threads)} -slicecrc 0 -threads {threads} -pix_fmt bgra ",
            "huffyuv" => $"-an -c:v huffyuv -threads {threads} -pix_fmt bgra ",
            "raw" or "rawvideo" => "-an -c:v rawvideo -pix_fmt bgra ",
            _ => "-an -c:v libvpx -deadline realtime -cpu-used 8 " +
                $"-threads {threads} -lag-in-frames 0 -auto-alt-ref 0 " +
                "-crf 10 -b:v 12M -maxrate 18M -bufsize 24M -pix_fmt yuv420p "
        };
    }

    private static int ChooseFfv1SliceCount(int threads) =>
        threads >= 16 ? 16 :
        threads >= 12 ? 12 :
        threads >= 9 ? 9 :
        threads >= 6 ? 6 :
        4;

    private static int GetRenderThreadCount() => Math.Max(1, Environment.ProcessorCount);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static void TryDeleteDirectory(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to delete cinematic temp directory '{Path}'.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to delete cinematic temp directory '{Path}'.", path);
        }
    }

    private static void TryDeleteFile(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to delete cinematic temp file '{Path}'.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to delete cinematic temp file '{Path}'.", path);
        }
    }
}