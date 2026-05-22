using System.Numerics;
using System.Runtime.CompilerServices;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleMotion
{
    private const int MaxCachedParticleMotionStates = 20000;
    private static readonly Lock MotionCacheLock = new();
    private static readonly Dictionary<ParticleMotionCacheKey, ParticleMotionCacheEntry> MotionCache = [];

    internal static float ComputeInitialSpeed(
        LegacyCinematicParticleTemplate template,
        double eventTimeSeconds,
        ParticleRandom random)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        if (template.Curves.EmitVelocity.IsSet)
        {
            Vector3 velocityRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.EmitVelocity, eventTimeSeconds);
            return random.GetFloat(
                Math.Min(velocityRange.X, velocityRange.Y),
                Math.Max(velocityRange.X, velocityRange.Y));
        }

        return parameters.VelocityNorm + random.GetFloat(0, Math.Max(0, parameters.VelocityVar));
    }

    internal static Vector2 ComputeInitialPosition(
        LegacyCinematicParticleParameters parameters,
        ParticleRandom random)
    {
        return parameters.GenerationType switch
        {
            LegacyCinematicParticleSimulator.GenerationRectangle => LegacyCinematicParticleMotion.ComputeRectanglePosition(parameters, random),
            LegacyCinematicParticleSimulator.GenerationCircle or LegacyCinematicParticleSimulator.GenerationSphere or LegacyCinematicParticleSimulator.GenerationHemisphere => LegacyCinematicParticleMotion.ComputeCirclePosition(parameters, random),
            LegacyCinematicParticleSimulator.GenerationPoints or _ => Vector2.Zero
        };
    }

    internal static Vector2 ComputeRectanglePosition(
        LegacyCinematicParticleParameters parameters,
        ParticleRandom random)
    {
        // Rectangle generation consumes an angle-range draw before local X/Y.
        // Keep that RNG draw or every following rectangle particle coordinate
        // comes from the wrong point in the stream.
        _ = random.GetFloat(parameters.GenerationAngleMin, parameters.GenerationAngleMax);
        Vector2 min = parameters.GenerationBox.Min;
        Vector2 max = parameters.GenerationBox.Max;
        return new Vector2(
            random.GetFloat(Math.Min(min.X, max.X), Math.Max(min.X, max.X)),
            random.GetFloat(Math.Min(min.Y, max.Y), Math.Max(min.Y, max.Y)));
    }

    internal static Vector2 ComputeCirclePosition(
        LegacyCinematicParticleParameters parameters,
        ParticleRandom random)
    {
        float angle = random.GetFloat(parameters.GenerationAngleMin, parameters.GenerationAngleMax);
        float radius = random.GetFloat(
            Math.Min(parameters.InnerCircleRadius, parameters.CircleRadius),
            Math.Max(parameters.InnerCircleRadius, parameters.CircleRadius));
        Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
        return new Vector2(
            direction.X * parameters.ScaleShape.X,
            direction.Y * parameters.ScaleShape.Y) * radius;
    }

    internal static Vector2 ComputeInitialVelocity(
        LegacyCinematicParticleTemplate template,
        Vector2 initialPosition,
        float actorAngle,
        bool isFxEmitter,
        double eventTimeSeconds,
        ParticleRandom random,
        float speed)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        if (Math.Abs(speed) <= 0.0001f)
            return Vector2.Zero;

        Vector2 direction;
        if (parameters.RandomizeDirection && initialPosition.LengthSquared() > 0.000001f)
        {
            direction = Vector2.Normalize(initialPosition);
        }
        else
        {
            float directionAngle = isFxEmitter && parameters.UseMatrix != 0
                ? 0.0f
                : actorAngle;
            if (parameters.VelocityAngle != 0.0f)
            {
                directionAngle += LegacyCinematicParticleCurveEvaluator.DegreesToRadians(parameters.VelocityAngle + random.GetFloat(
                    -Math.Abs(parameters.VelocityAngleDelta),
                    Math.Abs(parameters.VelocityAngleDelta)));
            }

            if (template.Curves.EmitVelocityAngle.IsSet)
            {
                Vector3 angleRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.EmitVelocityAngle, eventTimeSeconds);
                directionAngle = LegacyCinematicParticleCurveEvaluator.DegreesToRadians(random.GetFloat(
                    Math.Min(angleRange.X, angleRange.Y),
                    Math.Max(angleRange.X, angleRange.Y)));
            }

            direction = new Vector2(MathF.Cos(directionAngle), MathF.Sin(directionAngle));
        }

        return direction * speed;
    }

    internal static float ComputeInitialParticleAngle(
        LegacyCinematicParticleTemplate template,
        double updateStartTimeSeconds,
        ParticleRandom random)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        float initialAngle =
            parameters.InitAngle +
            random.GetFloat(-Math.Abs(parameters.AngleDelta), Math.Abs(parameters.AngleDelta));
        if (!template.Curves.EmitAngle.IsSet)
            return initialAngle;

        Vector3 angleRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.EmitAngle, updateStartTimeSeconds);
        return -LegacyCinematicParticleCurveEvaluator.DegreesToRadians(random.GetFloat(
            Math.Min(angleRange.X, angleRange.Y),
            Math.Max(angleRange.X, angleRange.Y)));
    }

    internal static float ComputeInitialParticleAngularSpeed(
        LegacyCinematicParticleTemplate template,
        double updateStartTimeSeconds,
        ParticleRandom random)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        float angularSpeed = parameters.AngularSpeed +
            random.GetFloat(-Math.Abs(parameters.AngularSpeedDelta), Math.Abs(parameters.AngularSpeedDelta));
        if (!template.Curves.EmitAngularSpeed.IsSet)
            return angularSpeed;

        Vector3 speedRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.EmitAngularSpeed, updateStartTimeSeconds);
        return LegacyCinematicParticleCurveEvaluator.DegreesToRadians(random.GetFloat(
            Math.Min(speedRange.X, speedRange.Y),
            Math.Max(speedRange.X, speedRange.Y)));
    }

    internal static Vector2 IntegrateParticlePosition(
        LegacyCinematicParticleTemplate template,
        Vector2 initialPosition,
        Vector2 initialVelocity,
        Vector2 baseAcceleration,
        double age,
        double particleLifeTime,
        string actorKey,
        int particleOrdinal)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        bool needsStepIntegration =
            template.Curves.VelocityMult.IsSet ||
            template.Curves.AccelerationX.IsSet ||
            template.Curves.AccelerationY.IsSet ||
            Math.Abs(parameters.Friction - 1.0f) > 0.000001f;
        if (!needsStepIntegration)
            return initialPosition + (initialVelocity * (float)age) + (baseAcceleration * (0.5f * (float)(age * age)));

        Vector2 position = initialPosition;
        Vector2 velocity = initialVelocity;
        Vector2 accumulatedAcceleration = Vector2.Zero;
        float velocityMultRandom = LegacyCinematicParticleCurveEvaluator.StableUnitRandom(actorKey, particleOrdinal, "velocity_mult");
        float accelerationXRandom = LegacyCinematicParticleCurveEvaluator.StableUnitRandom(actorKey, particleOrdinal, "accel_x");
        float accelerationYRandom = LegacyCinematicParticleCurveEvaluator.StableUnitRandom(actorKey, particleOrdinal, "accel_y");
        double integrationStep = LegacyCinematicParticleSimulator.ParticleIntegrationStepSeconds;
        int fullStepCount = (int)Math.Floor(age / integrationStep);
        double fullStepAge = fullStepCount * integrationStep;
        ParticleMotionCacheKey cacheKey = new(RuntimeHelpers.GetHashCode(template), actorKey, particleOrdinal);
        ParticleMotionCacheEntry? cacheEntry = LegacyCinematicParticleMotion.TryGetReusableMotionCacheEntry(
            cacheKey,
            initialPosition,
            initialVelocity,
            baseAcceleration,
            particleLifeTime,
            fullStepAge);
        double time = 0.0;
        if (cacheEntry != null)
        {
            position = cacheEntry.Position;
            velocity = cacheEntry.Velocity;
            accumulatedAcceleration = cacheEntry.AccumulatedAcceleration;
            time = cacheEntry.FullStepAge;
        }

        while (time < fullStepAge - LegacyCinematicParticleSimulator.MinimumPhaseTime)
        {
            LegacyCinematicParticleMotion.AdvanceParticleMotion(
                template,
                parameters,
                baseAcceleration,
                particleLifeTime,
                accelerationXRandom,
                accelerationYRandom,
                velocityMultRandom,
                integrationStep,
                ref time,
                ref position,
                ref velocity,
                ref accumulatedAcceleration);
        }

        LegacyCinematicParticleMotion.StoreMotionCacheEntry(
            cacheKey,
            initialPosition,
            initialVelocity,
            baseAcceleration,
            particleLifeTime,
            fullStepAge,
            position,
            velocity,
            accumulatedAcceleration);

        double remainder = age - fullStepAge;
        if (remainder > LegacyCinematicParticleSimulator.MinimumPhaseTime)
        {
            LegacyCinematicParticleMotion.AdvanceParticleMotion(
                template,
                parameters,
                baseAcceleration,
                particleLifeTime,
                accelerationXRandom,
                accelerationYRandom,
                velocityMultRandom,
                remainder,
                ref time,
                ref position,
                ref velocity,
                ref accumulatedAcceleration);
        }

        return position;
    }

    private static void AdvanceParticleMotion(
        LegacyCinematicParticleTemplate template,
        LegacyCinematicParticleParameters parameters,
        Vector2 baseAcceleration,
        double particleLifeTime,
        float accelerationXRandom,
        float accelerationYRandom,
        float velocityMultRandom,
        double step,
        ref double time,
        ref Vector2 position,
        ref Vector2 velocity,
        ref Vector2 accumulatedAcceleration)
    {
        double nextTime = time + step;
        double lifeFactor = particleLifeTime > LegacyCinematicParticleSimulator.MinimumPhaseTime
            ? Math.Clamp(nextTime / particleLifeTime, 0.0, 1.0)
            : 1.0;

        Vector2 acceleration = baseAcceleration + LegacyCinematicParticleMotion.EvaluateParticleAccelerationCurves(
            template,
            lifeFactor,
            accelerationXRandom,
            accelerationYRandom);
        accumulatedAcceleration += acceleration * (float)step;

        velocity *= parameters.Friction;
        Vector2 stepVelocity = velocity;
        if (template.Curves.VelocityMult.IsSet)
        {
            Vector3 range = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.VelocityMult, lifeFactor);
            float multiplier = LegacyCinematicParticleCurveEvaluator.Lerp(
                Math.Min(range.X, range.Y),
                Math.Max(range.X, range.Y),
                velocityMultRandom);
            stepVelocity *= multiplier;
        }

        position += (stepVelocity + accumulatedAcceleration) * (float)step;
        time = nextTime;
    }

    private static ParticleMotionCacheEntry? TryGetReusableMotionCacheEntry(
        ParticleMotionCacheKey key,
        Vector2 initialPosition,
        Vector2 initialVelocity,
        Vector2 baseAcceleration,
        double particleLifeTime,
        double targetFullStepAge)
    {
        lock (MotionCacheLock)
        {
            if (!MotionCache.TryGetValue(key, out ParticleMotionCacheEntry? entry) ||
                entry.FullStepAge > targetFullStepAge ||
                !entry.Matches(initialPosition, initialVelocity, baseAcceleration, particleLifeTime))
            {
                return null;
            }

            return entry;
        }
    }

    private static void StoreMotionCacheEntry(
        ParticleMotionCacheKey key,
        Vector2 initialPosition,
        Vector2 initialVelocity,
        Vector2 baseAcceleration,
        double particleLifeTime,
        double fullStepAge,
        Vector2 position,
        Vector2 velocity,
        Vector2 accumulatedAcceleration)
    {
        lock (MotionCacheLock)
        {
            if (MotionCache.Count > MaxCachedParticleMotionStates)
                MotionCache.Clear();

            MotionCache[key] = new ParticleMotionCacheEntry(
                initialPosition,
                initialVelocity,
                baseAcceleration,
                particleLifeTime,
                fullStepAge,
                position,
                velocity,
                accumulatedAcceleration);
        }
    }

    internal static Vector2 EvaluateParticleAccelerationCurves(
        LegacyCinematicParticleTemplate template,
        double lifeFactor,
        float accelerationXRandom,
        float accelerationYRandom)
    {
        Vector2 acceleration = Vector2.Zero;
        if (template.Curves.AccelerationX.IsSet)
        {
            Vector3 range = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.AccelerationX, lifeFactor);
            acceleration.X += LegacyCinematicParticleCurveEvaluator.Lerp(
                Math.Min(range.X, range.Y),
                Math.Max(range.X, range.Y),
                accelerationXRandom);
        }

        if (template.Curves.AccelerationY.IsSet)
        {
            Vector3 range = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.AccelerationY, lifeFactor);
            acceleration.Y += LegacyCinematicParticleCurveEvaluator.Lerp(
                Math.Min(range.X, range.Y),
                Math.Max(range.X, range.Y),
                accelerationYRandom);
        }

        return acceleration;
    }

    internal static void ApplyEmitAccelerationCurves(
        LegacyCinematicParticleTemplate template,
        double elapsedSeconds,
        ref Vector2 acceleration)
    {
        if (template.Curves.EmitAcceleration.IsSet)
        {
            Vector3 value = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.EmitAcceleration, elapsedSeconds);
            acceleration += new Vector2(value.X, value.Y);
        }

        if (template.Curves.EmitGravity.IsSet)
        {
            Vector3 value = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.EmitGravity, elapsedSeconds);
            acceleration += new Vector2(value.X, value.Y);
        }
    }

    internal static float ComputeParticleAngle(
        LegacyCinematicParticleTemplate template,
        double lifeFactor,
        double age,
        double particleDieTimeSeconds,
        int particleOrdinal,
        ParticleSpawnState spawn)
    {
        if (template.Curves.Angle.IsSet)
        {
            Vector3 angleRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.Angle, lifeFactor);
            float angleRandom = LegacyCinematicParticleCurveEvaluator.ParticlePointerLowByteUnit(
                particleOrdinal,
                (int)Math.Truncate(Math.Max(0.0, particleDieTimeSeconds)));
            return LegacyCinematicParticleCurveEvaluator.DegreesToRadians(LegacyCinematicParticleCurveEvaluator.Lerp(
                Math.Min(angleRange.X, angleRange.Y),
                Math.Max(angleRange.X, angleRange.Y),
                angleRandom));
        }

        return spawn.InitialAngle + (spawn.AngularSpeed * (float)age);
    }

    private readonly record struct ParticleMotionCacheKey(
        int TemplateId,
        string ActorKey,
        int ParticleOrdinal);

    private sealed class ParticleMotionCacheEntry(
        Vector2 initialPosition,
        Vector2 initialVelocity,
        Vector2 baseAcceleration,
        double particleLifeTime,
        double fullStepAge,
        Vector2 position,
        Vector2 velocity,
        Vector2 accumulatedAcceleration)
    {
        public Vector2 InitialPosition { get; } = initialPosition;
        public Vector2 InitialVelocity { get; } = initialVelocity;
        public Vector2 BaseAcceleration { get; } = baseAcceleration;
        public double ParticleLifeTime { get; } = particleLifeTime;
        public double FullStepAge { get; } = fullStepAge;
        public Vector2 Position { get; } = position;
        public Vector2 Velocity { get; } = velocity;
        public Vector2 AccumulatedAcceleration { get; } = accumulatedAcceleration;

        public bool Matches(
            Vector2 initialPosition,
            Vector2 initialVelocity,
            Vector2 baseAcceleration,
            double particleLifeTime) =>
            LegacyCinematicParticleMotion.NearlyEqual(InitialPosition, initialPosition) &&
            LegacyCinematicParticleMotion.NearlyEqual(InitialVelocity, initialVelocity) &&
            LegacyCinematicParticleMotion.NearlyEqual(BaseAcceleration, baseAcceleration) &&
            Math.Abs(ParticleLifeTime - particleLifeTime) <= 0.000001;
    }

    private static bool NearlyEqual(Vector2 left, Vector2 right) =>
        Math.Abs(left.X - right.X) <= 0.000001f &&
        Math.Abs(left.Y - right.Y) <= 0.000001f;

}