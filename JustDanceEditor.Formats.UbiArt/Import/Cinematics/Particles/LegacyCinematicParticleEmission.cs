using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleEmission
{
    internal static IReadOnlyList<SimulatedParticle> Simulate(
        string actorKey,
        LegacyCinematicParticleTemplate template,
        ResolvedActorState actorState,
        double elapsedSeconds,
        double? generationEndSeconds,
        bool isFxEmitter,
        FxPlaybackOptions playbackOptions)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        if (parameters.MaxParticles == 0 || parameters.GeneratorMode == LegacyCinematicParticleSimulator.GeneratorModeManual)
            return [];

        double lifeTime = LegacyCinematicParticleEmission.ComputeMaxLifeTime(template);
        bool allowPhaseLoop = !generationEndSeconds.HasValue && template.Animation.Loop;
        IReadOnlyList<EmissionSample> emissions = LegacyCinematicParticleEmission.ComputeActiveEmissionSamples(
            actorKey,
            template,
            lifeTime,
            elapsedSeconds,
            generationEndSeconds,
            allowPhaseLoop);
        if (emissions.Count == 0)
            return [];

        List<SimulatedParticle> particles = new(emissions.Count);
        foreach (EmissionSample emission in emissions)
        {
            double age = emission.AgeSeconds;
            ParticleRandom random = new(LegacyCinematicParticleCurveEvaluator.ComputeParticleSeed(actorKey, emission.ParticleOrdinal));
            ParticleSpawnState spawn = LegacyCinematicParticleEmission.CreateParticleSpawnState(
                template,
                actorState.Angle,
                isFxEmitter,
                emission.EventTimeSeconds,
                emission.UpdateStartTimeSeconds,
                random);
            double particleLifeTime = spawn.ParticleLifeTimeSeconds;
            if (!allowPhaseLoop && age >= particleLifeTime - LegacyCinematicParticleSimulator.MinimumPhaseTime)
                continue;

            if (!LegacyCinematicParticleEmission.TryResolvePhase(template, age, allowPhaseLoop, out int phaseIndex, out double phaseAge, out LegacyCinematicParticlePhase phase))
                continue;

            Vector2 acceleration = new(
                parameters.Acceleration.X + parameters.Gravity.X,
                parameters.Acceleration.Y + parameters.Gravity.Y);
            LegacyCinematicParticleMotion.ApplyEmitAccelerationCurves(template, elapsedSeconds, ref acceleration);
            double lifeFactor = particleLifeTime > LegacyCinematicParticleSimulator.MinimumPhaseTime
                ? Math.Clamp(age / particleLifeTime, 0.0, 1.0)
                : 0.0;
            double motionAge = age + emission.FirstUpdateDeltaSeconds;
            Vector2 position = LegacyCinematicParticleMotion.IntegrateParticlePosition(
                template,
                spawn.InitialPosition,
                spawn.InitialVelocity,
                acceleration,
                motionAge,
                particleLifeTime,
                actorKey,
                emission.ParticleOrdinal);
            if (template.Curves.Position.IsSet)
            {
                Vector3 curvePosition = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.Position, lifeFactor);
                position = new Vector2(curvePosition.X, curvePosition.Y);
            }

            ParticleVisual visual = LegacyCinematicParticleVisuals.ComputeVisual(
                template,
                phaseIndex,
                phase,
                phaseAge,
                lifeFactor,
                emission.EventTimeSeconds + particleLifeTime,
                elapsedSeconds,
                actorKey,
                emission.ParticleOrdinal,
                random);
            int atlasIndex = LegacyCinematicParticleVisuals.ComputeAtlasIndex(
                template,
                phase,
                age,
                phaseAge,
                particleLifeTime,
                emission.EventTimeSeconds,
                random,
                actorKey,
                emission.ParticleOrdinal);
            float angle = LegacyCinematicParticleMotion.ComputeParticleAngle(
                template,
                lifeFactor,
                motionAge,
                emission.EventTimeSeconds + particleLifeTime,
                emission.ParticleOrdinal,
                spawn);

            particles.Add(new SimulatedParticle(
                position,
                0.0f,
                visual.Size,
                angle,
                visual.Tint,
                visual.Alpha,
                atlasIndex));
        }

        return particles;
    }

    internal static double ComputeMaxLifeTime(LegacyCinematicParticleTemplate template)
    {
        if (template.Curves.ParticleLifeTime.IsSet)
        {
            Vector3 lifeRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.ParticleLifeTime, 0.0);
            return Math.Max(LegacyCinematicParticleSimulator.MinimumPhaseTime, Math.Max(lifeRange.X, lifeRange.Y));
        }

        double lifeTime = template.Phases
            .Where(phase => phase.PhaseTime > 0)
            .Sum(phase => (double)phase.PhaseTime + Math.Abs(phase.DeltaPhaseTime));
        if (lifeTime > LegacyCinematicParticleSimulator.MinimumPhaseTime)
            return lifeTime;

        return Math.Max(1.0, template.Parameters.Frequency);
    }

    internal static double ComputeParticleLifeTime(
        LegacyCinematicParticleTemplate template,
        double eventTimeSeconds,
        ParticleRandom random)
    {
        if (!template.Curves.ParticleLifeTime.IsSet)
            return LegacyCinematicParticleEmission.ComputeMaxLifeTime(template);

        Vector3 lifeRange = LegacyCinematicParticleCurveEvaluator.EvaluateParticleCurve(template.Curves.ParticleLifeTime, eventTimeSeconds);
        return Math.Max(
            LegacyCinematicParticleSimulator.MinimumPhaseTime,
            random.GetFloat(
                Math.Min(lifeRange.X, lifeRange.Y),
                Math.Max(lifeRange.X, lifeRange.Y)));
    }

    internal static IReadOnlyList<EmissionSample> ComputeActiveEmissionSamples(
        string actorKey,
        LegacyCinematicParticleTemplate template,
        double lifeTime,
        double elapsedSeconds,
        double? generationEndSeconds,
        bool particlesLoop)
    {
        LegacyCinematicParticleParameters parameters = template.Parameters;
        if (elapsedSeconds < 0)
            return [];

        if (parameters.GeneratorEmitMode == LegacyCinematicParticleSimulator.EmitModeAllAtOnce)
        {
            int allAtOnceCount = (int)Math.Max(1u, parameters.EmitBatchCountAllAtOnce);
            if (parameters.EmitBatchCountAllAtOnceMax != uint.MaxValue)
            {
                ParticleRandom allAtOnceRandom = new(LegacyCinematicParticleCurveEvaluator.ComputeParticleSeed(actorKey, -1));
                int maxAllAtOnceCount = (int)Math.Max(allAtOnceCount, parameters.EmitBatchCountAllAtOnceMax);
                allAtOnceCount = allAtOnceRandom.GetInt(allAtOnceCount, maxAllAtOnceCount);
            }

            if (parameters.EmitParticlesCount != uint.MaxValue)
                allAtOnceCount = (int)Math.Min((uint)allAtOnceCount, parameters.EmitParticlesCount);

            allAtOnceCount = (int)Math.Clamp(
                Math.Min(parameters.MaxParticles, (uint)allAtOnceCount),
                0,
                LegacyCinematicParticleSimulator.MaxGeneratedDrawItems);
            if (!particlesLoop && elapsedSeconds > lifeTime + LegacyCinematicParticleSimulator.MinimumPhaseTime)
                return [];

            return [.. Enumerable.Range(0, allAtOnceCount)
                .Select(index => LegacyCinematicParticleEmission.CreateEmissionSample(
                    actorKey,
                    template,
                    elapsedSeconds,
                    0.0,
                    0.0,
                    0.0,
                    index))];
        }

        double generationStopSeconds = generationEndSeconds.HasValue
            ? Math.Min(elapsedSeconds, generationEndSeconds.Value)
            : elapsedSeconds;
        double generationEnd = parameters.EmitterMaxLifeTime > LegacyCinematicParticleSimulator.MinimumPhaseTime
            ? Math.Min(generationStopSeconds, parameters.EmitterMaxLifeTime)
            : generationStopSeconds;
        if (generationEnd <= 0)
            return [];

        if (LegacyCinematicParticleEmission.TryComputeConstantIntervalEmissionSamples(
            actorKey,
            template,
            lifeTime,
            elapsedSeconds,
            generationEnd,
            particlesLoop,
            out IReadOnlyList<EmissionSample> constantIntervalSamples))
        {
            return constantIntervalSamples;
        }

        int batchCount = (int)Math.Max(1u, parameters.EmitBatchCount);
        int maxTotalEmitted = parameters.EmitParticlesCount == uint.MaxValue
            ? int.MaxValue
            : (int)Math.Min(parameters.EmitParticlesCount, int.MaxValue);
        int maxActive = (int)Math.Min(parameters.MaxParticles, LegacyCinematicParticleSimulator.MaxGeneratedDrawItems);
        List<EmissionSample> samples = new(Math.Min(maxActive, LegacyCinematicParticleSimulator.MaxGeneratedDrawItems));

        // Step the carried emission accumulator so frequency curves are evaluated
        // at generator update times instead of only at the final rendered frame.
        double lastUpdateTime = 0.0;
        double particlesToEmitExact = 0.0;
        int totalGenerated = 0;
        ParticleRandom emissionRandom = new(LegacyCinematicParticleCurveEvaluator.ComputeParticleSeed(actorKey, -1));
        for (double currentTime = 0.0;
             currentTime < generationEnd - LegacyCinematicParticleSimulator.MinimumPhaseTime &&
             totalGenerated < maxTotalEmitted &&
             (!particlesLoop || samples.Count < maxActive);
             )
        {
            double nextTime = Math.Min(generationEnd, currentTime + LegacyCinematicParticleSimulator.ParticleIntegrationStepSeconds);

            double interval = LegacyCinematicParticleCurveEvaluator.ComputeEmissionInterval(template, nextTime, emissionRandom);
            if (double.IsInfinity(interval))
            {
                lastUpdateTime = nextTime;
                currentTime = nextTime;
                LegacyCinematicParticleEmission.PruneExpiredEmissionSamples(samples, nextTime, particlesLoop);
                continue;
            }

            double newParticles = (nextTime - lastUpdateTime) / interval;
            if (lastUpdateTime <= LegacyCinematicParticleSimulator.MinimumPhaseTime &&
                parameters.ForceEmitAtStart &&
                newParticles < 1.0)
            {
                particlesToEmitExact = 0.0;
                newParticles = 1.0;
            }

            particlesToEmitExact += newParticles;
            int particleEvents = (int)particlesToEmitExact;
            int particlesToEmit = batchCount * particleEvents;
            if (particlesToEmit > 0 &&
                samples.Count < maxActive)
            {
                particlesToEmit = Math.Min(particlesToEmit, maxActive - samples.Count);
                particlesToEmit = Math.Min(particlesToEmit, maxTotalEmitted - totalGenerated);
                LegacyCinematicParticleEmission.AddEmissionEvent(
                    actorKey,
                    template,
                    samples,
                    elapsedSeconds,
                    lifeTime,
                    nextTime,
                    currentTime,
                    totalGenerated,
                    particlesToEmit,
                    maxTotalEmitted,
                    maxActive,
                    particlesLoop);
                totalGenerated += particlesToEmit;
            }

            particlesToEmitExact -= particleEvents;
            if (particlesToEmitExact < 0.0)
                particlesToEmitExact = 0.0;

            lastUpdateTime = nextTime;
            currentTime = nextTime;
            // Prune after the emit step so max-particle pressure matches the
            // update order, but use each sample's own randomized die time.
            LegacyCinematicParticleEmission.PruneExpiredEmissionSamples(samples, nextTime, particlesLoop);
        }

        LegacyCinematicParticleEmission.PruneExpiredEmissionSamples(samples, elapsedSeconds, particlesLoop);
        return samples;
    }

    internal static bool TryComputeConstantIntervalEmissionSamples(
        string actorKey,
        LegacyCinematicParticleTemplate template,
        double lifeTime,
        double elapsedSeconds,
        double generationEnd,
        bool particlesLoop,
        out IReadOnlyList<EmissionSample> samples)
    {
        samples = [];
        LegacyCinematicParticleParameters parameters = template.Parameters;
        if (template.Curves.Frequency.IsSet ||
            Math.Abs(parameters.FrequencyDelta) > 0.000001f ||
            parameters.Frequency <= LegacyCinematicParticleSimulator.MinimumEmissionInterval)
        {
            return false;
        }

        int batchCount = (int)Math.Max(1u, parameters.EmitBatchCount);
        int maxTotalEmitted = parameters.EmitParticlesCount == uint.MaxValue
            ? int.MaxValue
            : (int)Math.Min(parameters.EmitParticlesCount, int.MaxValue);
        int maxActive = (int)Math.Min(parameters.MaxParticles, LegacyCinematicParticleSimulator.MaxGeneratedDrawItems);
        if (maxActive <= 0 || batchCount <= 0 || maxTotalEmitted <= 0)
        {
            samples = [];
            return true;
        }

        double interval = Math.Max(LegacyCinematicParticleSimulator.MinimumEmissionInterval, parameters.Frequency);
        double step = LegacyCinematicParticleSimulator.ParticleIntegrationStepSeconds;
        bool forceFirstStep = parameters.ForceEmitAtStart && step / interval < 1.0;
        double firstSearchTime = particlesLoop
            ? 0.0
            : Math.Max(0.0, elapsedSeconds - lifeTime - (step * 2.0));
        long firstEventIndex = LegacyCinematicParticleEmission.EstimateFirstConstantIntervalEventIndex(
            firstSearchTime,
            interval,
            step,
            forceFirstStep);
        long maxEventCount = (maxTotalEmitted + (long)batchCount - 1L) / batchCount;
        List<EmissionSample> activeSamples = new(Math.Min(maxActive, LegacyCinematicParticleSimulator.MaxGeneratedDrawItems));

        for (long eventIndex = firstEventIndex;
             eventIndex < maxEventCount && activeSamples.Count < maxActive;
             eventIndex++)
        {
            double eventTime = LegacyCinematicParticleEmission.ComputeConstantIntervalEventTime(
                eventIndex,
                interval,
                step,
                forceFirstStep);
            if (eventTime > generationEnd + LegacyCinematicParticleSimulator.MinimumPhaseTime)
                break;

            int firstOrdinal = checked((int)Math.Min((long)int.MaxValue, eventIndex * batchCount));
            int eventBatchCount = Math.Min(batchCount, maxTotalEmitted - firstOrdinal);
            if (eventBatchCount <= 0)
                break;

            double updateStart = Math.Max(0.0, eventTime - step);
            LegacyCinematicParticleEmission.AddEmissionEvent(
                actorKey,
                template,
                activeSamples,
                elapsedSeconds,
                lifeTime,
                eventTime,
                updateStart,
                firstOrdinal,
                eventBatchCount,
                maxTotalEmitted,
                maxActive,
                particlesLoop);
        }

        samples = activeSamples;
        return true;
    }

    internal static long EstimateFirstConstantIntervalEventIndex(
        double firstSearchTime,
        double interval,
        double step,
        bool forceFirstStep)
    {
        if (firstSearchTime <= step)
            return 0;

        double adjustedTime = forceFirstStep
            ? firstSearchTime - step
            : firstSearchTime;
        long estimate = (long)Math.Floor(adjustedTime / interval) - 2L;
        if (forceFirstStep)
            estimate += 1L;

        return Math.Max(0L, estimate);
    }

    internal static double ComputeConstantIntervalEventTime(
        long eventIndex,
        double interval,
        double step,
        bool forceFirstStep)
    {
        if (forceFirstStep)
        {
            if (eventIndex <= 0)
                return step;

            return step + (Math.Ceiling(eventIndex * interval / step) * step);
        }

        return Math.Ceiling((eventIndex + 1L) * interval / step) * step;
    }

    internal static void PruneExpiredEmissionSamples(
        List<EmissionSample> samples,
        double currentTime,
        bool particlesLoop)
    {
        if (particlesLoop)
            return;

        for (int index = samples.Count - 1; index >= 0; index--)
        {
            if (currentTime - samples[index].EventTimeSeconds >= samples[index].ParticleLifeTimeSeconds - LegacyCinematicParticleSimulator.MinimumPhaseTime)
                samples.RemoveAt(index);
        }
    }

    internal static void AddEmissionEvent(
        string actorKey,
        LegacyCinematicParticleTemplate template,
        List<EmissionSample> samples,
        double elapsedSeconds,
        double lifeTime,
        double eventTimeSeconds,
        double updateStartTimeSeconds,
        int firstOrdinal,
        int batchCount,
        int maxTotalEmitted,
        int maxActive,
        bool particlesLoop)
    {
        double age = elapsedSeconds - eventTimeSeconds;
        if (age < -LegacyCinematicParticleSimulator.MinimumPhaseTime ||
            (!particlesLoop && age >= lifeTime - LegacyCinematicParticleSimulator.MinimumPhaseTime))
        {
            return;
        }

        for (int batchIndex = 0; batchIndex < batchCount && samples.Count < maxActive; batchIndex++)
        {
            int ordinal = firstOrdinal + batchIndex;
            if (ordinal < 0 || ordinal >= maxTotalEmitted)
                continue;

            EmissionSample sample = LegacyCinematicParticleEmission.CreateEmissionSample(
                actorKey,
                template,
                elapsedSeconds,
                eventTimeSeconds,
                updateStartTimeSeconds,
                Math.Max(0.0, eventTimeSeconds - updateStartTimeSeconds),
                ordinal);
            if (!particlesLoop && sample.AgeSeconds >= sample.ParticleLifeTimeSeconds - LegacyCinematicParticleSimulator.MinimumPhaseTime)
                continue;

            samples.Add(sample);
        }
    }

    internal static EmissionSample CreateEmissionSample(
        string actorKey,
        LegacyCinematicParticleTemplate template,
        double elapsedSeconds,
        double eventTimeSeconds,
        double updateStartTimeSeconds,
        double firstUpdateDeltaSeconds,
        int particleOrdinal)
    {
        ParticleRandom random = new(LegacyCinematicParticleCurveEvaluator.ComputeParticleSeed(actorKey, particleOrdinal));
        ParticleSpawnState spawn = LegacyCinematicParticleEmission.CreateParticleSpawnState(
            template,
            actorAngle: 0.0f,
            isFxEmitter: false,
            eventTimeSeconds,
            updateStartTimeSeconds,
            random);
        return new EmissionSample(
            Math.Max(0.0, elapsedSeconds - eventTimeSeconds),
            eventTimeSeconds,
            updateStartTimeSeconds,
            firstUpdateDeltaSeconds,
            particleOrdinal,
            spawn.ParticleLifeTimeSeconds);
    }

    internal static bool TryResolvePhase(
        LegacyCinematicParticleTemplate template,
        double age,
        bool allowPhaseLoop,
        out int phaseIndex,
        out double phaseAge,
        out LegacyCinematicParticlePhase phase)
    {
        phaseIndex = 0;
        phaseAge = age;
        phase = default!;

        if (template.Phases.Count == 0)
            return false;

        double phaseCycleTime = template.Phases
            .Where(candidate => candidate.PhaseTime > LegacyCinematicParticleSimulator.MinimumPhaseTime)
            .Sum(candidate => (double)candidate.PhaseTime);
        bool loopPhases = (allowPhaseLoop || template.Curves.ParticleLifeTime.IsSet) &&
            phaseCycleTime > LegacyCinematicParticleSimulator.MinimumPhaseTime;
        double remaining = loopPhases
            ? LegacyCinematicParticleCurveEvaluator.PositiveModulo(age, phaseCycleTime)
            : age;
        for (int index = 0; index < template.Phases.Count; index++)
        {
            LegacyCinematicParticlePhase candidate = template.Phases[index];
            double duration = Math.Max(LegacyCinematicParticleSimulator.MinimumPhaseTime, candidate.PhaseTime);
            if (remaining <= duration || index == template.Phases.Count - 1)
            {
                phaseIndex = index;
                phaseAge = Math.Clamp(remaining, 0, duration);
                phase = candidate;
                return true;
            }

            remaining -= duration;
        }

        return false;
    }

    internal static ParticleSpawnState CreateParticleSpawnState(
        LegacyCinematicParticleTemplate template,
        float actorAngle,
        bool isFxEmitter,
        double eventTimeSeconds,
        double updateStartTimeSeconds,
        ParticleRandom random)
    {
        // Keep the spawn RNG stream stable: velocity amount, generator position,
        // initial angle/angular speed, then particle lifetime. Rectangle FX are
        // especially sensitive because most visible variation is X/Y placement.
        float speed = LegacyCinematicParticleMotion.ComputeInitialSpeed(template, eventTimeSeconds, random);
        Vector2 initialPosition = LegacyCinematicParticleMotion.ComputeInitialPosition(template.Parameters, random);
        Vector2 initialVelocity = LegacyCinematicParticleMotion.ComputeInitialVelocity(
            template,
            initialPosition,
            actorAngle,
            isFxEmitter,
            eventTimeSeconds,
            random,
            speed);
        float initialAngle = LegacyCinematicParticleMotion.ComputeInitialParticleAngle(template, updateStartTimeSeconds, random);
        float angularSpeed = LegacyCinematicParticleMotion.ComputeInitialParticleAngularSpeed(template, updateStartTimeSeconds, random);
        double particleLifeTime = LegacyCinematicParticleEmission.ComputeParticleLifeTime(template, eventTimeSeconds, random);
        return new ParticleSpawnState(
            initialPosition,
            initialVelocity,
            initialAngle,
            angularSpeed,
            particleLifeTime);
    }
}