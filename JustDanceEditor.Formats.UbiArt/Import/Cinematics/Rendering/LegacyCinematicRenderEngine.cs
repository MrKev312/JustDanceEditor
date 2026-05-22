using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using SixLabors.ImageSharp;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal sealed class LegacyCinematicRenderRuntime
{
    private readonly Dictionary<string, LegacyCinematicActor> actorsByKey;
    private readonly Dictionary<string, string?> subSceneParentKeys;

    public LegacyCinematicRenderRuntime(LegacyCinematicScene scene)
    {
        actorsByKey = new Dictionary<string, LegacyCinematicActor>(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyCinematicActor actor in scene.Actors)
            actorsByKey.TryAdd(actor.Key, actor);

        subSceneParentKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyCinematicActor actor in scene.Actors)
            subSceneParentKeys[actor.Key] = FindSubSceneParentKey(actor);
    }

    public IReadOnlyDictionary<string, LegacyCinematicActor> ActorsByKey => actorsByKey;

    public bool TryGetSubSceneParent(
        LegacyCinematicActor actor,
        [NotNullWhen(true)] out LegacyCinematicActor? parent)
    {
        parent = null;
        if (!subSceneParentKeys.TryGetValue(actor.Key, out string? parentKey) ||
            parentKey == null)
        {
            return false;
        }

        return actorsByKey.TryGetValue(parentKey, out parent);
    }

    public bool TryResolveParentBindActor(
        LegacyCinematicActor actor,
        [NotNullWhen(true)] out LegacyCinematicActor? parentActor)
    {
        parentActor = null;
        if (actor.ParentBind == null || string.IsNullOrWhiteSpace(actor.ParentBind.ParentActorName))
            return false;

        string siblingParentKey = actor.Path.Count <= 1
            ? LegacyCinematicNames.NormalizeKey([actor.ParentBind.ParentActorName])
            : LegacyCinematicNames.NormalizeKey(actor.Path.Take(actor.Path.Count - 1).Append(actor.ParentBind.ParentActorName));
        if (actorsByKey.TryGetValue(siblingParentKey, out parentActor))
            return true;

        List<LegacyCinematicActor> matches = [.. actorsByKey.Values.Where(candidate =>
            string.Equals(candidate.Name, actor.ParentBind.ParentActorName, StringComparison.OrdinalIgnoreCase))];
        if (matches.Count != 1)
            return false;

        parentActor = matches[0];
        return true;
    }

    private string? FindSubSceneParentKey(LegacyCinematicActor actor)
    {
        for (int length = actor.Path.Count - 1; length >= 1; length--)
        {
            string key = LegacyCinematicNames.NormalizeKey(actor.Path.Take(length));
            if (actorsByKey.TryGetValue(key, out LegacyCinematicActor? candidate) &&
                IsSubSceneActor(candidate))
            {
                return candidate.Key;
            }
        }

        return null;
    }

    private static bool IsSubSceneActor(LegacyCinematicActor actor) =>
        LegacyBinarySerializer.IsTypeId<LegacyCinematicSubSceneActorBinary>(actor.TypeId) ||
        actor.SubScenePath != null;
}

internal static class LegacyCinematicRenderEngine
{
    public static RenderGeometry CreateRenderGeometry(MaterializedCinematicImage image) =>
        image.Geometry;

    public static RenderGeometry CreatePleoVideoGeometry() =>
        new(2.0, 2.0, 0.0, 0.0, CinematicGeometrySource.PleoVideoQuad);

    public static ResolvedActorState ResolveActorState(
        LegacyCinematicActor actor,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex,
        double frame,
        LegacyCinematicTimeline? timeline = null,
        int renderStartFrame = 0)
    {
        Dictionary<string, ResolvedActorState> cache = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> resolving = new(StringComparer.OrdinalIgnoreCase);
        return ResolveActorState(actor, runtime, clipIndex, frame, timeline, renderStartFrame, cache, resolving);
    }

    public static IReadOnlyDictionary<string, ResolvedActorState> ResolveActorStates(
        IEnumerable<LegacyCinematicActor> actors,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex,
        double frame,
        LegacyCinematicTimeline? timeline = null,
        int renderStartFrame = 0)
    {
        Dictionary<string, ResolvedActorState> cache = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> resolving = new(StringComparer.OrdinalIgnoreCase);
        foreach (LegacyCinematicActor actor in actors)
            ResolveActorState(actor, runtime, clipIndex, frame, timeline, renderStartFrame, cache, resolving);

        return cache;
    }

    public static ProjectedQuad ProjectQuad(
        RenderGeometry geometry,
        ResolvedActorState state,
        int outputWidth,
        int outputHeight,
        float customAnchorX = 0.0f,
        float customAnchorY = 0.0f,
        TextureAnchor anchor = TextureAnchor.MiddleCenter,
        LegacyCinematicCamera? cameraOverride = null)
    {
        LegacyCinematicCamera camera = cameraOverride ?? LegacyCinematicCamera.CreateDefault(outputWidth, outputHeight);
        double halfWidth = geometry.WidthWorld * 0.5;
        double halfHeight = geometry.HeightWorld * 0.5;
        Vector2 anchorOffset = ComputeAnchorOffset(geometry, anchor, customAnchorX, customAnchorY);

        Vector2 topLeft = ProjectLocalPoint(-halfWidth + geometry.CenterX, halfHeight + geometry.CenterY, 0, state, camera, outputWidth, outputHeight, anchorOffset);
        Vector2 topRight = ProjectLocalPoint(halfWidth + geometry.CenterX, halfHeight + geometry.CenterY, 0, state, camera, outputWidth, outputHeight, anchorOffset);
        Vector2 bottomRight = ProjectLocalPoint(halfWidth + geometry.CenterX, -halfHeight + geometry.CenterY, 0, state, camera, outputWidth, outputHeight, anchorOffset);
        Vector2 bottomLeft = ProjectLocalPoint(-halfWidth + geometry.CenterX, -halfHeight + geometry.CenterY, 0, state, camera, outputWidth, outputHeight, anchorOffset);

        if (!IsFinite(topLeft) ||
            !IsFinite(topRight) ||
            !IsFinite(bottomRight) ||
            !IsFinite(bottomLeft))
        {
            return ProjectedQuad.Empty;
        }

        float minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X));
        float minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y));
        float maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X));
        float maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y));

        int left = (int)Math.Floor(minX);
        int top = (int)Math.Floor(minY);
        int right = (int)Math.Ceiling(maxX);
        int bottom = (int)Math.Ceiling(maxY);
        if (right <= left || bottom <= top)
            return ProjectedQuad.Empty;

        return new ProjectedQuad(
            topLeft,
            topRight,
            bottomRight,
            bottomLeft,
            new Rectangle(left, top, right - left, bottom - top));
    }

    public static Vector2[] ProjectRenderVertices(
        RenderGeometry geometry,
        ResolvedActorState state,
        int outputWidth,
        int outputHeight,
        float customAnchorX = 0.0f,
        float customAnchorY = 0.0f,
        TextureAnchor anchor = TextureAnchor.MiddleCenter,
        LegacyCinematicSinusParameters sinus = default,
        double materialElapsedSeconds = 0.0,
        LegacyCinematicCamera? cameraOverride = null)
    {
        LegacyCinematicCamera camera = cameraOverride ?? LegacyCinematicCamera.CreateDefault(outputWidth, outputHeight);
        Vector2 anchorOffset = ComputeAnchorOffset(geometry, anchor, customAnchorX, customAnchorY);
        Vector2[] projected = new Vector2[geometry.Vertices.Count];
        for (int i = 0; i < geometry.Vertices.Count; i++)
        {
            RenderVertex vertex = geometry.Vertices[i];
            ApplySinusVertexOffset(vertex, sinus, materialElapsedSeconds, out double x, out double y, out double z);
            projected[i] = ProjectLocalPoint(x, y, z, state, camera, outputWidth, outputHeight, anchorOffset);
        }

        return projected;
    }

    private static void ApplySinusVertexOffset(
        RenderVertex vertex,
        LegacyCinematicSinusParameters sinus,
        double materialElapsedSeconds,
        out double x,
        out double y,
        out double z)
    {
        x = vertex.X;
        y = vertex.Y;
        z = vertex.Z;
        if (!sinus.HasAnimatedOffset || Math.Abs(vertex.SinusWeight) <= 0.000001)
            return;

        double factor = vertex.SinusWeight * Math.Sin((materialElapsedSeconds * sinus.Speed) + vertex.SinusOffsetTime);
        x += sinus.AmplitudeX * factor;
        y += sinus.AmplitudeY * factor;
        z += sinus.AmplitudeZ * factor;
    }

    private static ResolvedActorState ResolveActorState(
        LegacyCinematicActor actor,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex,
        double frame,
        LegacyCinematicTimeline? timeline,
        int renderStartFrame,
        Dictionary<string, ResolvedActorState> cache,
        HashSet<string> resolving)
    {
        if (cache.TryGetValue(actor.Key, out ResolvedActorState? cachedState))
            return cachedState;

        if (!resolving.Add(actor.Key))
            return ToResolvedState(ResolveLocalActorState(actor, clipIndex, LegacyCinematicActorTimeOffsets.GetClipFrame(actor, frame, timeline, renderStartFrame)));

        ResolvedActorState state;
        double actorFrame = LegacyCinematicActorTimeOffsets.GetClipFrame(actor, frame, timeline, renderStartFrame);
        LocalActorState local = ResolveLocalActorState(actor, clipIndex, actorFrame);
        if (actor.ParentBind != null &&
            runtime.TryResolveParentBindActor(actor, out LegacyCinematicActor? bindParent))
        {
            ResolvedActorState parentState = ResolveActorState(bindParent, runtime, clipIndex, frame, timeline, renderStartFrame, cache, resolving);
            state = ResolveBoundState(parentState, local, actor.ParentBind);
        }
        else if (runtime.TryGetSubSceneParent(actor, out LegacyCinematicActor? subSceneParent))
        {
            ResolvedActorState parentState = ResolveActorState(subSceneParent, runtime, clipIndex, frame, timeline, renderStartFrame, cache, resolving);
            state = ResolveSubSceneState(parentState, local);
        }
        else
        {
            state = ToResolvedState(local);
        }

        resolving.Remove(actor.Key);
        cache[actor.Key] = state;
        return state;
    }

    private static ResolvedActorState ResolveSubSceneState(ResolvedActorState parent, LocalActorState local)
    {
        float localX = local.PositionX;
        float localAngle = local.Angle;
        bool flipped = local.XFlipped;

        if (parent.XFlipped)
        {
            localX = -localX;
            localAngle = -localAngle;
            flipped = !flipped;
        }

        (float rotatedX, float rotatedY) = RotatePoint(
            localX * Math.Abs(parent.ScaleX),
            local.PositionY * Math.Abs(parent.ScaleY),
            parent.Angle);

        return ToResolvedState(
            parent.PositionX + rotatedX,
            parent.PositionY + rotatedY,
            parent.PositionZ + local.PositionZ,
            local.ScaleX * Math.Abs(parent.ScaleX),
            local.ScaleY * Math.Abs(parent.ScaleY),
            parent.Angle + localAngle,
            local.Alpha,
            local.Tint,
            flipped,
            local.RotationX,
            local.RotationY);
    }

    private static ResolvedActorState ResolveBoundState(
        ResolvedActorState parent,
        LocalActorState local,
        LegacyCinematicActorBind bind)
    {
        float localX = local.PositionX;
        if (bind.UseParentFlip != 0 && parent.XFlipped)
            localX = -localX;

        (float rotatedX, float rotatedY) = RotatePoint(
            localX * Math.Abs(parent.ScaleX),
            local.PositionY * Math.Abs(parent.ScaleY),
            parent.Angle);

        (float scaleX, float scaleY) = bind.ScaleInheritProp switch
        {
            LegacyCinematicConstants.BindScaleInheritUseParent => (Math.Abs(parent.ScaleX), Math.Abs(parent.ScaleY)),
            LegacyCinematicConstants.BindScaleInheritCombine => (Math.Abs(parent.ScaleX) * local.ScaleX, Math.Abs(parent.ScaleY) * local.ScaleY),
            _ => (local.ScaleX, local.ScaleY)
        };

        bool flipped = bind.UseParentFlip != 0 ? parent.XFlipped : local.XFlipped;
        float alpha = bind.UseParentAlpha != 0 ? parent.Alpha * local.Alpha : local.Alpha;
        RgbTint tint = bind.UseParentColor != 0 ? parent.Tint.Multiply(local.Tint) : local.Tint;

        return ToResolvedState(
            parent.PositionX + rotatedX,
            parent.PositionY + rotatedY,
            parent.PositionZ + local.PositionZ,
            scaleX,
            scaleY,
            parent.Angle + local.Angle,
            alpha,
            tint,
            flipped,
            local.RotationX,
            local.RotationY);
    }

    private static LocalActorState ResolveLocalActorState(
        LegacyCinematicActor actor,
        PropertyClipIndex clipIndex,
        double frame)
    {
        float baseX = actor.ParentBind?.OffsetX ?? actor.PositionX;
        float baseY = actor.ParentBind?.OffsetY ?? actor.PositionY;
        float baseZ = actor.ParentBind?.OffsetZ ?? actor.RelativeZ;
        float baseScaleX = actor.ParentBind?.ScaleInheritProp == LegacyCinematicConstants.BindScaleInheritCombine
            ? actor.ParentBind.LocalScaleX
            : actor.ScaleX;
        float baseScaleY = actor.ParentBind?.ScaleInheritProp == LegacyCinematicConstants.BindScaleInheritCombine
            ? actor.ParentBind.LocalScaleY
            : actor.ScaleY;
        float baseAngle = actor.ParentBind?.OffsetAngle ?? actor.Angle;

        float x = baseX;
        float y = baseY;
        float z = baseZ;
        float scaleX = AvoidZeroScale(baseScaleX);
        float scaleY = AvoidZeroScale(baseScaleY);
        float angle = baseAngle;
        float rotationX = actor.Sinus.AngleX;
        float rotationY = actor.Sinus.AngleY;
        float alpha = actor.BaseAlpha;
        RgbTint tint = actor.BaseTint;

        foreach (PropertyClip clip in clipIndex.GetClips(actor, frame))
        {
            CinematicTransform? transform = clip.State.Transform;
            CinematicMaterial? material = clip.State.Material;
            double localFrame = GetClipLocalFrame(clip, frame);

            if (transform?.PositionX != null)
                x = baseX + (float)EvaluateCurve(transform.PositionX, localFrame);

            if (transform?.PositionY != null)
                y = baseY + (float)EvaluateCurve(transform.PositionY, localFrame);

            if (transform?.PositionZ != null)
                z = baseZ + (float)EvaluateCurve(transform.PositionZ, localFrame);

            if (transform?.Rotation != null)
                angle = baseAngle + (float)EvaluateCurve(transform.Rotation, localFrame);

            if (transform?.RotationX != null)
                rotationX = actor.Sinus.AngleX + (float)EvaluateCurve(transform.RotationX, localFrame);

            if (transform?.RotationY != null)
                rotationY = actor.Sinus.AngleY + (float)EvaluateCurve(transform.RotationY, localFrame);

            if (transform?.ScaleX != null)
            {
                float value = AvoidZeroScale((float)EvaluateCurve(transform.ScaleX, localFrame));
                scaleX = transform.ScaleMode == CinematicScaleMode.Proportional
                    ? baseScaleX * value
                    : value;
            }

            if (transform?.ScaleY != null)
            {
                float value = AvoidZeroScale((float)EvaluateCurve(transform.ScaleY, localFrame));
                scaleY = transform.ScaleMode == CinematicScaleMode.Proportional
                    ? baseScaleY * value
                    : value;
            }

            if (material?.Alpha != null)
            {
                float evaluatedAlpha = (float)Math.Clamp(EvaluateCurve(material.Alpha, localFrame), 0, 1);
                alpha = LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(clip.TypeId)
                    ? evaluatedAlpha
                    : alpha * evaluatedAlpha;
            }

            if (material?.HasColor == true)
            {
                double red = tint.Red;
                double green = tint.Green;
                double blue = tint.Blue;

                if (material.Red != null)
                    red = EvaluateCurve(material.Red, localFrame);

                if (material.Green != null)
                    green = EvaluateCurve(material.Green, localFrame);

                if (material.Blue != null)
                    blue = EvaluateCurve(material.Blue, localFrame);

                tint = new RgbTint(Math.Clamp(red, 0, 4), Math.Clamp(green, 0, 4), Math.Clamp(blue, 0, 4));
            }
        }

        return new LocalActorState(
            x,
            y,
            z,
            scaleX,
            scaleY,
            angle,
            Math.Clamp(alpha, 0, 1),
            tint,
            (actor.XFlipped & 1) != 0,
            rotationX,
            rotationY);
    }

    private static ResolvedActorState ToResolvedState(LocalActorState local) =>
        ToResolvedState(
            local.PositionX,
            local.PositionY,
            local.PositionZ,
            local.ScaleX,
            local.ScaleY,
            local.Angle,
            local.Alpha,
            local.Tint,
            local.XFlipped,
            local.RotationX,
            local.RotationY);

    private static ResolvedActorState ToResolvedState(
        float positionX,
        float positionY,
        float positionZ,
        float scaleX,
        float scaleY,
        float angle,
        float alpha,
        RgbTint tint,
        bool flipped,
        float rotationX = 0.0f,
        float rotationY = 0.0f)
    {
        float renderScaleX = flipped ? -Math.Abs(scaleX) : Math.Abs(scaleX);
        return new ResolvedActorState(
            positionX,
            positionY,
            positionZ,
            renderScaleX,
            scaleY,
            angle,
            Math.Clamp(alpha, 0, 1),
            tint,
            flipped,
            rotationX,
            rotationY);
    }

    private static Vector2 ProjectLocalPoint(
        double localX,
        double localY,
        double localZ,
        ResolvedActorState state,
        LegacyCinematicCamera camera,
        int outputWidth,
        int outputHeight,
        Vector2 anchorOffset)
    {
        double modelX = (localX + anchorOffset.X) * state.ScaleX;
        double modelY = (localY + anchorOffset.Y) * state.ScaleY;
        double modelZ = localZ;

        RotateX(ref modelY, ref modelZ, state.RotationX);
        RotateY(ref modelX, ref modelZ, state.RotationY);

        (float rotatedX, float rotatedY) = RotatePoint(
            (float)modelX,
            (float)modelY,
            state.Angle);

        double worldX = state.PositionX + rotatedX;
        double worldY = state.PositionY + rotatedY;
        double worldZ = state.PositionZ + modelZ;
        double cameraZ = worldZ - camera.PositionZ;
        double depth = -cameraZ;
        if (depth <= 0.0001)
            return new Vector2(float.NaN, float.NaN);

        double f = 1.0 / Math.Tan(camera.FovYRadians * 0.5);
        double ndcX = (worldX - camera.PositionX) * f / camera.AspectRatio / depth;
        double ndcY = (worldY - camera.PositionY) * f / depth;
        return new Vector2(
            (float)((ndcX + 1.0) * 0.5 * outputWidth),
            (float)((1.0 - ndcY) * 0.5 * outputHeight));
    }

    private static Vector2 ComputeAnchorOffset(
        RenderGeometry geometry,
        TextureAnchor anchor,
        float customAnchorX,
        float customAnchorY)
    {
        float halfWidth = (float)(geometry.WidthWorld * 0.5);
        float halfHeight = (float)(geometry.HeightWorld * 0.5);
        return anchor switch
        {
            TextureAnchor.TopLeft => new Vector2(halfWidth, -halfHeight),
            TextureAnchor.MiddleCenter => Vector2.Zero,
            TextureAnchor.MiddleLeft => new Vector2(halfWidth, 0),
            TextureAnchor.MiddleRight => new Vector2(-halfWidth, 0),
            TextureAnchor.TopCenter => new Vector2(0, -halfHeight),
            TextureAnchor.TopRight => new Vector2(-halfWidth, -halfHeight),
            TextureAnchor.BottomCenter => new Vector2(0, halfHeight),
            TextureAnchor.BottomLeft => new Vector2(halfWidth, halfHeight),
            TextureAnchor.BottomRight => new Vector2(-halfWidth, halfHeight),
            TextureAnchor.Custom => new Vector2(customAnchorX, customAnchorY),
            _ => Vector2.Zero
        };
    }

    private static (float X, float Y) RotatePoint(float x, float y, float angle)
    {
        if (Math.Abs(angle) < 0.0001f)
            return (x, y);

        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        return ((float)((x * cos) - (y * sin)), (float)((x * sin) + (y * cos)));
    }

    private static void RotateX(ref double y, ref double z, float angle)
    {
        if (Math.Abs(angle) < 0.0001f)
            return;

        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double rotatedY = (y * cos) - (z * sin);
        double rotatedZ = (y * sin) + (z * cos);
        y = rotatedY;
        z = rotatedZ;
    }

    private static void RotateY(ref double x, ref double z, float angle)
    {
        if (Math.Abs(angle) < 0.0001f)
            return;

        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);
        double rotatedX = (x * cos) + (z * sin);
        double rotatedZ = (-x * sin) + (z * cos);
        x = rotatedX;
        z = rotatedZ;
    }

    internal static double EvaluateCurve(CinematicCurve curve, double localFrame)
    {
        if (curve.Keyframes.Count == 0)
            return 0;

        double frame = Math.Max(0, localFrame);
        if (frame <= curve.Keyframes[0].Time)
            return curve.Keyframes[0].Value;

        for (int i = 1; i < curve.Keyframes.Count; i++)
        {
            CinematicKeyframe previous = curve.Keyframes[i - 1];
            CinematicKeyframe current = curve.Keyframes[i];
            if (frame > current.Time)
                continue;

            double span = current.Time - previous.Time;
            if (span <= 0.0001)
                return current.Value;

            return EvaluateBezierSegment(previous, current, frame);
        }

        return curve.Keyframes[^1].Value;
    }

    internal static double GetClipLocalFrame(PropertyClip clip, double frame)
    {
        double localFrame = Math.Max(0, frame - clip.StartFrame);
        if (clip.DurationFrames > 0)
            localFrame = Math.Min(localFrame, clip.DurationFrames);

        return localFrame;
    }

    private static double EvaluateBezierSegment(CinematicKeyframe previous, CinematicKeyframe current, double frame)
    {
        double parameter = SolveBezierTime(
            frame,
            previous.Time,
            previous.RightHandleTime,
            current.LeftHandleTime,
            current.Time);

        return CubicBezier(
            previous.Value,
            previous.RightHandleValue,
            current.LeftHandleValue,
            current.Value,
            parameter);
    }

    private static double SolveBezierTime(double frame, double startTime, double outgoingHandleTime, double incomingHandleTime, double endTime)
    {
        double left = 0;
        double right = 1;
        for (int iteration = 0; iteration < 48; iteration++)
        {
            double middle = (left + right) / 2.0;
            double time = CubicBezier(startTime, outgoingHandleTime, incomingHandleTime, endTime, middle);
            if (time < frame)
                left = middle;
            else
                right = middle;
        }

        return (left + right) / 2.0;
    }

    private static double CubicBezier(double p0, double p1, double p2, double p3, double t)
    {
        double u = 1.0 - t;
        return (u * u * u * p0) +
            (3.0 * u * u * t * p1) +
            (3.0 * u * t * t * p2) +
            (t * t * t * p3);
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static float AvoidZeroScale(float value) =>
        value is > -0.00001f and < 0.00001f ? 0.00001f : value;
}