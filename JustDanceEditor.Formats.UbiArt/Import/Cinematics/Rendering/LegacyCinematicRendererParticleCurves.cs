using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicRendererParticleCurves
{
    internal static bool TryGetParticleAtlasFrame(
        LegacyCinematicParticleTemplate template,
        string actorKey,
        double elapsedSeconds,
        out int atlasIndex)
    {
        atlasIndex = -1;
        if (LegacyCinematicRendererParticleCurves.TryGetParticleCurveAtlasFrame(template.Curves.AtlasAnimation, elapsedSeconds, out atlasIndex))
            return true;

        int start = template.Animation.StartAnimIndex;
        int end = template.Animation.EndAnimIndex;
        bool stretchTime = false;
        double phaseTime = 0.0;

        if (!template.Animation.HasAtlasAnimation)
        {
            LegacyCinematicParticlePhase? phase = template.Phases.FirstOrDefault(item => item.AnimStart >= 0 && item.AnimEnd >= 0);
            if (phase == null)
                return false;

            start = phase.AnimStart;
            end = phase.AnimEnd;
            stretchTime = phase.AnimStretchTime;
            phaseTime = phase.PhaseTime;
        }

        if (start < 0 || end < 0)
            return false;

        int frameCount = Math.Abs(end - start) + 1;
        if (frameCount <= 0)
            return false;

        if (template.Animation.UseUvRandom)
        {
            atlasIndex = start + (LegacyCinematicRendererParticleCurves.StablePositiveHash(actorKey) % frameCount);
            if (end < start)
                atlasIndex = start - (LegacyCinematicRendererParticleCurves.StablePositiveHash(actorKey) % frameCount);

            return true;
        }

        int offset;
        if (stretchTime && phaseTime > 0.0001)
        {
            double normalized = elapsedSeconds % phaseTime / phaseTime;
            offset = Math.Clamp((int)Math.Floor(normalized * frameCount), 0, frameCount - 1);
        }
        else
        {
            double frequency = Math.Abs(template.Animation.AnimUvFrequency) > 0.0001
                ? Math.Abs(template.Animation.AnimUvFrequency)
                : 1.0;
            offset = (int)Math.Floor(Math.Max(0.0, elapsedSeconds) * frequency) % frameCount;
        }

        atlasIndex = end < start
            ? start - offset
            : start + offset;
        return true;
    }

    internal static bool TryGetParticleCurveAtlasFrame(
        LegacyCinematicParticleCurve curve,
        double elapsedSeconds,
        out int atlasIndex)
    {
        atlasIndex = -1;
        if (!curve.IsSet)
            return false;

        double scaledTime = curve.BaseTime > 0.0001f
            ? elapsedSeconds / curve.BaseTime
            : elapsedSeconds;
        Vector3 splineValue = LegacyCinematicRendererParticleCurves.EvaluateParticleCurveSpline(curve.Points, (float)scaledTime);
        float value = curve.OutputMin.X + ((curve.OutputMax.X - curve.OutputMin.X) * splineValue.X);
        atlasIndex = (int)value;
        return atlasIndex >= 0;
    }

    internal static Vector3 EvaluateParticleCurveSpline(
        IReadOnlyList<LegacyCinematicParticleCurvePoint> points,
        float time)
    {
        if (points.Count == 0)
            return new Vector3(Math.Clamp(time, 0.0f, 1.0f));

        if (points.Count == 1 ||
            time < points[0].Time)
        {
            return points[0].Value;
        }

        if (time >= points[^1].Time)
            return points[^1].Value;

        for (int index = 1; index < points.Count; index++)
        {
            LegacyCinematicParticleCurvePoint previous = points[index - 1];
            LegacyCinematicParticleCurvePoint next = points[index];
            if (time > next.Time)
                continue;

            float span = next.Time - previous.Time;
            if (Math.Abs(span) <= 0.000001f)
                return next.Value;

            float factor = Math.Clamp((time - previous.Time) / span, 0.0f, 1.0f);
            return previous.Interpolation switch
            {
                LegacyCinematicFrameRenderer.SplineInterpolationConstant => factor >= 1.0f ? next.Value : previous.Value,
                LegacyCinematicFrameRenderer.SplineInterpolationSpline => LegacyCinematicRendererParticleCurves.EvaluateCatmullRom(
                    points[Math.Max(0, index - 2)].Value,
                    previous.Value,
                    next.Value,
                    points[Math.Min(points.Count - 1, index + 1)].Value,
                    factor),
                LegacyCinematicFrameRenderer.SplineInterpolationBezier => LegacyCinematicRendererParticleCurves.EvaluateTimeBezier(previous, next, time),
                LegacyCinematicFrameRenderer.SplineInterpolationBezierStandard => LegacyCinematicRendererParticleCurves.CubicBezier(
                    previous.Value,
                    previous.Value + previous.NormalOut,
                    next.Value - next.NormalIn,
                    next.Value,
                    factor),
                _ => Vector3.Lerp(previous.Value, next.Value, factor)
            };
        }

        return points[^1].Value;
    }

    internal static Vector3 EvaluateCatmullRom(
        Vector3 p1,
        Vector3 p2,
        Vector3 p3,
        Vector3 p4,
        float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        float b1 = 0.5f * (-t3 + (2.0f * t2) - t);
        float b2 = 0.5f * ((3.0f * t3) - (5.0f * t2) + 2.0f);
        float b3 = 0.5f * ((-3.0f * t3) + (4.0f * t2) + t);
        float b4 = 0.5f * (t3 - t2);
        return (p1 * b1) + (p2 * b2) + (p3 * b3) + (p4 * b4);
    }

    internal static Vector3 EvaluateTimeBezier(
        LegacyCinematicParticleCurvePoint previous,
        LegacyCinematicParticleCurvePoint next,
        float time) =>
        new(
            LegacyCinematicRendererParticleCurves.CubicBezierY(
                new Vector2(previous.Time, previous.Value.X),
                new Vector2(previous.Time + previous.NormalOutTime.X, previous.Value.X + previous.NormalOut.X),
                new Vector2(next.Time - next.NormalInTime.X, next.Value.X - next.NormalIn.X),
                new Vector2(next.Time, next.Value.X),
                time),
            LegacyCinematicRendererParticleCurves.CubicBezierY(
                new Vector2(previous.Time, previous.Value.Y),
                new Vector2(previous.Time + previous.NormalOutTime.Y, previous.Value.Y + previous.NormalOut.Y),
                new Vector2(next.Time - next.NormalInTime.Y, next.Value.Y - next.NormalIn.Y),
                new Vector2(next.Time, next.Value.Y),
                time),
            LegacyCinematicRendererParticleCurves.CubicBezierY(
                new Vector2(previous.Time, previous.Value.Z),
                new Vector2(previous.Time + previous.NormalOutTime.Z, previous.Value.Z + previous.NormalOut.Z),
                new Vector2(next.Time - next.NormalInTime.Z, next.Value.Z - next.NormalIn.Z),
                new Vector2(next.Time, next.Value.Z),
                time));

    internal static float CubicBezierY(
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        Vector2 p4,
        float x)
    {
        float begin = 0.0f;
        float end = 1.0f;
        float t = 0.5f;
        for (int counter = 0; counter < 20; counter++)
        {
            t = (begin + end) * 0.5f;
            float px = LegacyCinematicRendererParticleCurves.CubicBezier(p1.X, p2.X, p3.X, p4.X, t);
            if (px - x > 0.0f)
                end = t;
            else
                begin = t;
        }

        return LegacyCinematicRendererParticleCurves.CubicBezier(p1.Y, p2.Y, p3.Y, p4.Y, t);
    }

    internal static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float tInv = 1.0f - t;
        return
            (p0 * (tInv * tInv * tInv)) +
            (p1 * (3.0f * t * tInv * tInv)) +
            (p2 * (3.0f * t * t * tInv)) +
            (p3 * (t * t * t));
    }

    internal static float CubicBezier(float p0, float p1, float p2, float p3, float t)
    {
        float tInv = 1.0f - t;
        return
            (p0 * (tInv * tInv * tInv)) +
            (p1 * (3.0f * t * tInv * tInv)) +
            (p2 * (3.0f * t * t * tInv)) +
            (p3 * (t * t * t));
    }

    internal static int StablePositiveHash(string value)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char character in value)
                hash = ((hash << 5) + hash) ^ char.ToLowerInvariant(character);

            return hash & 0x7FFFFFFF;
        }
    }

    internal static bool IsFullUvRect(LegacyCinematicUvRect rect) =>
        Math.Abs(rect.Left) < 0.00001f &&
        Math.Abs(rect.Top) < 0.00001f &&
        Math.Abs(rect.Right - 1.0f) < 0.00001f &&
        Math.Abs(rect.Bottom - 1.0f) < 0.00001f;

}