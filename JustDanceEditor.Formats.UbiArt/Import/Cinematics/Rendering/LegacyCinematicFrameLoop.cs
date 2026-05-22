using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp.PixelFormats;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicFrameLoop
{
    private const double SlowFrameLogThresholdSeconds = 0.25;

    internal static void RenderCompositeFrame(
        RenderableCinematicActor[] actors,
        LegacyCinematicActor[] actorModels,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex,
        IReadOnlySet<string> staticActorKeys,
        StaticLayerCache? staticLayerCache,
        PleoFrameProvider pleoFrameProvider,
        string sourceFramesFolder,
        string outputFramesFolder,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight,
        int videoOutputWidth,
        int videoOutputHeight,
        double videoStartOffsetSeconds,
        CanvasBuffer outputCanvas,
        List<FrameDrawItem> frameItems,
        int frame,
        int renderStartFrame,
        int materialTimeStartFrame,
        double materialTimeOffsetSeconds,
        LegacyCinematicTimeline? timeline,
        LegacyCinematicRenderBackend renderBackend,
        ILogger logger)
    {
        LegacyCinematicFrameLoop.DrawCompositeFrame(
            actors,
            actorModels,
            runtime,
            clipIndex,
            staticActorKeys,
            staticLayerCache,
            pleoFrameProvider,
            sourceFramesFolder,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            videoOutputWidth,
            videoOutputHeight,
            videoStartOffsetSeconds,
            outputCanvas,
            frameItems,
            frame,
            renderStartFrame,
            materialTimeStartFrame,
            materialTimeOffsetSeconds,
            timeline,
            renderBackend,
            logger);

        string framePath = Path.Combine(outputFramesFolder, $"frame_{frame:D05}.png");
        LegacyCinematicRasterizer.SaveCanvasAsPng(outputCanvas, framePath);
    }

    internal static bool DrawCompositeFrame(
        RenderableCinematicActor[] actors,
        LegacyCinematicActor[] actorModels,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex,
        IReadOnlySet<string> staticActorKeys,
        StaticLayerCache? staticLayerCache,
        PleoFrameProvider pleoFrameProvider,
        string sourceFramesFolder,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight,
        int videoOutputWidth,
        int videoOutputHeight,
        double videoStartOffsetSeconds,
        CanvasBuffer outputCanvas,
        List<FrameDrawItem> frameItems,
        int frame,
        int renderStartFrame,
        int materialTimeStartFrame,
        double materialTimeOffsetSeconds,
        LegacyCinematicTimeline? timeline,
        LegacyCinematicRenderBackend renderBackend,
        ILogger logger,
        byte[]? rawOutputBuffer = null,
        bool skipOutputReadback = false)
    {
        Stopwatch frameStopwatch = Stopwatch.StartNew();
        Stopwatch stageStopwatch = Stopwatch.StartNew();
        double timelineFrame = LegacyCinematicCpuDraw.ComputeTimelineFrame(frame, renderStartFrame, timeline);
        double materialElapsedSeconds = LegacyCinematicCpuDraw.ComputeMaterialElapsedSeconds(
            frame,
            timelineFrame,
            renderStartFrame,
            materialTimeStartFrame,
            materialTimeOffsetSeconds,
            timeline);
        double timingSeconds = stageStopwatch.Elapsed.TotalSeconds;

        stageStopwatch.Restart();
        using PleoFrameSource? pleoFrame = LegacyCinematicCpuDraw.TryLoadPleoFrame(
            frame,
            pleoFrameProvider);
        double pleoSeconds = stageStopwatch.Elapsed.TotalSeconds;

        stageStopwatch.Restart();
        bool canvasCleared = !(renderBackend.Vulkan != null && (rawOutputBuffer != null || skipOutputReadback));
        if (canvasCleared)
            LegacyCinematicRasterizer.ClearCanvas(outputCanvas, LegacyCinematicRasterizer.CreateOpaqueBlackPixel());
        frameItems.Clear();
        double clearSeconds = stageStopwatch.Elapsed.TotalSeconds;

        stageStopwatch.Restart();
        IReadOnlyDictionary<string, ResolvedActorState> resolvedStates = LegacyCinematicRenderEngine.ResolveActorStates(
            actorModels,
            runtime,
            clipIndex,
            timelineFrame,
            timeline,
            renderStartFrame);
        IReadOnlyDictionary<string, LegacyCinematicCamera> camerasByRoot = LegacyCinematicCpuDraw.ResolveSceneCameras(
            actorModels,
            resolvedStates,
            outputCanvas.Width,
            outputCanvas.Height);
        double resolveSeconds = stageStopwatch.Elapsed.TotalSeconds;

        stageStopwatch.Restart();
        int particleActorCount = 0;
        double particleSeconds = 0.0;
        double slowestParticleSeconds = 0.0;
        string slowestParticleActorKey = string.Empty;
        foreach (RenderableCinematicActor actor in actors)
        {
            bool hasSource = LegacyCinematicCpuDraw.HasSourceImage(actor, pleoFrame);
            ResolvedActorState state = resolvedStates[actor.Actor.Key];
            LegacyCinematicCamera camera = LegacyCinematicCpuDraw.GetCameraForActor(actor.Actor, camerasByRoot, outputCanvas.Width, outputCanvas.Height);
            ProjectedQuad quad = LegacyCinematicRenderEngine.ProjectQuad(
                actor.Geometry,
                state,
                outputCanvas.Width,
                outputCanvas.Height,
                actor.Actor.CustomAnchorX,
                actor.Actor.CustomAnchorY,
                actor.Actor.Anchor,
                camera);

            if (!hasSource ||
                state.Alpha <= LegacyCinematicFrameRenderer.MaterialGraphicAlphaDrawThreshold)
            {
                continue;
            }

            double actorMaterialElapsedSeconds = LegacyCinematicActorTiming.GetMaterialElapsedSecondsForActor(actor.Actor, materialElapsedSeconds);
            double actorClipFrame = LegacyCinematicActorTimeOffsets.GetClipFrame(actor.Actor, timelineFrame, timeline, renderStartFrame);
            double actorParticleElapsedSeconds = LegacyCinematicActorTiming.GetSongSecondsForTapeFrame(actorClipFrame, timeline, renderStartFrame);
            double actorDefaultFxElapsedSeconds = LegacyCinematicActorTiming.GetDefaultFxElapsedSecondsForActor(
                actorClipFrame,
                timeline,
                renderStartFrame);
            CinematicMaterialRuntimeOverrides? materialOverrides = LegacyCinematicActorTiming.GetMaterialRuntimeOverrides(actor.Actor, clipIndex, actorClipFrame);
            long particleStartTicks = Stopwatch.GetTimestamp();
            if (LegacyCinematicParticleSimulator.TryAddParticleDrawItems(
                actor,
                state,
                materialOverrides,
                LegacyCinematicActorTiming.GetActiveFxPlaybacks(actor.Actor, clipIndex, actorClipFrame, timeline, renderStartFrame),
                actorParticleElapsedSeconds,
                actorDefaultFxElapsedSeconds,
                outputCanvas.Width,
                outputCanvas.Height,
                frameItems))
            {
                double actorParticleSeconds = Stopwatch.GetElapsedTime(particleStartTicks).TotalSeconds;
                particleSeconds += actorParticleSeconds;
                particleActorCount++;
                if (actorParticleSeconds > slowestParticleSeconds)
                {
                    slowestParticleSeconds = actorParticleSeconds;
                    slowestParticleActorKey = actor.Actor.Key;
                }

                continue;
            }

            if (quad.Bounds.Width <= 0 || quad.Bounds.Height <= 0)
                continue;

            frameItems.Add(new FrameDrawItem(
                actor,
                state,
                quad,
                materialOverrides,
                LegacyCinematicActorTiming.GetFrameUvOverride(actor, actorMaterialElapsedSeconds),
                MaterialElapsedSeconds: actorMaterialElapsedSeconds,
                CameraOverride: camera));
        }

        double buildSeconds = stageStopwatch.Elapsed.TotalSeconds;

        stageStopwatch.Restart();
        frameItems.Sort(LegacyCinematicRasterizer.CompareFrameDrawItems);
        double sortSeconds = stageStopwatch.Elapsed.TotalSeconds;

        stageStopwatch.Restart();
        bool rawOutputWritten = LegacyCinematicGpuDrawBuilder.DrawCompositeFrameItems(
            outputCanvas,
            frameItems,
            pleoFrame,
            materialElapsedSeconds,
            staticActorKeys,
            staticLayerCache,
            renderBackend,
            rawOutputBuffer,
            skipOutputReadback,
            canvasCleared,
            frame,
            logger);
        double drawSeconds = stageStopwatch.Elapsed.TotalSeconds;
        double totalSeconds = frameStopwatch.Elapsed.TotalSeconds;
        if (totalSeconds >= SlowFrameLogThresholdSeconds)
        {
            int particleItemCount = 0;
            for (int i = 0; i < frameItems.Count; i++)
            {
                if (frameItems[i].ParticleIndex >= 0)
                    particleItemCount++;
            }

            logger.LogWarning(
                "Slow legacy cinematic frame {Frame}: total={Total:0.###}s timing={Timing:0.###}s pleo={Pleo:0.###}s clear={Clear:0.###}s resolve={Resolve:0.###}s build={Build:0.###}s particles={Particles:0.###}s sort={Sort:0.###}s draw={Draw:0.###}s items={FrameItems} particleItems={ParticleItems} particleActors={ParticleActors} slowestParticle='{SlowestParticleActor}' slowestParticleTime={SlowestParticleTime:0.###}s rawByRenderer={RawByRenderer}.",
                frame,
                totalSeconds,
                timingSeconds,
                pleoSeconds,
                clearSeconds,
                resolveSeconds,
                buildSeconds,
                particleSeconds,
                sortSeconds,
                drawSeconds,
                frameItems.Count,
                particleItemCount,
                particleActorCount,
                slowestParticleActorKey,
                slowestParticleSeconds,
                rawOutputWritten);
        }

        return rawOutputWritten;
    }

    internal static int GetRenderThreadCount() => Math.Max(1, Environment.ProcessorCount);

    internal static StaticLayerCache? CreateStaticLayerCache(int outputWidth, int outputHeight) =>
        new(outputWidth, outputHeight);

    internal static byte[] CopyPixelsToRentedBuffer(CanvasBuffer image, out int length)
    {
        length = checked(image.Width * image.Height * 4);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        LegacyCinematicFrameLoop.CopyPixelsToBuffer(image, buffer, length);

        return buffer;
    }

    internal static void CopyPixelsToBuffer(CanvasBuffer image, byte[] buffer, int length)
    {
        if (buffer.Length < length)
            throw new ArgumentException("Raw output buffer is smaller than the frame.", nameof(buffer));

        MemoryMarshal.AsBytes(image.Pixels.AsSpan())[..length].CopyTo(buffer);
    }

    internal static void WriteRawFramesInOrder(
        BlockingCollection<RenderedRawFrame> renderedFrames,
        Stream outputStream,
        int frameCount,
        ILogger? logger = null)
    {
        Stopwatch progressStopwatch = Stopwatch.StartNew();
        int writtenFrames = 0;
        SortedDictionary<int, RenderedRawFrame> pending = [];
        int nextFrame = 0;
        foreach (RenderedRawFrame frame in renderedFrames.GetConsumingEnumerable())
        {
            pending.Add(frame.Frame, frame);
            while (pending.Remove(nextFrame, out RenderedRawFrame ready))
            {
                long writeStartTicks = Stopwatch.GetTimestamp();
                outputStream.Write(ready.Buffer, 0, ready.Length);
                double writeSeconds = Stopwatch.GetElapsedTime(writeStartTicks).TotalSeconds;
                ArrayPool<byte>.Shared.Return(ready.Buffer);
                nextFrame++;
                writtenFrames++;
                if (writeSeconds >= SlowFrameLogThresholdSeconds)
                {
                    logger?.LogWarning(
                        "Legacy cinematic ffmpeg stdin write for frame {Frame} took {Elapsed:0.###}s; pending out-of-order frames={PendingFrameCount}.",
                        ready.Frame,
                        writeSeconds,
                        pending.Count);
                }

                if (writtenFrames == frameCount || writtenFrames % 250 == 0)
                {
                    double elapsedSeconds = Math.Max(0.001, progressStopwatch.Elapsed.TotalSeconds);
                    logger?.LogInformation(
                        "Legacy cinematic wrote {WrittenFrameCount}/{FrameCount} raw frame(s) to ffmpeg stdin ({FramesPerSecond:0.###} fps).",
                        writtenFrames,
                        frameCount,
                        writtenFrames / elapsedSeconds);
                }
            }
        }

        while (nextFrame < frameCount && pending.Remove(nextFrame, out RenderedRawFrame ready))
        {
            long writeStartTicks = Stopwatch.GetTimestamp();
            outputStream.Write(ready.Buffer, 0, ready.Length);
            double writeSeconds = Stopwatch.GetElapsedTime(writeStartTicks).TotalSeconds;
            ArrayPool<byte>.Shared.Return(ready.Buffer);
            nextFrame++;
            writtenFrames++;
            if (writeSeconds >= SlowFrameLogThresholdSeconds)
            {
                logger?.LogWarning(
                    "Legacy cinematic ffmpeg stdin write for frame {Frame} took {Elapsed:0.###}s while draining pending frames.",
                    ready.Frame,
                    writeSeconds);
            }
        }

        foreach (RenderedRawFrame frame in pending.Values)
            ArrayPool<byte>.Shared.Return(frame.Buffer);

        if (nextFrame != frameCount)
            throw new InvalidOperationException($"Raw frame writer received {nextFrame} of {frameCount} rendered frame(s).");
    }
}