using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicRasterizer
{
    internal static void DrawActorImage(
        CanvasBuffer canvas,
        RenderableCinematicActor actor,
        ResolvedActorState state,
        ProjectedQuad quad,
        double materialElapsedSeconds,
        CinematicMaterialRuntimeOverrides? materialOverrides = null,
        LegacyCinematicUvRect? uvOverride = null,
        LegacyCinematicCamera? cameraOverride = null)
    {
        if (actor.Image == null)
            return;

        LegacyCinematicRasterizer.DrawActorImage(canvas, actor, actor.Image, actor.Geometry, state, quad, materialElapsedSeconds, materialOverrides, uvOverride, cameraOverride);
    }

    internal static void DrawActorImage(
        CanvasBuffer canvas,
        RenderableCinematicActor actor,
        MaterializedCinematicImage image,
        RenderGeometry geometry,
        ResolvedActorState state,
        ProjectedQuad quad,
        double materialElapsedSeconds,
        CinematicMaterialRuntimeOverrides? materialOverrides = null,
        LegacyCinematicUvRect? uvOverride = null,
        LegacyCinematicCamera? cameraOverride = null)
    {
        LegacyCinematicMaterial material = image.Material;
        IReadOnlyList<LegacyCinematicUvSampler> uvSamplers = material.CreateSamplers(materialElapsedSeconds, materialOverrides);
        if ((geometry.Source == CinematicGeometrySource.MeshAtlas || geometry.HasSinusVertices) &&
            geometry.Vertices.Count >= 3 &&
            geometry.Indices.Count >= 3)
        {
            LegacyCinematicRasterizer.DrawActorMeshImage(
                canvas,
                actor,
                geometry,
                image.Textures,
                state,
                actor.Actor.CustomAnchorX,
                actor.Actor.CustomAnchorY,
                actor.Actor.Anchor,
                uvSamplers,
                material.BlendMode,
                uvOverride,
                materialElapsedSeconds,
                cameraOverride);
            return;
        }

        Rectangle canvasRectangle = new(0, 0, canvas.Width, canvas.Height);
        Rectangle clippedRectangle = Rectangle.Intersect(canvasRectangle, quad.Bounds);
        float quadMaxY = MathF.Max(MathF.Max(quad.TopLeft.Y, quad.TopRight.Y), MathF.Max(quad.BottomRight.Y, quad.BottomLeft.Y));
        if (LegacyCinematicRasterizer.ShouldClampCanvasBottomEdge(clippedRectangle.Bottom, quadMaxY, canvas.Height))
        {
            clippedRectangle = Rectangle.FromLTRB(
                clippedRectangle.Left,
                clippedRectangle.Top,
                clippedRectangle.Right,
                canvas.Height);
        }

        if (clippedRectangle.Width <= 0 || clippedRectangle.Height <= 0)
            return;

        Vector2 origin = quad.TopLeft;
        Vector2 axisU = quad.TopRight - quad.TopLeft;
        Vector2 axisV = quad.BottomLeft - quad.TopLeft;
        float determinant = (axisU.X * axisV.Y) - (axisU.Y * axisV.X);
        if (Math.Abs(determinant) <= 0.000001f)
            return;

        for (int y = clippedRectangle.Top; y < clippedRectangle.Bottom; y++)
        {
            Span<Bgra32> canvasRow = canvas.GetRowSpan(y);
            float sampleY = LegacyCinematicRasterizer.GetSampleY(y, quadMaxY, canvas.Height);
            for (int x = clippedRectangle.Left; x < clippedRectangle.Right; x++)
            {
                Vector2 delta = new(x + 0.5f - origin.X, sampleY - origin.Y);
                float u = ((delta.X * axisV.Y) - (delta.Y * axisV.X)) / determinant;
                float v = ((axisU.X * delta.Y) - (axisU.Y * delta.X)) / determinant;
                if (u < 0 || u > 1 || v < 0 || v > 1)
                    continue;

                LegacyCinematicRasterizer.ApplyUvOverride(uvOverride, ref u, ref v);
                if (!LegacyCinematicRasterizer.TrySampleMaterialPixel(image.Textures, u, v, uvSamplers, out Bgra32 source))
                    continue;

                LegacyCinematicRasterizer.BlendMaterialPixel(ref canvasRow[x], source, state, material.BlendMode);
            }
        }
    }

    internal static void DrawPleoVideoImage(
        CanvasBuffer canvas,
        PleoFrameSource sourceFrame,
        ResolvedActorState state,
        ProjectedQuad quad)
    {
        if (sourceFrame.AlphaBounds.IsEmpty)
            return;

        Rectangle canvasRectangle = new(0, 0, canvas.Width, canvas.Height);
        Rectangle clippedRectangle = Rectangle.Intersect(canvasRectangle, quad.Bounds);
        float quadMaxY = MathF.Max(MathF.Max(quad.TopLeft.Y, quad.TopRight.Y), MathF.Max(quad.BottomRight.Y, quad.BottomLeft.Y));
        if (LegacyCinematicRasterizer.ShouldClampCanvasBottomEdge(clippedRectangle.Bottom, quadMaxY, canvas.Height))
        {
            clippedRectangle = Rectangle.FromLTRB(
                clippedRectangle.Left,
                clippedRectangle.Top,
                clippedRectangle.Right,
                canvas.Height);
        }

        if (clippedRectangle.Width <= 0 || clippedRectangle.Height <= 0)
            return;

        Vector2 origin = quad.TopLeft;
        Vector2 axisU = quad.TopRight - quad.TopLeft;
        Vector2 axisV = quad.BottomLeft - quad.TopLeft;
        clippedRectangle = Rectangle.Intersect(
            clippedRectangle,
            LegacyCinematicCpuDraw.ProjectPleoAlphaBounds(sourceFrame.AlphaBounds, origin, axisU, axisV, canvasRectangle));
        if (clippedRectangle.Width <= 0 || clippedRectangle.Height <= 0)
            return;

        float determinant = (axisU.X * axisV.Y) - (axisU.Y * axisV.X);
        if (Math.Abs(determinant) <= 0.000001f)
            return;

        for (int y = clippedRectangle.Top; y < clippedRectangle.Bottom; y++)
        {
            Span<Bgra32> canvasRow = canvas.GetRowSpan(y);
            float sampleY = LegacyCinematicRasterizer.GetSampleY(y, quadMaxY, canvas.Height);
            for (int x = clippedRectangle.Left; x < clippedRectangle.Right; x++)
            {
                Vector2 delta = new(x + 0.5f - origin.X, sampleY - origin.Y);
                float u = ((delta.X * axisV.Y) - (delta.Y * axisV.X)) / determinant;
                float v = ((axisU.X * delta.Y) - (axisU.Y * delta.X)) / determinant;
                if (u < 0 || u > 1 || v < 0 || v > 1)
                    continue;

                Bgra32 source = LegacyCinematicRasterizer.SampleBilinearRegion(
                    sourceFrame,
                    u,
                    v,
                    left: 0,
                    top: 0,
                    width: sourceFrame.SourceWidth,
                    height: sourceFrame.VisibleHeight);
                if (!sourceFrame.IsCutout)
                {
                    Bgra32 mask = LegacyCinematicRasterizer.SampleBilinearRegion(
                        sourceFrame,
                        u,
                        v,
                        left: 0,
                        top: sourceFrame.VisibleHeight,
                        width: sourceFrame.SourceWidth,
                        height: sourceFrame.AlphaHeight);
                    double luma = (mask.R * 0.2126) + (mask.G * 0.7152) + (mask.B * 0.0722);
                    source.A = LegacyCinematicRasterizer.ToByte((luma - 16.0) * 255.0 / 219.0);
                }

                if (source.A == 0)
                    continue;

                LegacyCinematicRasterizer.ApplyTint(ref source, state.Tint);
                LegacyCinematicRasterizer.BlendPixel(ref canvasRow[x], source, state.Alpha);
            }
        }
    }

    internal static void DrawActorMeshImage(
        CanvasBuffer canvas,
        RenderableCinematicActor actor,
        RenderGeometry geometry,
        IReadOnlyList<MaterializedCinematicTexture> textures,
        ResolvedActorState state,
        float customAnchorX,
        float customAnchorY,
        TextureAnchor anchor,
        IReadOnlyList<LegacyCinematicUvSampler> uvSamplers,
        int blendMode,
        LegacyCinematicUvRect? uvOverride,
        double materialElapsedSeconds,
        LegacyCinematicCamera? cameraOverride = null)
    {
        Vector2[] projected = LegacyCinematicRenderEngine.ProjectRenderVertices(
            geometry,
            state,
            canvas.Width,
            canvas.Height,
            customAnchorX,
            customAnchorY,
            anchor,
            actor.Actor.Sinus,
            materialElapsedSeconds,
            cameraOverride);
        Rectangle canvasRectangle = new(0, 0, canvas.Width, canvas.Height);

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

            RenderVertex v0 = geometry.Vertices[index0];
            RenderVertex v1 = geometry.Vertices[index1];
            RenderVertex v2 = geometry.Vertices[index2];
            float denominator = ((p1.Y - p2.Y) * (p0.X - p2.X)) + ((p2.X - p1.X) * (p0.Y - p2.Y));
            if (Math.Abs(denominator) <= 0.000001f)
                continue;

            int left = (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)));
            int top = (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)));
            int right = (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X)));
            int bottom = (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)));
            float maxY = MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y));
            if (LegacyCinematicRasterizer.ShouldClampCanvasBottomEdge(bottom, maxY, canvasRectangle.Height))
                bottom = canvasRectangle.Height;

            Rectangle bounds = Rectangle.Intersect(
                canvasRectangle,
                new Rectangle(left, top, right - left, bottom - top));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            for (int y = bounds.Top; y < bounds.Bottom; y++)
            {
                Span<Bgra32> canvasRow = canvas.GetRowSpan(y);
                float py = LegacyCinematicRasterizer.GetSampleY(y, maxY, canvas.Height);
                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    float px = x + 0.5f;
                    float w0 = (((p1.Y - p2.Y) * (px - p2.X)) + ((p2.X - p1.X) * (py - p2.Y))) / denominator;
                    float w1 = (((p2.Y - p0.Y) * (px - p2.X)) + ((p0.X - p2.X) * (py - p2.Y))) / denominator;
                    float w2 = 1.0f - w0 - w1;
                    if (w0 < -0.0001f || w1 < -0.0001f || w2 < -0.0001f)
                        continue;

                    float u = (float)((w0 * v0.U) + (w1 * v1.U) + (w2 * v2.U));
                    float v = (float)((w0 * v0.V) + (w1 * v1.V) + (w2 * v2.V));
                    LegacyCinematicRasterizer.ApplyUvOverride(uvOverride, ref u, ref v);
                    if (!LegacyCinematicRasterizer.TrySampleMaterialPixel(textures, u, v, uvSamplers, out Bgra32 source))
                        continue;

                    LegacyCinematicRasterizer.BlendMaterialPixel(ref canvasRow[x], source, state, blendMode);
                }
            }
        }
    }

    internal static bool IsValidTriangleIndex(int index, int vertexCount) =>
        index >= 0 && index < vertexCount;

    internal static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    internal static bool ShouldClampCanvasBottomEdge(int exclusiveBottom, float maxY, int canvasHeight) =>
        exclusiveBottom == canvasHeight - 1 &&
        maxY > canvasHeight - 1.5f &&
        maxY < canvasHeight;

    internal static float GetSampleY(int y, float maxY, int canvasHeight)
    {
        if (y == canvasHeight - 1 &&
            maxY > canvasHeight - 1.5f &&
            maxY < canvasHeight)
        {
            return maxY - 0.0001f;
        }

        return y + 0.5f;
    }

    internal static int CompareFrameDrawItems(FrameDrawItem left, FrameDrawItem right)
    {
        int priority = left.Actor.ScenePriority.CompareTo(right.Actor.ScenePriority);
        if (priority != 0)
            return priority;

        int depth = LegacyCinematicRasterizer.GetSortDepth(left).CompareTo(LegacyCinematicRasterizer.GetSortDepth(right));
        if (depth != 0)
            return depth;

        int primitive = right.Actor.PrimitiveTieBreak.CompareTo(left.Actor.PrimitiveTieBreak);
        if (primitive != 0)
            return primitive;

        return left.ParticleIndex.CompareTo(right.ParticleIndex);
    }

    internal static float GetSortDepth(FrameDrawItem item) =>
        item.SortDepthOverride ?? item.State.PositionZ;

    internal static void ApplyUvOverride(LegacyCinematicUvRect? uvOverride, ref float u, ref float v)
    {
        if (uvOverride == null)
            return;

        LegacyCinematicUvRect rect = uvOverride.Value;
        u = rect.Left + (u * (rect.Right - rect.Left));
        v = rect.Top + (v * (rect.Bottom - rect.Top));
    }

    internal static bool TrySampleMaterialPixel(
        IReadOnlyList<MaterializedCinematicTexture> textures,
        float baseU,
        float baseV,
        IReadOnlyList<LegacyCinematicUvSampler> samplers,
        out Bgra32 pixel)
    {
        bool hasLayer = false;
        MaterialLayerColor finalColor = default;

        for (int index = 0; index < samplers.Count; index++)
        {
            LegacyCinematicUvSampler sampler = samplers[index];
            if (!sampler.Enabled)
                continue;

            Bgra32 sourcePixel;
            if (sampler.TextureUsage == LegacyCinematicTextureUsage.NoTexture)
            {
                sourcePixel = LegacyCinematicRasterizer.CreateOpaqueWhitePixel();
            }
            else
            {
                if (!LegacyCinematicRasterizer.TryGetMaterialLayerTexture(textures, index, out MaterializedCinematicTexture? texture))
                    continue;

                float u = baseU;
                float v = baseV;
                if (!LegacyCinematicRasterizer.TryTransformAndAddressUv(sampler, ref u, ref v))
                    continue;

                sourcePixel = LegacyCinematicRasterizer.SampleBilinear(texture, u, v);
            }

            MaterialLayerColor layerColor = LegacyCinematicRasterizer.RemapTextureUsage(sourcePixel, sampler.TextureUsage);
            layerColor = LegacyCinematicRasterizer.MultiplyMaterialLayerColor(layerColor, sampler.DiffuseColor);
            finalColor = hasLayer || index > 0
                ? LegacyCinematicRasterizer.BlendMaterialLayer(finalColor, layerColor, sampler.BlendMode)
                : layerColor;
            hasLayer = true;
        }

        if (!hasLayer)
        {
            pixel = default;
            return false;
        }

        pixel = LegacyCinematicRasterizer.ToPixel(finalColor);
        return pixel.A > 0;
    }

    internal static bool TryGetMaterialLayerTexture(
        IReadOnlyList<MaterializedCinematicTexture> textures,
        int layerIndex,
        [NotNullWhen(true)] out MaterializedCinematicTexture? texture)
    {
        texture = null;
        if (textures.Count == 0)
            return false;

        texture = layerIndex < textures.Count
            ? textures[layerIndex]
            : textures[0];
        return true;
    }

    internal static MaterialLayerColor RemapTextureUsage(Bgra32 source, LegacyCinematicTextureUsage textureUsage)
    {
        double red = source.R / 255.0;
        double green = source.G / 255.0;
        double blue = source.B / 255.0;
        double alpha = source.A / 255.0;

        return textureUsage switch
        {
            LegacyCinematicTextureUsage.NoTexture => new(1, 1, 1, 1),
            LegacyCinematicTextureUsage.AlphaIsRed => new(1, 1, 1, red),
            LegacyCinematicTextureUsage.AlphaIsGreen => new(1, 1, 1, green),
            LegacyCinematicTextureUsage.AlphaIsBlue => new(1, 1, 1, blue),
            LegacyCinematicTextureUsage.AlphaIsAlpha => new(1, 1, 1, alpha),
            LegacyCinematicTextureUsage.RgbOnly => new(red, green, blue, 1),
            _ => new(red, green, blue, alpha)
        };
    }

    internal static MaterialLayerColor BlendMaterialLayer(
        MaterialLayerColor destination,
        MaterialLayerColor source,
        int blendMode)
    {
        return blendMode switch
        {
            LegacyCinematicFrameRenderer.GfxBlendAdd => new(
                destination.Red + source.Red,
                destination.Green + source.Green,
                destination.Blue + source.Blue,
                destination.Alpha + source.Alpha),
            LegacyCinematicFrameRenderer.GfxBlendAddAlpha => new(
                destination.Red + (source.Red * source.Alpha),
                destination.Green + (source.Green * source.Alpha),
                destination.Blue + (source.Blue * source.Alpha),
                destination.Alpha + source.Alpha),
            LegacyCinematicFrameRenderer.GfxBlendMul => new(
                destination.Red * source.Red,
                destination.Green * source.Green,
                destination.Blue * source.Blue,
                destination.Alpha * source.Alpha),
            LegacyCinematicFrameRenderer.GfxBlendAlphaMul => new(
                destination.Red * source.Alpha,
                destination.Green * source.Alpha,
                destination.Blue * source.Alpha,
                destination.Alpha * source.Alpha),
            _ => LegacyCinematicRasterizer.BlendMaterialLayerAlpha(destination, source)
        };
    }

    internal static MaterialLayerColor MultiplyMaterialLayerColor(
        MaterialLayerColor color,
        LegacyCinematicMaterialColor diffuseColor)
    {
        return new(
            color.Red * diffuseColor.Red,
            color.Green * diffuseColor.Green,
            color.Blue * diffuseColor.Blue,
            color.Alpha * diffuseColor.Alpha);
    }

    internal static MaterialLayerColor BlendMaterialLayerAlpha(MaterialLayerColor destination, MaterialLayerColor source)
    {
        double inverseAlpha = 1.0 - source.Alpha;
        return new(
            (source.Red * source.Alpha) + (destination.Red * inverseAlpha),
            (source.Green * source.Alpha) + (destination.Green * inverseAlpha),
            (source.Blue * source.Alpha) + (destination.Blue * inverseAlpha),
            source.Alpha + (destination.Alpha * inverseAlpha));
    }

    internal static Bgra32 ToPixel(MaterialLayerColor color)
    {
        Bgra32 pixel = default;
        pixel.R = LegacyCinematicRasterizer.ToByte(color.Red * 255.0);
        pixel.G = LegacyCinematicRasterizer.ToByte(color.Green * 255.0);
        pixel.B = LegacyCinematicRasterizer.ToByte(color.Blue * 255.0);
        pixel.A = LegacyCinematicRasterizer.ToByte(color.Alpha * 255.0);
        return pixel;
    }

    internal static Bgra32 CreateOpaqueWhitePixel()
    {
        Bgra32 pixel = default;
        pixel.R = 255;
        pixel.G = 255;
        pixel.B = 255;
        pixel.A = 255;
        return pixel;
    }

    internal static bool TryTransformAndAddressUv(LegacyCinematicUvSampler sampler, ref float u, ref float v)
    {
        foreach (LegacyCinematicUvModifierState modifier in sampler.Modifiers)
        {
            if (Math.Abs(modifier.Rotation) > 0.000001f)
            {
                float cos = MathF.Cos(modifier.Rotation);
                float sin = MathF.Sin(modifier.Rotation);
                float rotatedU = (u * cos) - (v * sin) +
                    (modifier.RotationOffsetU * (1.0f - cos)) +
                    (modifier.RotationOffsetV * sin);
                float rotatedV = (u * sin) + (v * cos) +
                    (modifier.RotationOffsetV * (1.0f - cos)) -
                    (modifier.RotationOffsetU * sin);
                u = rotatedU;
                v = rotatedV;
            }

            u = (u * modifier.ScaleU) + (modifier.ScaleOffsetU * (1.0f - modifier.ScaleU));
            v = (v * modifier.ScaleV) + (modifier.ScaleOffsetV * (1.0f - modifier.ScaleV));
            u += modifier.TranslationU;
            v += modifier.TranslationV;
        }

        return LegacyCinematicRasterizer.TryAddressUv(ref u, sampler.AddressModeU) &&
            LegacyCinematicRasterizer.TryAddressUv(ref v, sampler.AddressModeV);
    }

    internal static bool TryAddressUv(ref float value, TextureAddressMode mode)
    {
        switch (mode)
        {
            case TextureAddressMode.Wrap:
                value = LegacyCinematicRasterizer.AvoidAddressingExactUpperEdge(value);
                value -= MathF.Floor(value);
                return true;
            case TextureAddressMode.Mirror:
                {
                    value = LegacyCinematicRasterizer.AvoidAddressingExactUpperEdge(value);
                    float floor = MathF.Floor(value);
                    value -= floor;
                    if (((int)floor & 1) != 0)
                        value = 1.0f - value;
                    return true;
                }
            case TextureAddressMode.Clamp:
                value = Math.Clamp(value, 0.0f, 1.0f);
                return true;
            case TextureAddressMode.Border:
                return value is >= 0.0f and <= 1.0f;
            default:
                return value is >= 0.0f and <= 1.0f;
        }
    }

    internal static float AvoidAddressingExactUpperEdge(float value)
    {
        if (value > 0.0f && MathF.Abs(value - MathF.Round(value)) <= 0.00001f)
            return value - 0.00001f;

        return value;
    }

    internal static Bgra32 SampleBilinear(MaterializedCinematicTexture source, float u, float v)
    {
        float sourceX = u * (source.Width - 1);
        float sourceY = v * (source.Height - 1);
        int x0 = (int)MathF.Floor(sourceX);
        int y0 = (int)MathF.Floor(sourceY);
        int x1 = Math.Min(x0 + 1, source.Width - 1);
        int y1 = Math.Min(y0 + 1, source.Height - 1);
        float tx = sourceX - x0;
        float ty = sourceY - y0;

        Bgra32 c00 = source.Pixels[(y0 * source.Width) + x0];
        Bgra32 c10 = source.Pixels[(y0 * source.Width) + x1];
        Bgra32 c01 = source.Pixels[(y1 * source.Width) + x0];
        Bgra32 c11 = source.Pixels[(y1 * source.Width) + x1];

        Bgra32 result = default;
        result.B = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.B, c10.B, tx), LegacyCinematicRasterizer.Lerp(c01.B, c11.B, tx), ty);
        result.G = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.G, c10.G, tx), LegacyCinematicRasterizer.Lerp(c01.G, c11.G, tx), ty);
        result.R = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.R, c10.R, tx), LegacyCinematicRasterizer.Lerp(c01.R, c11.R, tx), ty);
        result.A = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.A, c10.A, tx), LegacyCinematicRasterizer.Lerp(c01.A, c11.A, tx), ty);
        return result;
    }

    internal static Bgra32 SampleBilinearRegion(
        PleoFrameSource source,
        float u,
        float v,
        int left,
        int top,
        int width,
        int height)
    {
        float sourceX = left + (u * (width - 1));
        float sourceY = top + (v * (height - 1));
        int x0 = (int)MathF.Floor(sourceX);
        int y0 = (int)MathF.Floor(sourceY);
        int x1 = Math.Min(x0 + 1, left + width - 1);
        int y1 = Math.Min(y0 + 1, top + height - 1);
        float tx = sourceX - x0;
        float ty = sourceY - y0;

        Bgra32 c00 = source.Pixels[(y0 * source.StackWidth) + x0];
        Bgra32 c10 = source.Pixels[(y0 * source.StackWidth) + x1];
        Bgra32 c01 = source.Pixels[(y1 * source.StackWidth) + x0];
        Bgra32 c11 = source.Pixels[(y1 * source.StackWidth) + x1];

        Bgra32 result = default;
        result.B = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.B, c10.B, tx), LegacyCinematicRasterizer.Lerp(c01.B, c11.B, tx), ty);
        result.G = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.G, c10.G, tx), LegacyCinematicRasterizer.Lerp(c01.G, c11.G, tx), ty);
        result.R = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.R, c10.R, tx), LegacyCinematicRasterizer.Lerp(c01.R, c11.R, tx), ty);
        result.A = LegacyCinematicRasterizer.Lerp(LegacyCinematicRasterizer.Lerp(c00.A, c10.A, tx), LegacyCinematicRasterizer.Lerp(c01.A, c11.A, tx), ty);
        return result;
    }

    internal static void ApplyTint(ref Bgra32 pixel, RgbTint tint)
    {
        if (tint.IsWhite)
            return;

        pixel.R = LegacyCinematicRasterizer.ScaleColorChannel(pixel.R, tint.Red);
        pixel.G = LegacyCinematicRasterizer.ScaleColorChannel(pixel.G, tint.Green);
        pixel.B = LegacyCinematicRasterizer.ScaleColorChannel(pixel.B, tint.Blue);
    }

    internal static void BlendMaterialPixel(ref Bgra32 destination, Bgra32 source, ResolvedActorState state, int blendMode)
    {
        if (source.A == 0)
            return;

        LegacyCinematicRasterizer.ApplyTint(ref source, state.Tint);
        switch (blendMode)
        {
            case LegacyCinematicFrameRenderer.GfxBlendCopy:
                LegacyCinematicRasterizer.BlendCopyPixel(ref destination, source, state.Alpha);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendAdd:
                LegacyCinematicRasterizer.BlendAdditivePixel(ref destination, source, state.Alpha, useSourceAlpha: false);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendAddAlpha:
                LegacyCinematicRasterizer.BlendAdditivePixel(ref destination, source, state.Alpha, useSourceAlpha: true);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendSub:
                LegacyCinematicRasterizer.BlendSubtractivePixel(ref destination, source, state.Alpha, useSourceAlpha: false);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendSubAlpha:
                LegacyCinematicRasterizer.BlendSubtractivePixel(ref destination, source, state.Alpha, useSourceAlpha: true);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendMul:
                LegacyCinematicRasterizer.BlendSourceColorMultiplyPixel(ref destination, source, state.Alpha);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendAlphaMul:
                LegacyCinematicRasterizer.BlendAlphaMultiplyPixel(ref destination, source, state.Alpha, invertAlpha: false);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendInvAlphaMul:
                LegacyCinematicRasterizer.BlendAlphaMultiplyPixel(ref destination, source, state.Alpha, invertAlpha: true);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendMul2X:
                LegacyCinematicRasterizer.BlendMultiply2xPixel(ref destination, source, state.Alpha);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendScreen:
                LegacyCinematicRasterizer.BlendScreenPixel(ref destination, source, state.Alpha);
                return;
            case LegacyCinematicFrameRenderer.GfxBlendAlphaPremult:
                LegacyCinematicRasterizer.BlendPremultipliedAlphaPixel(ref destination, source, state.Alpha);
                return;
        }

        LegacyCinematicRasterizer.BlendPixel(ref destination, source, state.Alpha);
    }

    internal static void BlendCopyPixel(ref Bgra32 destination, Bgra32 source, float opacity)
    {
        double alpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (alpha <= 0)
            return;

        source.A = LegacyCinematicRasterizer.ToByte(alpha * 255.0);
        destination = source;
    }

    internal static void BlendPremultipliedAlphaPixel(ref Bgra32 destination, Bgra32 source, float opacity)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        double destinationAlpha = destination.A / 255.0;
        double outputAlpha = sourceAlpha + (destinationAlpha * (1.0 - sourceAlpha));
        if (outputAlpha <= 0)
        {
            destination = default;
            return;
        }

        destination.B = LegacyCinematicRasterizer.ToByte(source.B + (destination.B * (1.0 - sourceAlpha)));
        destination.G = LegacyCinematicRasterizer.ToByte(source.G + (destination.G * (1.0 - sourceAlpha)));
        destination.R = LegacyCinematicRasterizer.ToByte(source.R + (destination.R * (1.0 - sourceAlpha)));
        destination.A = LegacyCinematicRasterizer.ToByte(outputAlpha * 255.0);
    }

    internal static void BlendSourceColorMultiplyPixel(ref Bgra32 destination, Bgra32 source, float opacity)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        destination.B = LegacyCinematicRasterizer.ToByte(destination.B * (source.B / 255.0));
        destination.G = LegacyCinematicRasterizer.ToByte(destination.G * (source.G / 255.0));
        destination.R = LegacyCinematicRasterizer.ToByte(destination.R * (source.R / 255.0));
        destination.A = LegacyCinematicRasterizer.ToByte(destination.A * sourceAlpha);
    }

    internal static void BlendAdditivePixel(ref Bgra32 destination, Bgra32 source, float opacity, bool useSourceAlpha)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        double factor = useSourceAlpha ? sourceAlpha : 1.0;
        destination.B = LegacyCinematicRasterizer.ToByte(destination.B + (source.B * factor));
        destination.G = LegacyCinematicRasterizer.ToByte(destination.G + (source.G * factor));
        destination.R = LegacyCinematicRasterizer.ToByte(destination.R + (source.R * factor));
        destination.A = LegacyCinematicRasterizer.ToByte(destination.A + (sourceAlpha * 255.0));
    }

    internal static void BlendSubtractivePixel(ref Bgra32 destination, Bgra32 source, float opacity, bool useSourceAlpha)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        if (useSourceAlpha)
        {
            destination.B = LegacyCinematicRasterizer.ToByte(destination.B - (source.B * sourceAlpha));
            destination.G = LegacyCinematicRasterizer.ToByte(destination.G - (source.G * sourceAlpha));
            destination.R = LegacyCinematicRasterizer.ToByte(destination.R - (source.R * sourceAlpha));
            destination.A = LegacyCinematicRasterizer.ToByte(destination.A - (sourceAlpha * 255.0));
            return;
        }

        destination.B = LegacyCinematicRasterizer.ToByte(destination.B * (1.0 - (source.B / 255.0)));
        destination.G = LegacyCinematicRasterizer.ToByte(destination.G * (1.0 - (source.G / 255.0)));
        destination.R = LegacyCinematicRasterizer.ToByte(destination.R * (1.0 - (source.R / 255.0)));
        destination.A = LegacyCinematicRasterizer.ToByte(destination.A * (1.0 - sourceAlpha));
    }

    internal static void BlendAlphaMultiplyPixel(ref Bgra32 destination, Bgra32 source, float opacity, bool invertAlpha)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        double factor = invertAlpha ? 1.0 - sourceAlpha : sourceAlpha;
        destination.B = LegacyCinematicRasterizer.ToByte(destination.B * factor);
        destination.G = LegacyCinematicRasterizer.ToByte(destination.G * factor);
        destination.R = LegacyCinematicRasterizer.ToByte(destination.R * factor);
        destination.A = LegacyCinematicRasterizer.ToByte(destination.A * factor);
    }

    internal static void BlendMultiply2xPixel(ref Bgra32 destination, Bgra32 source, float opacity)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        destination.B = LegacyCinematicRasterizer.ToByte(destination.B * source.B / 127.5);
        destination.G = LegacyCinematicRasterizer.ToByte(destination.G * source.G / 127.5);
        destination.R = LegacyCinematicRasterizer.ToByte(destination.R * source.R / 127.5);
        destination.A = LegacyCinematicRasterizer.ToByte(destination.A * sourceAlpha * 2.0);
    }

    internal static void BlendScreenPixel(ref Bgra32 destination, Bgra32 source, float opacity)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        destination.B = LegacyCinematicRasterizer.ToByte(LegacyCinematicRasterizer.ScreenChannel(destination.B, source.B));
        destination.G = LegacyCinematicRasterizer.ToByte(LegacyCinematicRasterizer.ScreenChannel(destination.G, source.G));
        destination.R = LegacyCinematicRasterizer.ToByte(LegacyCinematicRasterizer.ScreenChannel(destination.R, source.R));
        destination.A = LegacyCinematicRasterizer.ToByte((sourceAlpha * 255.0) + (destination.A * (1.0 - sourceAlpha)));
    }

    internal static double ScreenChannel(byte destination, byte source) =>
        255.0 - ((255.0 - destination) * (255.0 - source) / 255.0);

    internal static void BlendPixel(ref Bgra32 destination, Bgra32 source, float opacity)
    {
        double sourceAlpha = source.A / 255.0 * Math.Clamp(opacity, 0, 1);
        if (sourceAlpha <= 0)
            return;

        double destinationAlpha = destination.A / 255.0;
        double outputAlpha = sourceAlpha + (destinationAlpha * (1.0 - sourceAlpha));
        if (outputAlpha <= 0)
        {
            destination = default;
            return;
        }

        destination.B = LegacyCinematicRasterizer.BlendChannel(source.B, sourceAlpha, destination.B, destinationAlpha, outputAlpha);
        destination.G = LegacyCinematicRasterizer.BlendChannel(source.G, sourceAlpha, destination.G, destinationAlpha, outputAlpha);
        destination.R = LegacyCinematicRasterizer.BlendChannel(source.R, sourceAlpha, destination.R, destinationAlpha, outputAlpha);
        destination.A = LegacyCinematicRasterizer.ToByte(outputAlpha * 255.0);
    }

    internal static byte BlendChannel(byte source, double sourceAlpha, byte destination, double destinationAlpha, double outputAlpha) =>
        LegacyCinematicRasterizer.ToByte(((source * sourceAlpha) + (destination * destinationAlpha * (1.0 - sourceAlpha))) / outputAlpha);

    internal static byte ScaleColorChannel(byte value, double multiplier) =>
        LegacyCinematicRasterizer.ToByte(value * multiplier);

    internal static byte Lerp(byte left, byte right, float amount) =>
        LegacyCinematicRasterizer.ToByte(left + ((right - left) * amount));

    internal static double LerpDouble(double left, double right, double amount) =>
        left + ((right - left) * amount);

    internal static byte ToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    internal static void ClearCanvas(CanvasBuffer canvas) =>
        canvas.Pixels.AsSpan().Clear();

    internal static void ClearCanvas(CanvasBuffer canvas, Bgra32 color) =>
        canvas.Pixels.AsSpan().Fill(color);

    internal static void SaveCanvasAsPng(CanvasBuffer canvas, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using Image<Bgra32> image = Image.LoadPixelData(canvas.Pixels, canvas.Width, canvas.Height);
        image.Save(path, new PngEncoder());
    }

    internal static Bgra32 CreateOpaqueBlackPixel()
    {
        Bgra32 pixel = default;
        pixel.A = 255;
        return pixel;
    }
}