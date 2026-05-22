using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using Microsoft.Extensions.Logging;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicFrameRenderer
{
    internal const int GfxBlendCopy = 1;
    internal const int GfxBlendAlpha = 2;
    internal const int GfxBlendAlphaPremult = 3;
    internal const int GfxBlendAdd = 6;
    internal const int GfxBlendAddAlpha = 7;
    internal const int GfxBlendSubAlpha = 8;
    internal const int GfxBlendSub = 9;
    internal const int GfxBlendMul = 10;
    internal const int GfxBlendAlphaMul = 11;
    internal const int GfxBlendInvAlphaMul = 12;
    internal const int GfxBlendMul2X = 17;
    internal const int GfxBlendScreen = 21;
    internal const int SplineInterpolationLinear = 0;
    internal const int SplineInterpolationSpline = 1;
    internal const int SplineInterpolationBezier = 2;
    internal const int SplineInterpolationConstant = 3;
    internal const int SplineInterpolationBezierStandard = 4;
    internal const float MaterialGraphicAlphaDrawThreshold = 1.0f / 255.0f;

    public static IReadOnlyList<RenderableCinematicActor> BuildRenderableActors(
        LegacyCinematicScene scene,
        IReadOnlyDictionary<string, MaterializedCinematicImage> images,
        bool includeVideoOutput = false) =>
        LegacyCinematicActorBuilder.BuildRenderableActors(scene, images, includeVideoOutput);

    public static void RenderFrameSequence(
        LegacyCinematicScene scene,
        IReadOnlyDictionary<string, MaterializedCinematicImage> images,
        PropertyClipIndex clipIndex,
        string framesFolder,
        CinematicLayerPlane plane,
        int outputWidth,
        int outputHeight,
        int frameCount,
        int renderStartFrame,
        ILogger logger,
        int materialTimeStartFrame = 0,
        double materialTimeOffsetSeconds = 0.0,
        LegacyCinematicTimeline? timeline = null)
    {
        Directory.CreateDirectory(framesFolder);
        RenderableCinematicActor[] actors = [.. LegacyCinematicActorBuilder.BuildRenderableActors(scene, images)
            .Where(actor => actor.Plane == plane)];
        LegacyCinematicRenderRuntime runtime = new(scene);

        bool clearToBlack = plane == CinematicLayerPlane.Background;
        CanvasBuffer outputCanvas = new(outputWidth, outputHeight);

        for (int frame = 0; frame < frameCount; frame++)
        {
            double timelineFrame = LegacyCinematicCpuDraw.ComputeTimelineFrame(frame, renderStartFrame, timeline);
            double materialElapsedSeconds = LegacyCinematicCpuDraw.ComputeMaterialElapsedSeconds(
                frame,
                timelineFrame,
                renderStartFrame,
                materialTimeStartFrame,
                materialTimeOffsetSeconds,
                timeline);
            if (clearToBlack)
                LegacyCinematicRasterizer.ClearCanvas(outputCanvas, LegacyCinematicRasterizer.CreateOpaqueBlackPixel());
            else
                LegacyCinematicRasterizer.ClearCanvas(outputCanvas);

            List<FrameDrawItem> frameItems = [];
            IReadOnlyDictionary<string, ResolvedActorState> resolvedStates = LegacyCinematicRenderEngine.ResolveActorStates(
                scene.Actors,
                runtime,
                clipIndex,
                timelineFrame,
                timeline,
                renderStartFrame);
            IReadOnlyDictionary<string, LegacyCinematicCamera> camerasByRoot = LegacyCinematicCpuDraw.ResolveSceneCameras(
                scene.Actors,
                resolvedStates,
                outputWidth,
                outputHeight);
            foreach (RenderableCinematicActor actor in actors)
            {
                ResolvedActorState state = resolvedStates[actor.Actor.Key];
                LegacyCinematicCamera camera = LegacyCinematicCpuDraw.GetCameraForActor(actor.Actor, camerasByRoot, outputWidth, outputHeight);
                ProjectedQuad quad = LegacyCinematicRenderEngine.ProjectQuad(
                    actor.Geometry,
                    state,
                    outputWidth,
                    outputHeight,
                    actor.Actor.CustomAnchorX,
                    actor.Actor.CustomAnchorY,
                    actor.Actor.Anchor,
                    camera);
                if (state.Alpha <= MaterialGraphicAlphaDrawThreshold)
                    continue;

                double actorMaterialElapsedSeconds = LegacyCinematicActorTiming.GetMaterialElapsedSecondsForActor(actor.Actor, materialElapsedSeconds);
                double actorClipFrame = LegacyCinematicActorTimeOffsets.GetClipFrame(actor.Actor, timelineFrame, timeline, renderStartFrame);
                double actorParticleElapsedSeconds = LegacyCinematicActorTiming.GetSongSecondsForTapeFrame(actorClipFrame, timeline, renderStartFrame);
                double actorDefaultFxElapsedSeconds = LegacyCinematicActorTiming.GetDefaultFxElapsedSecondsForActor(
                    actorClipFrame,
                    timeline,
                    renderStartFrame);
                CinematicMaterialRuntimeOverrides? materialOverrides = LegacyCinematicActorTiming.GetMaterialRuntimeOverrides(actor.Actor, clipIndex, actorClipFrame);
                if (LegacyCinematicParticleSimulator.TryAddParticleDrawItems(
                    actor,
                    state,
                    materialOverrides,
                    LegacyCinematicActorTiming.GetActiveFxPlaybacks(actor.Actor, clipIndex, actorClipFrame, timeline, renderStartFrame),
                    actorParticleElapsedSeconds,
                    actorDefaultFxElapsedSeconds,
                    outputWidth,
                    outputHeight,
                    frameItems))
                {
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

            frameItems.Sort(LegacyCinematicRasterizer.CompareFrameDrawItems);

            foreach (FrameDrawItem item in frameItems)
            {
                MaterializedCinematicImage? image = LegacyCinematicActorTiming.GetItemImage(item);
                if (image != null)
                    LegacyCinematicRasterizer.DrawActorImage(outputCanvas, item.Actor, image, LegacyCinematicActorTiming.GetItemGeometry(item), item.State, item.Quad, LegacyCinematicActorTiming.GetItemMaterialElapsedSeconds(item, materialElapsedSeconds), item.MaterialOverrides, item.UvOverride, item.CameraOverride);
            }

            string framePath = Path.Combine(framesFolder, $"frame_{frame:D05}.png");
            LegacyCinematicRasterizer.SaveCanvasAsPng(outputCanvas, framePath);
        }

        logger.LogDebug(
            "Rendered {FrameCount} legacy cinematic {Plane} frame(s) with {ActorCount} actor(s).",
            frameCount,
            plane,
            actors.Length);
    }

    public static void RenderCompositeFrameSequence(
        LegacyCinematicScene scene,
        IReadOnlyDictionary<string, MaterializedCinematicImage> images,
        PropertyClipIndex clipIndex,
        string sourceFramesFolder,
        string outputFramesFolder,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight,
        int videoOutputWidth,
        int videoOutputHeight,
        double videoStartOffsetSeconds,
        int outputWidth,
        int outputHeight,
        int frameCount,
        int renderStartFrame,
        int materialTimeStartFrame,
        double materialTimeOffsetSeconds,
        ILogger logger,
        LegacyCinematicTimeline? timeline = null,
        string? sourceFramesRawPath = null,
        string? pleoVideoSourcePath = null,
        bool discardOutput = false)
    {
        Directory.CreateDirectory(outputFramesFolder);
        RenderableCinematicActor[] actors = [.. LegacyCinematicActorBuilder.BuildRenderableActors(scene, images, includeVideoOutput: true)];
        LegacyCinematicActor[] actorModels = [.. scene.Actors];
        LegacyCinematicRenderRuntime runtime = new(scene);
        IReadOnlySet<string> staticActorKeys = LegacyCinematicActorTiming.ComputeStaticActorKeys(actors, runtime, clipIndex);
        bool canRenderInParallel = true;
        using StaticLayerCache? staticLayerCache = LegacyCinematicFrameLoop.CreateStaticLayerCache(outputWidth, outputHeight);
        using PleoFrameProvider pleoFrameProvider = PleoFrameProvider.Create(
            sourceFramesFolder,
            sourceFramesRawPath,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            pleoVideoSourcePath);
        using LegacyCinematicRenderBackend renderBackend = LegacyCinematicRenderBackend.Create(outputWidth, outputHeight, out string backendFallbackReason);
        LegacyCinematicFrameRenderer.LogRenderBackend(logger, renderBackend, backendFallbackReason);
        if (renderBackend.Vulkan != null)
            canRenderInParallel = false;

        if (canRenderInParallel)
        {
            Parallel.ForEach(
                Partitioner.Create(0, frameCount),
                new ParallelOptions { MaxDegreeOfParallelism = LegacyCinematicFrameLoop.GetRenderThreadCount() },
                range =>
                {
                    CanvasBuffer outputCanvas = new(outputWidth, outputHeight);
                    List<FrameDrawItem> frameItems = new(actors.Length);

                    for (int frame = range.Item1; frame < range.Item2; frame++)
                    {
                        LegacyCinematicFrameLoop.RenderCompositeFrame(
                            actors,
                            actorModels,
                            runtime,
                            clipIndex,
                            staticActorKeys,
                            staticLayerCache,
                            pleoFrameProvider,
                            sourceFramesFolder,
                            outputFramesFolder,
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
                    }
                });
        }
        else
        {
            CanvasBuffer outputCanvas = new(outputWidth, outputHeight);
            List<FrameDrawItem> frameItems = new(actors.Length);

            foreach (int frame in Enumerable.Range(0, frameCount))
            {
                LegacyCinematicFrameLoop.RenderCompositeFrame(
                    actors,
                    actorModels,
                    runtime,
                    clipIndex,
                        staticActorKeys,
                        staticLayerCache,
                        pleoFrameProvider,
                        sourceFramesFolder,
                    outputFramesFolder,
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
            }
        }

        logger.LogDebug(
            "Rendered {FrameCount} unified legacy cinematic frame(s) with {ActorCount} actor(s).",
            frameCount,
            actors.Length);
    }

    public static void RenderCompositeFrameSequenceToRawStream(
        LegacyCinematicScene scene,
        IReadOnlyDictionary<string, MaterializedCinematicImage> images,
        PropertyClipIndex clipIndex,
        string sourceFramesFolder,
        Stream outputStream,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight,
        int videoOutputWidth,
        int videoOutputHeight,
        double videoStartOffsetSeconds,
        int outputWidth,
        int outputHeight,
        int frameCount,
        int renderStartFrame,
        int materialTimeStartFrame,
        double materialTimeOffsetSeconds,
        ILogger logger,
        LegacyCinematicTimeline? timeline = null,
        string? sourceFramesRawPath = null,
        string? pleoVideoSourcePath = null,
        bool discardOutput = false)
    {
        RenderableCinematicActor[] actors = [.. LegacyCinematicActorBuilder.BuildRenderableActors(scene, images, includeVideoOutput: true)];
        LegacyCinematicActor[] actorModels = [.. scene.Actors];
        LegacyCinematicRenderRuntime runtime = new(scene);
        IReadOnlySet<string> staticActorKeys = LegacyCinematicActorTiming.ComputeStaticActorKeys(actors, runtime, clipIndex);
        int renderThreads = LegacyCinematicFrameLoop.GetRenderThreadCount();
        using StaticLayerCache? staticLayerCache = LegacyCinematicFrameLoop.CreateStaticLayerCache(outputWidth, outputHeight);
        using PleoFrameProvider pleoFrameProvider = PleoFrameProvider.Create(
            sourceFramesFolder,
            sourceFramesRawPath,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            pleoVideoSourcePath);
        using LegacyCinematicRenderBackend renderBackend = LegacyCinematicRenderBackend.Create(outputWidth, outputHeight, out string backendFallbackReason);
        LegacyCinematicFrameRenderer.LogRenderBackend(logger, renderBackend, backendFallbackReason);
        if (renderBackend.Vulkan != null)
            renderThreads = 1;
        using BlockingCollection<RenderedRawFrame> renderedFrames = new(Math.Max(2, renderThreads));
        int rawFrameLength = checked(outputWidth * outputHeight * 4);
        int completedFrames = 0;
        Stopwatch progressStopwatch = Stopwatch.StartNew();

        Task? writer = discardOutput
            ? null
            : Task.Run(() => LegacyCinematicFrameLoop.WriteRawFramesInOrder(renderedFrames, outputStream, frameCount, logger));
        try
        {
            Parallel.ForEach(
                Partitioner.Create(0, frameCount),
                new ParallelOptions { MaxDegreeOfParallelism = renderThreads },
                range =>
                {
                    CanvasBuffer outputCanvas = new(outputWidth, outputHeight);
                    List<FrameDrawItem> frameItems = new(actors.Length);

                    for (int frame = range.Item1; frame < range.Item2; frame++)
                    {
                        byte[]? rawBuffer = discardOutput
                            ? null
                            : ArrayPool<byte>.Shared.Rent(rawFrameLength);
                        try
                        {
                            bool rawBufferFilledByRenderer = LegacyCinematicFrameLoop.DrawCompositeFrame(
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
                                logger,
                                rawBuffer,
                                discardOutput);

                            if (!discardOutput)
                            {
                                if (rawBuffer == null)
                                    throw new InvalidOperationException("Raw output buffer was not rented.");

                                if (!rawBufferFilledByRenderer)
                                {
                                    LegacyCinematicFrameLoop.CopyPixelsToBuffer(outputCanvas, rawBuffer, rawFrameLength);
                                }

                                long queueStartTicks = Stopwatch.GetTimestamp();
                                renderedFrames.Add(new RenderedRawFrame(frame, rawBuffer, rawFrameLength));
                                double queueSeconds = Stopwatch.GetElapsedTime(queueStartTicks).TotalSeconds;
                                if (queueSeconds >= 0.25)
                                {
                                    logger.LogWarning(
                                        "Legacy cinematic renderer waited {Elapsed:0.###}s to enqueue raw frame {Frame}; ffmpeg/write side is applying backpressure.",
                                        queueSeconds,
                                        frame);
                                }

                                rawBuffer = null;
                            }

                            int completed = Interlocked.Increment(ref completedFrames);
                            if (completed == frameCount || completed % 250 == 0)
                            {
                                double elapsedSeconds = Math.Max(0.001, progressStopwatch.Elapsed.TotalSeconds);
                                logger.LogInformation(
                                    "Legacy cinematic rendered {CompletedFrameCount}/{FrameCount} raw frame(s) ({FramesPerSecond:0.###} fps).",
                                    completed,
                                    frameCount,
                                    completed / elapsedSeconds);
                            }
                        }
                        finally
                        {
                            if (rawBuffer != null)
                                ArrayPool<byte>.Shared.Return(rawBuffer);
                        }
                    }
                });
        }
        finally
        {
            renderedFrames.CompleteAdding();
        }

        writer?.GetAwaiter().GetResult();

        logger.LogDebug(
            "Rendered {FrameCount} unified legacy cinematic raw frame(s) with {ActorCount} actor(s).",
            frameCount,
            actors.Length);
    }

    private static void LogRenderBackend(
        ILogger logger,
        LegacyCinematicRenderBackend renderBackend,
        string backendFallbackReason)
    {
        if (renderBackend.Kind == LegacyCinematicRenderBackendKind.Vulkan)
        {
            logger.LogInformation("Legacy cinematic renderer using Vulkan backend.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(backendFallbackReason))
        {
            logger.LogWarning(
                "Legacy cinematic renderer using {Backend} backend; Vulkan was unavailable: {Reason}",
                renderBackend.Name,
                backendFallbackReason);
            return;
        }

        logger.LogInformation("Legacy cinematic renderer using {Backend} backend.", renderBackend.Name);
    }
}