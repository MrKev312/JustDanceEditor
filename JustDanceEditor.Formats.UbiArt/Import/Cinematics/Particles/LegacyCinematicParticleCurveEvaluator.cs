using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleCurveEvaluator
{
    internal static double ComputeEmissionInterval(
        LegacyCinematicParticleTemplate template,
        double elapsedSeconds,
        ParticleRandom random)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        if (!template.Curves.Frequency.IsSet ||
            parameters.GeneratorEmitMode == LegacyCinematicParticleSimulator.EmitModeAllAtOnce)
        {
            float interval = parameters.Frequency + random.GetFloat(0.0f, Math.Max(0.0f, parameters.FrequencyDelta));
            return interval > LegacyCinematicParticleSimulator.MinimumEmissionInterval
                ? Math.Max(LegacyCinematicParticleSimulator.MinimumEmissionInterval, interval)
                : double.PositiveInfinity;
        }

        Vector3 frequencyRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.Frequency, elapsedSeconds);
        double frequency = random.GetFloat(
            Math.Min(frequencyRange.X, frequencyRange.Y),
            Math.Max(frequencyRange.X, frequencyRange.Y));
        return frequency > LegacyCinematicParticleSimulator.MinimumEmissionInterval
            ? Math.Max(LegacyCinematicParticleSimulator.MinimumEmissionInterval, 1.0 / frequency)
            : double.PositiveInfinity;
    }

    internal static Vector3 EvaluateParticleCurve(LegacyCinematicParticleCurve curve, double time)
    {
        double scaledTime = curve.BaseTime > LegacyCinematicParticleSimulator.MinimumPhaseTime
            ? time / curve.BaseTime
            : time;

        Vector3 splineValue = LegacyCinematicParticleCurveEvaluator.EvaluateSplineXyz(curve.Points, (float)scaledTime);
        return new Vector3(
            LegacyCinematicParticleCurveEvaluator.Lerp(curve.OutputMin.X, curve.OutputMax.X, splineValue.X),
            LegacyCinematicParticleCurveEvaluator.Lerp(curve.OutputMin.Y, curve.OutputMax.Y, splineValue.Y),
            LegacyCinematicParticleCurveEvaluator.Lerp(curve.OutputMin.Z, curve.OutputMax.Z, splineValue.Z));
    }

    internal static Vector3 EvaluateSplineXyz(
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
                LegacyCinematicParticleSimulator.SplineInterpolationConstant => factor >= 1.0f ? next.Value : previous.Value,
                LegacyCinematicParticleSimulator.SplineInterpolationSpline => LegacyCinematicParticleCurveEvaluator.EvaluateCatmullRom(
                    points[Math.Max(0, index - 2)].Value,
                    previous.Value,
                    next.Value,
                    points[Math.Min(points.Count - 1, index + 1)].Value,
                    factor),
                LegacyCinematicParticleSimulator.SplineInterpolationBezier => LegacyCinematicParticleCurveEvaluator.EvaluateTimeBezier(previous, next, time),
                LegacyCinematicParticleSimulator.SplineInterpolationBezierStandard => LegacyCinematicParticleCurveEvaluator.CubicBezier(
                    previous.Value,
                    previous.Value + previous.NormalOut,
                    next.Value - next.NormalIn,
                    next.Value,
                    factor),
                LegacyCinematicParticleSimulator.SplineInterpolationLinear or _ => Vector3.Lerp(previous.Value, next.Value, factor)
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
            LegacyCinematicParticleCurveEvaluator.CubicBezierY(
                new Vector2(previous.Time, previous.Value.X),
                new Vector2(previous.Time + previous.NormalOutTime.X, previous.Value.X + previous.NormalOut.X),
                new Vector2(next.Time - next.NormalInTime.X, next.Value.X - next.NormalIn.X),
                new Vector2(next.Time, next.Value.X),
                time),
            LegacyCinematicParticleCurveEvaluator.CubicBezierY(
                new Vector2(previous.Time, previous.Value.Y),
                new Vector2(previous.Time + previous.NormalOutTime.Y, previous.Value.Y + previous.NormalOut.Y),
                new Vector2(next.Time - next.NormalInTime.Y, next.Value.Y - next.NormalIn.Y),
                new Vector2(next.Time, next.Value.Y),
                time),
            LegacyCinematicParticleCurveEvaluator.CubicBezierY(
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
            float px = LegacyCinematicParticleCurveEvaluator.CubicBezier(p1.X, p2.X, p3.X, p4.X, t);
            if (px - x > 0.0f)
                end = t;
            else
                begin = t;
        }

        return LegacyCinematicParticleCurveEvaluator.CubicBezier(p1.Y, p2.Y, p3.Y, p4.Y, t);
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

    internal static ResolvedActorState CreateParticleState(
        ResolvedActorState actorState,
        LegacyCinematicParticleParameters parameters,
        SimulatedParticle particle,
        Vector2 atlasSizeScale,
        bool isFxEmitter,
        FxPlaybackOptions playbackOptions)
    {
        Vector2 basePosition = new(actorState.PositionX, actorState.PositionY);
        if (isFxEmitter && parameters.PositionOffset != Vector3.Zero)
            basePosition += LegacyCinematicParticleCurveEvaluator.Rotate(new Vector2(parameters.PositionOffset.X, parameters.PositionOffset.Y), actorState.Angle);
        if (parameters.UseActorTranslation)
        {
            // Actor-translation mode applies a local, actor-scale-adjusted offset
            // before particle-local transforms.
            basePosition += new Vector2(
                parameters.ActorTranslationOffset.X * actorState.ScaleX,
                parameters.ActorTranslationOffset.Y * actorState.ScaleY);
        }

        Vector2 localPosition = particle.Position;
        float scaleX = particle.Size.X * atlasSizeScale.X * 0.5f;
        float scaleY = particle.Size.Y * atlasSizeScale.Y * 0.5f;
        float angle = particle.Angle;
        if (parameters.UseMatrix != 0)
        {
            localPosition = LegacyCinematicParticleCurveEvaluator.Rotate(
                new Vector2(localPosition.X * actorState.ScaleX, localPosition.Y * actorState.ScaleY),
                actorState.Angle);
            scaleX *= Math.Abs(actorState.ScaleX);
            scaleY *= Math.Abs(actorState.ScaleY);
            angle += actorState.Angle;
        }

        float x = basePosition.X + localPosition.X;
        float y = basePosition.Y + localPosition.Y;
        float z = actorState.PositionZ + particle.PositionZ + (isFxEmitter ? parameters.PositionOffset.Z : 0.0f);

        return actorState with
        {
            PositionX = x,
            PositionY = y,
            PositionZ = z,
            ScaleX = scaleX,
            ScaleY = scaleY,
            Angle = angle,
            Alpha = (isFxEmitter && !playbackOptions.UseActorAlpha ? 1.0f : actorState.Alpha) * particle.Alpha,
            Tint = actorState.Tint.Multiply(particle.Tint),
            XFlipped = false
        };
    }

    internal static Vector2 Rotate(Vector2 value, float radians)
    {
        if (Math.Abs(radians) <= 0.000001f)
            return value;

        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector2(
            (value.X * cos) - (value.Y * sin),
            (value.X * sin) + (value.Y * cos));
    }

    internal static Vector2 ComputeAtlasSizeScale(
        LegacyCinematicParticleParameters parameters,
        LegacyCinematicUvRect? uvOverride)
    {
        if (!parameters.GetAtlasSize || uvOverride is not { } uv)
            return Vector2.One;

        float scaleX = Math.Abs(uv.Right - uv.Left);
        float scaleY = Math.Abs(uv.Bottom - uv.Top);
        return new Vector2(
            Math.Max(0.0001f, scaleX),
            Math.Max(0.0001f, scaleY));
    }

    internal static float ComputeParticleSortDepth(
        ResolvedActorState actorState,
        LegacyCinematicParticleParameters parameters)
    {
        // Keep sort depth separate from projected particle Z so render priority
        // changes ordering, not size.
        float depth = parameters.UseZAsDepth
            ? actorState.PositionZ + parameters.PositionOffset.Z
            : parameters.Depth;
        return depth + parameters.RenderPriority;
    }

    internal static bool HasArea(Vector2 min, Vector2 max) =>
        Math.Abs(max.X - min.X) > 0.0001f ||
        Math.Abs(max.Y - min.Y) > 0.0001f;

    internal static double PositiveModulo(double value, double divisor)
    {
        if (divisor <= 0)
            return 0;

        double result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    internal static float Lerp(float min, float max, float factor) =>
        min + ((max - min) * factor);

    internal static float DegreesToRadians(float degrees) =>
        degrees * (MathF.PI / 180.0f);

    internal static int ComputeParticleSeed(string actorKey, int particleIndex) =>
        Math.Max(1, LegacyCinematicParticleCurveEvaluator.StablePositiveHash($"{actorKey}:{particleIndex}") % 2147483646);

    internal static float StableUnitRandom(string actorKey, int particleIndex, string salt) =>
        (LegacyCinematicParticleCurveEvaluator.StablePositiveHash($"{actorKey}:{particleIndex}:{salt}") & 0x00FFFFFF) * (1.0f / 0x01000000);

    internal static float ParticlePointerLowByteUnit(int particleIndex, int byteOffset)
    {
        // The offline renderer has no stable runtime pool address, so use a
        // deterministic aligned-stride surrogate for pointer-derived variation.
        int lowByte = unchecked(((particleIndex * LegacyCinematicParticleSimulator.ParticleStructStrideBytes) + byteOffset) & 0xFF);
        return lowByte * (1.0f / 255.0f);
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
}