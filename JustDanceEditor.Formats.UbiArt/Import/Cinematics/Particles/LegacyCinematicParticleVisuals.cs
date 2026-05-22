using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleVisuals
{
    internal static ParticleVisual ComputeVisual(
        LegacyCinematicParticleTemplate template,
        int phaseIndex,
        LegacyCinematicParticlePhase phase,
        double phaseAge,
        double lifeFactor,
        double particleDieTimeSeconds,
        double generatorElapsedSeconds,
        string actorKey,
        int particleOrdinal,
        ParticleRandom random)
    {
        Vector2 startSize = template.Parameters.UsePhasesColorAndSize
            ? LegacyCinematicParticleVisuals.RandomSize(template.Parameters, phase, random)
            : Vector2.Zero;
        LegacyCinematicParticleColor startColor = template.Parameters.UsePhasesColorAndSize
            ? LegacyCinematicParticleVisuals.ApplyDefaultParticleColor(LegacyCinematicParticleVisuals.RandomColor(phase.ColorMin, phase.ColorMax, random), template.Parameters)
            : LegacyCinematicParticleVisuals.CreateCurveDrivenInitialColor(template);
        Vector2 endSize = startSize;
        LegacyCinematicParticleColor endColor = startColor;

        int nextPhaseIndex = phaseIndex + 1;
        if (template.Parameters.UsePhasesColorAndSize && phase.BlendToNextPhase && nextPhaseIndex < template.Phases.Count)
        {
            LegacyCinematicParticlePhase next = template.Phases[nextPhaseIndex];
            endSize = LegacyCinematicParticleVisuals.RandomSize(template.Parameters, next, random);
            endColor = LegacyCinematicParticleVisuals.ApplyDefaultParticleColor(LegacyCinematicParticleVisuals.RandomColor(next.ColorMin, next.ColorMax, random), template.Parameters);
        }
        else if (template.Parameters.UsePhasesColorAndSize && phase.BlendToNextPhase)
        {
            endSize = Vector2.Zero;
            endColor = new LegacyCinematicParticleColor(startColor.Red, startColor.Green, startColor.Blue, 0);
        }

        float factor = phase.PhaseTime > LegacyCinematicParticleSimulator.MinimumPhaseTime
            ? (float)Math.Clamp(phaseAge / phase.PhaseTime, 0, 1)
            : 0;
        Vector2 size = Vector2.Lerp(startSize, endSize, factor);
        LegacyCinematicParticleColor color = LegacyCinematicParticleVisuals.LerpColor(startColor, endColor, factor);
        LegacyCinematicParticleVisuals.ApplyLifeCurves(template, lifeFactor, particleOrdinal, particleDieTimeSeconds, ref size, ref color);
        LegacyCinematicParticleVisuals.ApplyEmitCurves(template, generatorElapsedSeconds, ref size, ref color);
        return new ParticleVisual(
            size,
            new RgbTint(color.Red, color.Green, color.Blue),
            Math.Clamp(color.Alpha, 0, 1));
    }

    internal static LegacyCinematicParticleColor CreateCurveDrivenInitialColor(LegacyCinematicParticleTemplate template)
    {
        bool hasColorCurve =
            template.Curves.Alpha.IsSet ||
            template.Curves.Rgb.IsSet ||
            template.Curves.Rgb1.IsSet ||
            template.Curves.Rgb2.IsSet ||
            template.Curves.Rgb3.IsSet;
        return new LegacyCinematicParticleColor(
            template.Parameters.DefaultColor.Red,
            template.Parameters.DefaultColor.Green,
            template.Parameters.DefaultColor.Blue,
            hasColorCurve ? template.Parameters.DefaultColor.Alpha : 0.0f);
    }

    internal static LegacyCinematicParticleColor ApplyDefaultParticleColor(
        LegacyCinematicParticleColor color,
        LegacyCinematicParticleParameters parameters) =>
        new(
            color.Red * parameters.DefaultColor.Red,
            color.Green * parameters.DefaultColor.Green,
            color.Blue * parameters.DefaultColor.Blue,
            color.Alpha * parameters.DefaultColor.Alpha);

    internal static void ApplyEmitCurves(
        LegacyCinematicParticleTemplate template,
        double generatorElapsedSeconds,
        ref Vector2 size,
        ref LegacyCinematicParticleColor color)
    {
        LegacyCinematicParticleCurves curves = template.Curves;
        if (curves.EmitSizeXy.IsSet)
        {
            Vector3 sizeFactor = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.EmitSizeXy, generatorElapsedSeconds);
            size *= new Vector2(sizeFactor.X, sizeFactor.Y);
        }

        if (curves.EmitColorFactor.IsSet)
        {
            Vector3 colorFactor = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.EmitColorFactor, generatorElapsedSeconds);
            color = color with
            {
                Red = color.Red * colorFactor.X,
                Green = color.Green * colorFactor.Y,
                Blue = color.Blue * colorFactor.Z
            };
        }

        if (curves.EmitAlpha.IsSet)
        {
            Vector3 alphaFactor = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.EmitAlpha, generatorElapsedSeconds);
            color = color with { Alpha = color.Alpha * alphaFactor.X };
        }
    }

    internal static void ApplyLifeCurves(
        LegacyCinematicParticleTemplate template,
        double lifeFactor,
        int particleOrdinal,
        double particleDieTimeSeconds,
        ref Vector2 size,
        ref LegacyCinematicParticleColor color)
    {
        LegacyCinematicParticleCurves curves = template.Curves;
        float sizeRandom = LegacyCinematicParticleCurveEvaluator.ParticlePointerLowByteUnit(particleOrdinal, 0);
        float alphaRandom = LegacyCinematicParticleCurveEvaluator.ParticlePointerLowByteUnit(particleOrdinal, (int)Math.Truncate(Math.Max(0.0, particleDieTimeSeconds)));
        if (curves.Alpha.IsSet)
        {
            Vector3 alphaRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.Alpha, lifeFactor);
            color = color with
            {
                Alpha = LegacyCinematicParticleCurveEvaluator.Lerp(
                    Math.Min(alphaRange.X, alphaRange.Y),
                    Math.Max(alphaRange.X, alphaRange.Y),
                    alphaRandom)
            };
        }

        if (curves.Rgb.IsSet)
        {
            Vector3 rgb = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.Rgb, lifeFactor);
            color = color with { Red = rgb.X, Green = rgb.Y, Blue = rgb.Z };
        }

        if (!curves.Size.IsSet)
            return;

        Vector3 sizeRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.Size, lifeFactor);
        float sizeX = LegacyCinematicParticleCurveEvaluator.Lerp(
            Math.Min(sizeRange.X, sizeRange.Y),
            Math.Max(sizeRange.X, sizeRange.Y),
            sizeRandom);
        float sizeY;
        if (template.Parameters.UniformScale != 0)
        {
            sizeY = sizeX * template.Parameters.UniformScale;
        }
        else if (curves.SizeY.IsSet)
        {
            Vector3 sizeYRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curves.SizeY, lifeFactor);
            sizeY = LegacyCinematicParticleCurveEvaluator.Lerp(
                Math.Min(sizeYRange.X, sizeYRange.Y),
                Math.Max(sizeYRange.X, sizeYRange.Y),
                sizeRandom);
        }
        else
        {
            sizeY = sizeX;
        }

        size = new Vector2(sizeX, sizeY);
    }

    internal static Vector2 RandomSize(
        LegacyCinematicParticleParameters parameters,
        LegacyCinematicParticlePhase phase,
        ParticleRandom random)
    {
        float sizeX = random.GetFloat(
            Math.Min(phase.SizeMin.X, phase.SizeMax.X),
            Math.Max(phase.SizeMin.X, phase.SizeMax.X));
        float sizeY = parameters.UniformScale != 0
            ? sizeX * parameters.UniformScale
            : random.GetFloat(
                Math.Min(phase.SizeMin.Y, phase.SizeMax.Y),
                Math.Max(phase.SizeMin.Y, phase.SizeMax.Y));

        return new Vector2(sizeX, sizeY);
    }

    internal static LegacyCinematicParticleColor RandomColor(
        LegacyCinematicParticleColor min,
        LegacyCinematicParticleColor max,
        ParticleRandom random)
    {
        float factor = random.GetFloat(0, 1);
        return LegacyCinematicParticleVisuals.LerpColor(min, max, factor);
    }

    internal static LegacyCinematicParticleColor LerpColor(
        LegacyCinematicParticleColor min,
        LegacyCinematicParticleColor max,
        float factor) =>
        new(
            LegacyCinematicParticleCurveEvaluator.Lerp(min.Red, max.Red, factor),
            LegacyCinematicParticleCurveEvaluator.Lerp(min.Green, max.Green, factor),
            LegacyCinematicParticleCurveEvaluator.Lerp(min.Blue, max.Blue, factor),
            LegacyCinematicParticleCurveEvaluator.Lerp(min.Alpha, max.Alpha, factor));

    internal static int ComputeAtlasIndex(
        LegacyCinematicParticleTemplate template,
        LegacyCinematicParticlePhase phase,
        double age,
        double phaseAge,
        double lifeTime,
        double elapsedSeconds,
        ParticleRandom random,
        string actorKey,
        int particleIndex)
    {
        // Follow/complex particle modes advance atlas animation before drawing.
        // Manual generators are not synthesized here.
        double displayedAge = age;
        double displayedPhaseAge = phaseAge;

        if (LegacyCinematicParticleVisuals.TryEvaluateParticleAtlasCurve(template.Curves.AtlasAnimation, lifeTime > LegacyCinematicParticleSimulator.MinimumPhaseTime ? displayedAge / lifeTime : 0.0, out int curveAtlasIndex))
            return curveAtlasIndex;

        if (LegacyCinematicParticleVisuals.TryEvaluateEmitAtlasCurve(template.Curves.EmitAtlasAnimation, elapsedSeconds, random, out int emitCurveAtlasIndex))
            return emitCurveAtlasIndex;

        int start = template.Animation.StartAnimIndex;
        int end = template.Animation.EndAnimIndex;
        bool useRandom = template.Animation.UseUvRandom;
        bool stretchTime = false;
        double phaseTime = phase.PhaseTime;

        if (!template.Animation.HasAtlasAnimation)
        {
            if (phase.AnimStart < 0 || phase.AnimEnd < 0)
                return -1;

            start = phase.AnimStart;
            end = phase.AnimEnd;
            stretchTime = phase.AnimStretchTime;
        }

        int frameCount = Math.Abs(end - start) + 1;
        if (frameCount <= 0)
            return -1;

        int offset;
        if (useRandom)
        {
            offset = Math.Abs(LegacyCinematicParticleCurveEvaluator.StablePositiveHash($"{actorKey}:{particleIndex}:{random.Seed}")) % frameCount;
        }
        else if (stretchTime && phaseTime > LegacyCinematicParticleSimulator.MinimumPhaseTime)
        {
            offset = Math.Clamp((int)Math.Floor(displayedPhaseAge / phaseTime * frameCount), 0, frameCount - 1);
        }
        else
        {
            double frequency = Math.Abs(template.Animation.AnimUvFrequency) > 0.0001
                ? Math.Abs(template.Animation.AnimUvFrequency)
                : 1.0;
            offset = (int)Math.Floor(displayedPhaseAge * frequency) % frameCount;
        }

        return end < start
            ? start - offset
            : start + offset;
    }

    internal static bool TryEvaluateParticleAtlasCurve(
        LegacyCinematicParticleCurve curve,
        double particleLifeFactor,
        out int atlasIndex)
    {
        atlasIndex = -1;
        if (!curve.IsSet)
            return false;

        Vector3 value = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curve, particleLifeFactor);
        atlasIndex = (int)value.X;
        return atlasIndex >= 0;
    }

    internal static bool TryEvaluateEmitAtlasCurve(
        LegacyCinematicParticleCurve curve,
        double emissionTimeSeconds,
        ParticleRandom random,
        out int atlasIndex)
    {
        atlasIndex = -1;
        if (!curve.IsSet)
            return false;

        Vector3 value = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(curve, emissionTimeSeconds);
        float min = Math.Min(value.X, value.Y);
        float max = Math.Max(value.X, value.Y);
        atlasIndex = (int)random.GetFloat(min, max);
        return atlasIndex >= 0;
    }
}