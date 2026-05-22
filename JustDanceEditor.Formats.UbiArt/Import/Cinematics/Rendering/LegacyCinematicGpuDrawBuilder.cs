using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Vulkan;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicGpuDrawBuilder
{
    internal static bool DrawCompositeFrameItems(
        CanvasBuffer outputCanvas,
        IReadOnlyList<FrameDrawItem> frameItems,
        PleoFrameSource? pleoFrame,
        double materialElapsedSeconds,
        IReadOnlySet<string> staticActorKeys,
        StaticLayerCache? staticLayerCache,
        LegacyCinematicRenderBackend renderBackend,
        byte[]? rawOutputBuffer = null,
        bool skipOutputReadback = false,
        bool canvasCleared = true,
        int frame = -1,
        ILogger? logger = null)
    {
        if (renderBackend.Vulkan != null &&
            LegacyCinematicGpuDrawBuilder.TryDrawCompositeFrameItemsWithGpu(
                outputCanvas,
                frameItems,
                pleoFrame,
                materialElapsedSeconds,
                staticActorKeys,
                staticLayerCache,
                renderBackend.Vulkan,
            rawOutputBuffer,
            skipOutputReadback,
                frame,
                logger,
                out bool rawOutputWritten))
        {
            return rawOutputWritten;
        }

        if (staticLayerCache == null || staticActorKeys.Count == 0)
        {
            if (!canvasCleared)
                LegacyCinematicRasterizer.ClearCanvas(outputCanvas, LegacyCinematicRasterizer.CreateOpaqueBlackPixel());

            for (int i = 0; i < frameItems.Count; i++)
                LegacyCinematicCpuDraw.DrawFrameItem(outputCanvas, frameItems[i], pleoFrame, materialElapsedSeconds);

            return false;
        }

        int staticRunStart = 0;
        if (!canvasCleared)
            LegacyCinematicRasterizer.ClearCanvas(outputCanvas, LegacyCinematicRasterizer.CreateOpaqueBlackPixel());

        for (int index = 0; index < frameItems.Count; index++)
        {
            FrameDrawItem item = frameItems[index];
            if (staticActorKeys.Contains(item.Actor.Actor.Key))
                continue;

            LegacyCinematicCpuDraw.DrawStaticRun(outputCanvas, frameItems, staticRunStart, index, staticLayerCache);
            LegacyCinematicCpuDraw.DrawFrameItem(outputCanvas, item, pleoFrame, materialElapsedSeconds);
            staticRunStart = index + 1;
        }

        LegacyCinematicCpuDraw.DrawStaticRun(outputCanvas, frameItems, staticRunStart, frameItems.Count, staticLayerCache);
        return false;
    }

    internal static bool TryDrawCompositeFrameItemsWithGpu(
        CanvasBuffer outputCanvas,
        IReadOnlyList<FrameDrawItem> frameItems,
        PleoFrameSource? pleoFrame,
        double materialElapsedSeconds,
        IReadOnlySet<string> staticActorKeys,
        StaticLayerCache? staticLayerCache,
        LegacyCinematicVulkanBackend vulkanBackend,
        byte[]? rawOutputBuffer,
        bool skipOutputReadback,
        int frame,
        ILogger? logger,
        out bool rawOutputWritten)
    {
        rawOutputWritten = false;
        List<LegacyCinematicGpuDrawItem> gpuItems = new(frameItems.Count);
        if (!LegacyCinematicGpuDrawBuilder.TryBuildGpuDrawItems(
            outputCanvas,
            frameItems,
            pleoFrame,
            materialElapsedSeconds,
            staticActorKeys,
            staticLayerCache,
            gpuItems,
            out string unsupportedReason))
        {
            logger?.LogWarning(
                "Legacy cinematic frame {Frame} fell back to CPU rendering while building Vulkan draw items: {Reason}",
                frame,
                unsupportedReason);
            return false;
        }

        if (!vulkanBackend.TryRenderFrame(outputCanvas, gpuItems, out string failureReason, rawOutputBuffer, skipOutputReadback, frame))
        {
            logger?.LogWarning(
                "Legacy cinematic frame {Frame} fell back to CPU rendering after Vulkan render failed: {Reason}",
                frame,
                failureReason);
            return false;
        }

        rawOutputWritten = rawOutputBuffer != null && !skipOutputReadback;
        return true;
    }

    internal static bool TryBuildGpuDrawItems(
        CanvasBuffer outputCanvas,
        IReadOnlyList<FrameDrawItem> frameItems,
        PleoFrameSource? pleoFrame,
        double materialElapsedSeconds,
        IReadOnlySet<string> staticActorKeys,
        StaticLayerCache? staticLayerCache,
        List<LegacyCinematicGpuDrawItem> gpuItems,
        out string unsupportedReason)
    {
        unsupportedReason = string.Empty;
        if (staticLayerCache != null)
        {
            int staticRunStart = 0;
            for (int index = 0; index < frameItems.Count; index++)
            {
                FrameDrawItem item = frameItems[index];
                if (staticActorKeys.Contains(item.Actor.Actor.Key))
                    continue;

                LegacyCinematicGpuDrawBuilder.AddGpuStaticRun(outputCanvas, frameItems, staticRunStart, index, staticLayerCache, gpuItems);
                if (!LegacyCinematicGpuDrawBuilder.TryAddGpuDrawItemsForFrameItem(outputCanvas, item, pleoFrame, materialElapsedSeconds, gpuItems, out unsupportedReason))
                    return false;

                staticRunStart = index + 1;
            }

            LegacyCinematicGpuDrawBuilder.AddGpuStaticRun(outputCanvas, frameItems, staticRunStart, frameItems.Count, staticLayerCache, gpuItems);
            return true;
        }

        foreach (FrameDrawItem item in frameItems)
        {
            if (!LegacyCinematicGpuDrawBuilder.TryAddGpuDrawItemsForFrameItem(outputCanvas, item, pleoFrame, materialElapsedSeconds, gpuItems, out unsupportedReason))
                return false;
        }

        return true;
    }

    internal static bool TryAddGpuDrawItemsForFrameItem(
        CanvasBuffer outputCanvas,
        FrameDrawItem item,
        PleoFrameSource? pleoFrame,
        double materialElapsedSeconds,
        List<LegacyCinematicGpuDrawItem> gpuItems,
        out string unsupportedReason)
    {
        unsupportedReason = string.Empty;
        if (item.Actor.RenderKind == CinematicRenderKind.PleoVideo)
        {
            if (pleoFrame == null || pleoFrame.AlphaBounds.IsEmpty)
                return true;

            Rectangle pleoClip = LegacyCinematicCpuDraw.ComputeQuadClip(outputCanvas, item.Quad, out float pleoMaxY);
            if (pleoClip.Width <= 0 || pleoClip.Height <= 0)
                return true;

            Bgra32[] pixels = pleoFrame.IsCutout ? pleoFrame.CutoutPixels : pleoFrame.Pixels;
            int textureHeight = pleoFrame.IsCutout
                ? pleoFrame.VisibleHeight
                : pleoFrame.VisibleHeight + pleoFrame.AlphaHeight;
            LegacyCinematicUvSampler sampler = pleoFrame.IsCutout
                ? LegacyCinematicUvSampler.Default
                : LegacyCinematicUvSampler.Default with
                {
                    AddressModeU = TextureAddressMode.Clamp,
                    AddressModeV = TextureAddressMode.Clamp,
                    TextureUsage = LegacyCinematicTextureUsage.PleoStackedAlpha
                };
            LegacyCinematicGpuTextureSource texture = LegacyCinematicGpuTextureSource.FromPixels(
                pixels,
                pleoFrame.IsCutout ? "pleo-cutout" : "pleo-stacked",
                pleoFrame.SourceWidth,
                textureHeight,
                pixels,
                cacheable: false);
            gpuItems.Add(new LegacyCinematicGpuDrawItem(
                pleoClip,
                item.Quad,
                pleoMaxY,
                item.State.Alpha,
                item.State.Tint,
                LegacyCinematicFrameRenderer.GfxBlendAlpha,
                [new LegacyCinematicGpuLayer(texture, sampler, 0)]));
            return true;
        }

        MaterializedCinematicImage? itemImage = LegacyCinematicActorTiming.GetItemImage(item);
        if (itemImage == null)
            return true;

        LegacyCinematicMaterial material = itemImage.Material;
        double itemMaterialElapsedSeconds = LegacyCinematicActorTiming.GetItemMaterialElapsedSeconds(item, materialElapsedSeconds);
        IReadOnlyList<LegacyCinematicUvSampler> samplers = material.CreateSamplers(
            itemMaterialElapsedSeconds,
            item.MaterialOverrides);
        if (samplers.Count > 4)
        {
            unsupportedReason = $"{item.Actor.Actor.Key} has {samplers.Count} material layers";
            return false;
        }

        List<LegacyCinematicGpuLayer> layers = new(samplers.Count);
        bool hasEnabledLayer = false;
        for (int samplerIndex = 0; samplerIndex < samplers.Count; samplerIndex++)
        {
            LegacyCinematicUvSampler sampler = samplers[samplerIndex];
            LegacyCinematicGpuTextureSource? texture = null;
            if (sampler.TextureUsage != LegacyCinematicTextureUsage.NoTexture)
            {
                if (!LegacyCinematicRasterizer.TryGetMaterialLayerTexture(itemImage.Textures, samplerIndex, out MaterializedCinematicTexture? materialTexture))
                {
                    sampler = sampler with { Enabled = false };
                }
                else
                {
                    texture = LegacyCinematicGpuTextureSource.FromMaterialized(materialTexture);
                }
            }

            if (sampler.Enabled)
                hasEnabledLayer = true;

            layers.Add(new LegacyCinematicGpuLayer(texture, sampler, samplerIndex));
        }

        if (!hasEnabledLayer)
            return true;

        RenderGeometry geometry = LegacyCinematicActorTiming.GetItemGeometry(item);
        if ((geometry.Source == CinematicGeometrySource.MeshAtlas || geometry.HasSinusVertices) &&
            geometry.Vertices.Count >= 3 &&
            geometry.Indices.Count >= 3)
        {
            LegacyCinematicGpuDrawBuilder.AddGpuMeshDrawItem(outputCanvas, item, itemImage, geometry, layers, itemMaterialElapsedSeconds, gpuItems);
            return true;
        }

        Rectangle clip = LegacyCinematicCpuDraw.ComputeQuadClip(outputCanvas, item.Quad, out float maxY);
        if (clip.Width <= 0 || clip.Height <= 0)
            return true;

        gpuItems.Add(new LegacyCinematicGpuDrawItem(
            clip,
            item.Quad,
            maxY,
            item.State.Alpha,
            item.State.Tint,
            material.BlendMode,
            layers,
            item.UvOverride));
        return true;
    }

    internal static void AddGpuStaticRun(
        CanvasBuffer outputCanvas,
        IReadOnlyList<FrameDrawItem> frameItems,
        int start,
        int end,
        StaticLayerCache staticLayerCache,
        List<LegacyCinematicGpuDrawItem> gpuItems)
    {
        if (start >= end)
            return;

        CachedStaticLayer layer = staticLayerCache.GetOrCreate(frameItems, start, end);
        if (layer.Bounds.Width <= 0 || layer.Bounds.Height <= 0)
            return;

        ProjectedQuad quad = new(
            new Vector2(0, 0),
            new Vector2(outputCanvas.Width, 0),
            new Vector2(outputCanvas.Width, outputCanvas.Height),
            new Vector2(0, outputCanvas.Height),
            layer.Bounds);
        LegacyCinematicGpuTextureSource texture = LegacyCinematicGpuTextureSource.FromPixels(
            layer,
            $"static-run:{start}:{end}",
            outputCanvas.Width,
            outputCanvas.Height,
            layer.Image.Pixels,
            cacheable: true);
        gpuItems.Add(new LegacyCinematicGpuDrawItem(
            layer.Bounds,
            quad,
            outputCanvas.Height,
            1.0f,
            RgbTint.White,
            LegacyCinematicFrameRenderer.GfxBlendAlpha,
            [new LegacyCinematicGpuLayer(texture, LegacyCinematicUvSampler.Default, 0)]));
    }

    internal static void AddGpuMeshDrawItem(
        CanvasBuffer outputCanvas,
        FrameDrawItem item,
        MaterializedCinematicImage image,
        RenderGeometry geometry,
        IReadOnlyList<LegacyCinematicGpuLayer> layers,
        double materialElapsedSeconds,
        List<LegacyCinematicGpuDrawItem> gpuItems)
    {
        Vector2[] projected = LegacyCinematicRenderEngine.ProjectRenderVertices(
            geometry,
            item.State,
            outputCanvas.Width,
            outputCanvas.Height,
            item.Actor.Actor.CustomAnchorX,
            item.Actor.Actor.CustomAnchorY,
            item.Actor.Actor.Anchor,
            item.Actor.Actor.Sinus,
            materialElapsedSeconds,
            item.CameraOverride);
        Rectangle canvasRectangle = new(0, 0, outputCanvas.Width, outputCanvas.Height);
        List<LegacyCinematicGpuTriangle> triangles = [];
        int left = outputCanvas.Width;
        int top = outputCanvas.Height;
        int right = 0;
        int bottom = 0;
        float maxY = float.MinValue;

        for (int index = 0; index + 2 < geometry.Indices.Count; index += 3)
        {
            int index0 = geometry.Indices[index];
            int index1 = geometry.Indices[index + 1];
            int index2 = geometry.Indices[index + 2];
            if (!LegacyCinematicRasterizer.IsValidTriangleIndex(index0, geometry.Vertices.Count) ||
                !LegacyCinematicRasterizer.IsValidTriangleIndex(index1, geometry.Vertices.Count) ||
                !LegacyCinematicRasterizer.IsValidTriangleIndex(index2, geometry.Vertices.Count))
            {
                continue;
            }

            Vector2 p0 = projected[index0];
            Vector2 p1 = projected[index1];
            Vector2 p2 = projected[index2];
            if (!LegacyCinematicRasterizer.IsFinite(p0) || !LegacyCinematicRasterizer.IsFinite(p1) || !LegacyCinematicRasterizer.IsFinite(p2))
                continue;

            float denominator = ((p1.Y - p2.Y) * (p0.X - p2.X)) + ((p2.X - p1.X) * (p0.Y - p2.Y));
            if (Math.Abs(denominator) <= 0.000001f)
                continue;

            RenderVertex v0 = geometry.Vertices[index0];
            RenderVertex v1 = geometry.Vertices[index1];
            RenderVertex v2 = geometry.Vertices[index2];
            triangles.Add(new LegacyCinematicGpuTriangle(
                p0,
                p1,
                p2,
                new Vector2((float)v0.U, (float)v0.V),
                new Vector2((float)v1.U, (float)v1.V),
                new Vector2((float)v2.U, (float)v2.V)));
            left = Math.Min(left, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
            top = Math.Min(top, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
            right = Math.Max(right, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
            bottom = Math.Max(bottom, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
            maxY = MathF.Max(maxY, MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)));
        }

        if (triangles.Count == 0)
            return;

        if (LegacyCinematicRasterizer.ShouldClampCanvasBottomEdge(bottom, maxY, canvasRectangle.Height))
            bottom = canvasRectangle.Height;

        Rectangle bounds = Rectangle.Intersect(
            canvasRectangle,
            Rectangle.FromLTRB(left, top, right, bottom));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        gpuItems.Add(new LegacyCinematicGpuDrawItem(
            bounds,
            item.Quad,
            maxY,
            item.State.Alpha,
            item.State.Tint,
            image.Material.BlendMode,
            layers,
            item.UvOverride,
            MeshTriangles: triangles));
    }
}