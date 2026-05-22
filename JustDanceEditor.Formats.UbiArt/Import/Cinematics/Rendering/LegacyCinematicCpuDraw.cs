using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicCpuDraw
{
    internal static Rectangle ComputeQuadClip(CanvasBuffer canvas, ProjectedQuad quad, out float maxY)
    {
        Rectangle canvasRectangle = new(0, 0, canvas.Width, canvas.Height);
        Rectangle clippedRectangle = Rectangle.Intersect(canvasRectangle, quad.Bounds);
        maxY = MathF.Max(MathF.Max(quad.TopLeft.Y, quad.TopRight.Y), MathF.Max(quad.BottomRight.Y, quad.BottomLeft.Y));
        if (LegacyCinematicRasterizer.ShouldClampCanvasBottomEdge(clippedRectangle.Bottom, maxY, canvas.Height))
        {
            clippedRectangle = Rectangle.FromLTRB(
                clippedRectangle.Left,
                clippedRectangle.Top,
                clippedRectangle.Right,
                canvas.Height);
        }

        return clippedRectangle;
    }

    internal static void DrawStaticRun(
        CanvasBuffer outputCanvas,
        IReadOnlyList<FrameDrawItem> frameItems,
        int start,
        int end,
        StaticLayerCache staticLayerCache)
    {
        if (start >= end)
            return;

        CachedStaticLayer layer = staticLayerCache.GetOrCreate(frameItems, start, end);
        LegacyCinematicCpuDraw.DrawCachedStaticLayer(outputCanvas, layer);
    }

    internal static void DrawFrameItem(
        CanvasBuffer outputCanvas,
        FrameDrawItem item,
        PleoFrameSource? pleoFrame,
        double materialElapsedSeconds)
    {
        if (item.Actor.RenderKind == CinematicRenderKind.PleoVideo)
        {
            if (pleoFrame != null)
                LegacyCinematicRasterizer.DrawPleoVideoImage(outputCanvas, pleoFrame, item.State, item.Quad);

            return;
        }

        MaterializedCinematicImage? image = LegacyCinematicActorTiming.GetItemImage(item);
        if (image != null)
        {
            LegacyCinematicRasterizer.DrawActorImage(
                outputCanvas,
                item.Actor,
                image,
                LegacyCinematicActorTiming.GetItemGeometry(item),
                item.State,
                item.Quad,
                LegacyCinematicActorTiming.GetItemMaterialElapsedSeconds(item, materialElapsedSeconds),
                item.MaterialOverrides,
                item.UvOverride,
                item.CameraOverride);
        }
    }

    internal static void DrawCachedStaticLayer(CanvasBuffer canvas, CachedStaticLayer layer)
    {
        if (layer.Bounds.Width <= 0 || layer.Bounds.Height <= 0)
            return;

        for (int y = layer.Bounds.Top; y < layer.Bounds.Bottom; y++)
        {
            Span<Bgra32> canvasRow = canvas.GetRowSpan(y);
            ReadOnlySpan<Bgra32> layerRow = layer.Image.GetRowSpan(y);
            for (int x = layer.Bounds.Left; x < layer.Bounds.Right; x++)
            {
                Bgra32 source = layerRow[x];
                if (source.A == 0)
                    continue;

                if (source.A == 255)
                {
                    canvasRow[x] = source;
                    continue;
                }

                LegacyCinematicRasterizer.BlendPixel(ref canvasRow[x], source, 1.0f);
            }
        }
    }

    internal static bool HasSourceImage(RenderableCinematicActor actor, PleoFrameSource? pleoFrame) =>
        actor.RenderKind == CinematicRenderKind.PleoVideo
            ? pleoFrame != null
            : actor.Image?.Image != null;

    internal static int GetSourceWidth(RenderableCinematicActor actor, PleoFrameSource? pleoFrame) =>
        actor.RenderKind == CinematicRenderKind.PleoVideo
            ? pleoFrame?.SourceWidth ?? 0
            : actor.Image?.Width ?? 0;

    internal static int GetSourceHeight(RenderableCinematicActor actor, PleoFrameSource? pleoFrame) =>
        actor.RenderKind == CinematicRenderKind.PleoVideo
            ? pleoFrame?.VisibleHeight ?? 0
            : actor.Image?.Height ?? 0;

    internal static double ComputeTimelineFrame(
        int outputFrame,
        int renderStartFrame,
        LegacyCinematicTimeline? timeline) =>
        timeline?.GetTapeFrameForOutputFrame(outputFrame, renderStartFrame) ??
        (renderStartFrame + (outputFrame * LegacyCinematicConstants.TapeTicksPerSecond / (double)LegacyCinematicConstants.OutputFramesPerSecond));

    internal static IReadOnlyDictionary<string, LegacyCinematicCamera> ResolveSceneCameras(
        IEnumerable<LegacyCinematicActor> actors,
        IReadOnlyDictionary<string, ResolvedActorState> resolvedStates,
        int outputWidth,
        int outputHeight)
    {
        Dictionary<string, LegacyCinematicCamera> cameras = new(StringComparer.OrdinalIgnoreCase);
        double aspectRatio = outputHeight <= 0 ? 1.0 : outputWidth / (double)outputHeight;
        foreach (LegacyCinematicActor actor in actors)
        {
            if (!LegacyCinematicCpuDraw.IsFixedCameraActor(actor) ||
                actor.Path.Count == 0 ||
                !resolvedStates.TryGetValue(actor.Key, out ResolvedActorState? state))
            {
                continue;
            }

            double cameraOffsetZ = LegacyCinematicCpuDraw.IsLegacyGraphCameraDummy(actor) ? 0.0 : FixedCameraDefaultOffsetZ;
            LegacyCinematicCamera camera = new(
                state.PositionX,
                state.PositionY,
                state.PositionZ + cameraOffsetZ,
                LegacyCinematicCamera.DefaultFovYRadians,
                aspectRatio);

            if (LegacyCinematicCpuDraw.IsMainJustDanceRuntimeCamera(actor))
            {
                cameras[GlobalMainCameraKey] = camera;
                continue;
            }

            if (actor.Path.Count > 1)
            {
                string rootKey = LegacyCinematicNames.NormalizeKey([actor.Path[0]]);
                cameras[rootKey] = camera;
            }
        }

        return cameras;
    }

    internal static LegacyCinematicCamera GetCameraForActor(
        LegacyCinematicActor actor,
        IReadOnlyDictionary<string, LegacyCinematicCamera> camerasByRoot,
        int outputWidth,
        int outputHeight)
    {
        if (actor.Path.Count > 0)
        {
            string rootKey = LegacyCinematicNames.NormalizeKey([actor.Path[0]]);
            if (camerasByRoot.TryGetValue(rootKey, out LegacyCinematicCamera camera))
                return camera;
        }

        if (camerasByRoot.TryGetValue(GlobalMainCameraKey, out LegacyCinematicCamera globalCamera))
            return globalCamera;

        return LegacyCinematicCamera.CreateDefault(outputWidth, outputHeight);
    }

    internal const string GlobalMainCameraKey = "__global_main_camera";
    internal const double FixedCameraDefaultOffsetZ = 10.0;

    internal static bool IsFixedCameraActor(LegacyCinematicActor actor) =>
        LegacyCinematicCpuDraw.IsLegacyGraphCameraDummy(actor) ||
        LegacyCinematicCpuDraw.IsJustDanceRuntimeCamera(actor) ||
        actor.TemplatePath.Contains("fixedcamera", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLegacyGraphCameraDummy(LegacyCinematicActor actor) =>
        string.Equals(actor.Name, "Camera_JD_Dummy", StringComparison.OrdinalIgnoreCase);

    internal static bool IsMainJustDanceRuntimeCamera(LegacyCinematicActor actor) =>
        actor.Path.Count == 1 &&
        string.Equals(actor.Name, "Camera_JD", StringComparison.OrdinalIgnoreCase);

    internal static bool IsJustDanceRuntimeCamera(LegacyCinematicActor actor) =>
        actor.Path.Count == 1 &&
        (string.Equals(actor.Name, "Camera_JD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actor.Name, "Camera_JD_Remote", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actor.Name, "Camera_JD_WDF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(actor.Name, "Camera_JD_WDF_Remote", StringComparison.OrdinalIgnoreCase));

    internal static double ComputeMaterialElapsedSeconds(
        int outputFrame,
        double timelineFrame,
        int renderStartFrame,
        int materialTimeStartFrame,
        double materialTimeOffsetSeconds,
        LegacyCinematicTimeline? timeline)
    {
        string? mode = null;
        if (string.Equals(mode, "timeline", StringComparison.OrdinalIgnoreCase) && timeline != null)
            return timeline.GetSongSecondsForTapeFrame(timelineFrame, renderStartFrame) + LegacyCinematicCpuDraw.GetMaterialTimeOffsetSeconds();

        if (string.Equals(mode, "video-marker", StringComparison.OrdinalIgnoreCase))
        {
            return materialTimeOffsetSeconds +
                (outputFrame / (double)LegacyCinematicConstants.OutputFramesPerSecond) +
                LegacyCinematicCpuDraw.GetMaterialTimeOffsetSeconds();
        }

        if (string.Equals(mode, "tape-offset", StringComparison.OrdinalIgnoreCase))
        {
            if (timeline != null)
            {
                double currentSeconds = timeline.GetSongSecondsForTapeFrame(timelineFrame, renderStartFrame);
                double startSeconds = timeline.GetSongSecondsForTapeFrame(materialTimeStartFrame, renderStartFrame);
                return Math.Max(0, currentSeconds - startSeconds);
            }

            return Math.Max(0, timelineFrame - materialTimeStartFrame) / LegacyCinematicConstants.TapeTicksPerSecond;
        }

        if (string.Equals(mode, "tape", StringComparison.OrdinalIgnoreCase))
        {
            if (timeline != null)
                return timeline.GetSongSecondsForTapeFrame(timelineFrame, renderStartFrame);

            return Math.Max(0, timelineFrame) / LegacyCinematicConstants.TapeTicksPerSecond;
        }

        if (timeline != null)
            return timeline.GetSongSecondsForTapeFrame(timelineFrame, renderStartFrame) + LegacyCinematicCpuDraw.GetMaterialTimeOffsetSeconds();

        return materialTimeOffsetSeconds +
            (outputFrame / (double)LegacyCinematicConstants.OutputFramesPerSecond) +
            LegacyCinematicCpuDraw.GetMaterialTimeOffsetSeconds();
    }

    internal static double GetMaterialTimeOffsetSeconds()
    {
        return 0.0;
    }

    internal static PleoFrameSource? TryLoadPleoFrame(int outputFrame, PleoFrameProvider provider) =>
        provider.TryLoad(outputFrame);

    internal static int GetPleoFrameOffset(int outputFrame)
    {
        int offset = LegacyCinematicCpuDraw.GetBasePleoFrameOffset(outputFrame);
        return offset;
    }

    internal static int GetBasePleoFrameOffset(int outputFrame)
    {
        return -1;
    }

    internal static PleoUvBounds ComputePleoAlphaBounds(
        IReadOnlyList<Bgra32> pixels,
        int stackWidth,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight)
    {
        int minX = sourceWidth;
        int minY = alphaHeight;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < alphaHeight; y++)
        {
            int rowOffset = (visibleHeight + y) * stackWidth;
            for (int x = 0; x < sourceWidth; x++)
            {
                Bgra32 mask = pixels[rowOffset + x];
                double luma = (mask.R * 0.2126) + (mask.G * 0.7152) + (mask.B * 0.0722);
                if (LegacyCinematicRasterizer.ToByte((luma - 16.0) * 255.0 / 219.0) == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
            return PleoUvBounds.Empty;

        float maxSourceX = Math.Max(1, sourceWidth - 1);
        float maxSourceY = Math.Max(1, alphaHeight - 1);
        return new PleoUvBounds(
            Math.Max(0.0f, (minX - 1) / maxSourceX),
            Math.Max(0.0f, (minY - 1) / maxSourceY),
            Math.Min(1.0f, (maxX + 1) / maxSourceX),
            Math.Min(1.0f, (maxY + 1) / maxSourceY));
    }

    internal static Bgra32[] CreatePleoCutoutPixels(
        IReadOnlyList<Bgra32> pixels,
        int stackWidth,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight)
    {
        Bgra32[] cutoutPixels = new Bgra32[checked(sourceWidth * visibleHeight)];
        float alphaMaxY = Math.Max(1, alphaHeight - 1);
        float visibleMaxY = Math.Max(1, visibleHeight - 1);
        for (int y = 0; y < visibleHeight; y++)
        {
            float alphaY = y * alphaMaxY / visibleMaxY;
            int alphaY0 = (int)MathF.Floor(alphaY);
            int alphaY1 = Math.Min(alphaY0 + 1, alphaHeight - 1);
            float alphaT = alphaY - alphaY0;
            int colorRow = y * stackWidth;
            int alphaRow0 = (visibleHeight + alphaY0) * stackWidth;
            int alphaRow1 = (visibleHeight + alphaY1) * stackWidth;
            int targetRow = y * sourceWidth;

            for (int x = 0; x < sourceWidth; x++)
            {
                Bgra32 source = pixels[colorRow + x];
                Bgra32 mask0 = pixels[alphaRow0 + x];
                Bgra32 mask1 = pixels[alphaRow1 + x];
                double luma0 = (mask0.R * 0.2126) + (mask0.G * 0.7152) + (mask0.B * 0.0722);
                double luma1 = (mask1.R * 0.2126) + (mask1.G * 0.7152) + (mask1.B * 0.0722);
                source.A = LegacyCinematicRasterizer.ToByte((LegacyCinematicRasterizer.LerpDouble(luma0, luma1, alphaT) - 16.0) * 255.0 / 219.0);
                cutoutPixels[targetRow + x] = source;
            }
        }

        return cutoutPixels;
    }

    internal static Rectangle ProjectPleoAlphaBounds(
        PleoUvBounds bounds,
        Vector2 origin,
        Vector2 axisU,
        Vector2 axisV,
        Rectangle canvasRectangle)
    {
        Vector2 topLeft = origin + (axisU * bounds.Left) + (axisV * bounds.Top);
        Vector2 topRight = origin + (axisU * bounds.Right) + (axisV * bounds.Top);
        Vector2 bottomRight = origin + (axisU * bounds.Right) + (axisV * bounds.Bottom);
        Vector2 bottomLeft = origin + (axisU * bounds.Left) + (axisV * bounds.Bottom);
        float minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X));
        float minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y));
        float maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X));
        float maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y));
        Rectangle projected = Rectangle.FromLTRB(
            (int)MathF.Floor(minX),
            (int)MathF.Floor(minY),
            (int)MathF.Ceiling(maxX),
            (int)MathF.Ceiling(maxY));
        return Rectangle.Intersect(canvasRectangle, projected);
    }
}