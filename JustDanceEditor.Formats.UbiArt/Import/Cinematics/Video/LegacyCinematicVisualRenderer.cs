using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class LegacyCinematicVisualRenderer
{
    private const int MaximumRenderFrameCount = 5000;

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
        int? frameLimit,
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
                $"Invalid legacy cinematic cutout/video layout: source={sourceWidth}x{visibleHeight}+{alphaHeight}, output={outputWidth}x{outputHeight}, duration={outputDurationSeconds:0.###}s.");
        }

        Stopwatch totalStopwatch = Stopwatch.StartNew();

        try
        {
            Stopwatch stageStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Starting unified legacy cinematic render to '{Destination}' at {OutputWidth}x{OutputHeight} for {Duration:0.###}s.",
                destination,
                outputWidth,
                outputHeight,
                outputDurationSeconds);

            LegacyCinematicScene scene = LegacyCinematicSceneReader.ReadSceneGraph(fileSystem, logger);
            if (scene.Actors.Count == 0)
            {
                throw new InvalidOperationException("No legacy cinematic scene actors were loaded.");
            }

            Dictionary<string, MaterializedCinematicImage> images = await LegacyCinematicImageLoader.LoadActorImagesAsync(
                scene,
                fileSystem,
                textureService,
                logger);
            logger.LogInformation(
                "Legacy cinematic scene/image load took {Elapsed:0.###}s: {SceneActorCount} scene actor(s), {ImageCount} decoded image(s).",
                stageStopwatch.Elapsed.TotalSeconds,
                scene.Actors.Count,
                images.Count);

            try
            {
                stageStopwatch.Restart();
                IReadOnlyList<RenderableCinematicActor> renderableActors = LegacyCinematicFrameRenderer.BuildRenderableActors(
                    scene,
                    images,
                    includeVideoOutput: true);

                int videoOutputActorCount = renderableActors.Count(actor => actor.RenderKind == CinematicRenderKind.PleoVideo);
                if (renderableActors.Count == 0 || videoOutputActorCount == 0)
                {
                    throw new InvalidOperationException(
                        $"Legacy cinematic renderable actor discovery failed: scene actors={scene.Actors.Count}, decoded images={images.Count}, renderable actors={renderableActors.Count}, video output actors={videoOutputActorCount}.");
                }

                LegacyCinematicTapeData tapeData = LegacyCinematicTapeReader.ReadCinematicTapes(
                    fileSystem,
                    outputDurationSeconds,
                    logger);

                PropertyClipIndex clipIndex = LegacyCinematicTapeReader.BuildPropertyClipIndex(
                    tapeData.PropertyClips,
                    scene,
                    logger,
                    tapeData.SourceEvaluationClips);

                double materialTimeOffsetSeconds = ComputeMaterialTimeOffsetSeconds(timelineStructure, logger);
                LegacyCinematicTimeline timeline = LegacyCinematicTimeline.Create(
                    timelineStructure.Markers,
                    timelineStructure.StartBeat,
                    timelineStructure.VideoStartOffset);
                logger.LogInformation(
                    "Legacy cinematic actor/tape preparation took {Elapsed:0.###}s: {RenderableCount} renderable actor(s), {VideoOutputCount} video output actor(s), {ClipCount} property clip(s).",
                    stageStopwatch.Elapsed.TotalSeconds,
                    renderableActors.Count,
                    videoOutputActorCount,
                    tapeData.PropertyClips.Count);

                int uncappedFrameCount = Math.Max(1, (int)Math.Ceiling(outputDurationSeconds * LegacyCinematicConstants.OutputFramesPerSecond));
                int maximumFrameCount = frameLimit.GetValueOrDefault(MaximumRenderFrameCount);
                if (maximumFrameCount <= 0)
                    throw new InvalidOperationException($"Invalid legacy cinematic frame limit {maximumFrameCount}.");

                int frameCount = Math.Min(uncappedFrameCount, maximumFrameCount);
                if (frameCount != uncappedFrameCount)
                {
                    logger.LogWarning(
                        "Capping legacy cinematic render from {UncappedFrameCount} to {FrameCount} frame(s).",
                        uncappedFrameCount,
                        frameCount);
                }

                logger.LogInformation("Legacy cinematic Pleo input will be decoded through an FFmpeg pipe.");
                stageStopwatch.Restart();
                await EncodeRawFramesAsync(
                    destination,
                    outputWidth,
                    outputHeight,
                    frameCount,
                    stream => LegacyCinematicFrameRenderer.RenderCompositeFrameSequenceToRawStream(
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
                        pleoVideoSourcePath: sourcePath),
                    logger);

                logger.LogInformation(
                    "Legacy cinematic raw render/encode stage took {Elapsed:0.###}s for {FrameCount} frame(s) ({FramesPerSecond:0.###} fps).",
                    stageStopwatch.Elapsed.TotalSeconds,
                    frameCount,
                    frameCount / Math.Max(0.001, stageStopwatch.Elapsed.TotalSeconds));

                logger.LogInformation(
                    "Rendered unified legacy cinematic video with {RenderableCount} renderable actor(s), {VideoOutputCount} video output actor(s), {ImageCount} decoded image(s), {ClipCount} property clip(s), render start frame {RenderStartFrame}, material time start frame {MaterialTimeStartFrame}, material time offset {MaterialTimeOffsetSeconds}, {FrameCount} frame(s), and total time {Elapsed:0.###}s ({FramesPerSecond:0.###} fps).",
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
            logger.LogError(ex, "Failed to render unified legacy cinematic video.");
            throw;
        }
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
        Action<Stream> renderFrames,
        ILogger logger)
    {
        string codecArgs = CreateRawPipeCodecArgs();
        string args =
            "-v error " +
            $"-f rawvideo -pix_fmt bgra -s {outputWidth}x{outputHeight} -r {LegacyCinematicConstants.OutputFramesPerSecond} -i pipe:0 " +
            codecArgs +
            $"-y \"{destination}\"";

        ProcessStartInfo startInfo = new(FindFfmpegExecutable(), args)
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
            throw new InvalidOperationException("Failed to start ffmpeg raw video encoder.");

        process.BeginErrorReadLine();
        Stopwatch renderStopwatch = Stopwatch.StartNew();
        try
        {
            Stream outputStream = process.StandardInput.BaseStream;
            renderFrames(outputStream);
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
                $"ffmpeg raw video encoder exited with code {process.ExitCode}: {error}");
        }

        logger.LogInformation(
            "Piped {FrameCount} raw legacy cinematic frame(s) in {RenderElapsed:0.###}s ({FramesPerSecond:0.###} fps); ffmpeg drain took {FfmpegElapsed:0.###}s.",
            frameCount,
            renderStopwatch.Elapsed.TotalSeconds,
            frameCount / Math.Max(0.001, renderStopwatch.Elapsed.TotalSeconds),
            ffmpegDrainStopwatch.Elapsed.TotalSeconds);
        logger.LogDebug("Encoded raw legacy cinematic frames directly to '{Destination}'.", destination);
    }

    private static string CreateRawPipeCodecArgs()
    {
        string codec = GetRawPipeCodecName();
        int threads = GetRenderThreadCount();

        return codec switch
        {
            "ffv1" => $"-an -c:v ffv1 -level 3 -g 1 -slices {ChooseFfv1SliceCount(threads)} -slicecrc 0 -threads {threads} -pix_fmt bgra ",
            "huffyuv" => $"-an -c:v huffyuv -threads {threads} -pix_fmt bgra ",
            "raw" or "rawvideo" => "-an -c:v rawvideo -pix_fmt bgra ",
            _ => "-an -c:v libvpx -deadline realtime -cpu-used 8 " +
                $"-threads {threads} -lag-in-frames 0 -auto-alt-ref 0 " +
                "-crf 22 -b:v 0 -pix_fmt yuv420p "
        };
    }

    private static int ChooseFfv1SliceCount(int threads) =>
        threads >= 16 ? 16 :
        threads >= 12 ? 12 :
        threads >= 9 ? 9 :
        threads >= 6 ? 6 :
        4;

    private static string FindFfmpegExecutable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            "ffmpeg"
        ];

        foreach (string candidate in candidates)
        {
            if (candidate.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) || File.Exists(candidate))
                return candidate;
        }

        return "ffmpeg";
    }

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
            logger.LogDebug(ex, "Failed to delete legacy cinematic temp directory '{Path}'.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to delete legacy cinematic temp directory '{Path}'.", path);
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
            logger.LogDebug(ex, "Failed to delete legacy cinematic temp file '{Path}'.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to delete legacy cinematic temp file '{Path}'.", path);
        }
    }
}