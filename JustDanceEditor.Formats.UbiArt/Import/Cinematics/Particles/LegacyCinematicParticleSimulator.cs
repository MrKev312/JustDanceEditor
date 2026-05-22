using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleSimulator
{
    internal const double MinimumPhaseTime = 0.0001;
    internal const double MinimumEmissionInterval = 0.000001;
    internal const double ParticleIntegrationStepSeconds = 1.0 / 60.0;
    internal const int MaxGeneratedDrawItems = 4096;
    internal const int SplineInterpolationLinear = 0;
    internal const int SplineInterpolationSpline = 1;
    internal const int SplineInterpolationBezier = 2;
    internal const int SplineInterpolationConstant = 3;
    internal const int SplineInterpolationBezierStandard = 4;
    internal const int ParticleStructStrideBytes = 132;
    internal const uint GenerationPoints = 0;
    internal const uint GenerationRectangle = 1;
    internal const uint GenerationCircle = 2;
    internal const uint GenerationSphere = 4;
    internal const uint GenerationHemisphere = 5;
    internal const uint GeneratorModeManual = 2;
    internal const uint EmitModeAllAtOnce = 1;
    private static readonly object ParticleLogLock = new();

    public static bool TryAddParticleDrawItems(
        RenderableCinematicActor actor,
        ResolvedActorState actorState,
        CinematicMaterialRuntimeOverrides? materialOverrides,
        IReadOnlyList<ActiveFxPlayback> activeFx,
        double elapsedSeconds,
        double defaultFxElapsedSeconds,
        int outputWidth,
        int outputHeight,
        List<FrameDrawItem> frameItems)
    {
        if (actor.Image == null)
            return false;

        if (actor.Actor.FxTemplate is { } fxTemplate)
        {
            IReadOnlyList<ActiveFxEmitterPlayback> activeEmitterPlaybacks = LegacyCinematicParticleSimulator.ResolveActiveFxEmitterPlaybacks(
                fxTemplate,
                activeFx,
                defaultFxElapsedSeconds);
            if (activeEmitterPlaybacks.Count > 0)
            {
                foreach (ActiveFxEmitterPlayback emitterPlayback in activeEmitterPlaybacks)
                {
                    MaterializedCinematicFxEmitter? emitter = actor.Image.FxEmitters.FirstOrDefault(item => item.Template.Index == emitterPlayback.EmitterIndex);
                    if (emitter == null)
                        continue;

                    LegacyCinematicParticleDrawItemBuilder.AddParticleDrawItems(
                        actor,
                        actorState,
                        materialOverrides,
                        emitter.Template.ParticleTemplate,
                        emitter.Image,
                        emitter.Template.Index,
                        emitterPlayback.ElapsedSeconds,
                        emitterPlayback.GenerationEndSeconds,
                        emitterPlayback.Options,
                        outputWidth,
                        outputHeight,
                        frameItems);
                }

                return true;
            }

            if (actor.Actor.ParticleTemplate == null)
                return true;
        }

        if (actor.Actor.ParticleTemplate is not { } template)
            return false;

        LegacyCinematicParticleDrawItemBuilder.AddParticleDrawItems(
            actor,
            actorState,
            materialOverrides,
            template,
            actor.Image,
            -1,
            elapsedSeconds,
            null,
            FxPlaybackOptions.PlainParticleGenerator,
            outputWidth,
            outputHeight,
            frameItems);
        return true;
    }

    private static IReadOnlyList<ActiveFxEmitterPlayback> ResolveActiveFxEmitterPlaybacks(
        LegacyCinematicFxTemplate fxTemplate,
        IReadOnlyList<ActiveFxPlayback> activeFx,
        double defaultElapsedSeconds)
    {
        List<ActiveFxEmitterPlayback> playbacks = [];
        if (LegacyCinematicParticleSimulator.ShouldPlayDefaultFx(fxTemplate) && LegacyCinematicFxIds.IsValid(fxTemplate.DefaultFxNameId))
        {
            // FXControllerComponent::onBecomeActive() starts defaultFx once and
            // keeps the FxBank instance alive until the actor becomes inactive.
            // The offline renderer passes an actor-activation-relative clock.
            LegacyCinematicParticleSimulator.AddEmitterPlaybacksForFxName(
                fxTemplate,
                fxTemplate.DefaultFxNameId,
                LegacyCinematicParticleSimulator.GetDefaultFxElapsedSeconds(defaultElapsedSeconds),
                null,
                playbacks);
        }

        foreach (ActiveFxPlayback fx in activeFx)
            LegacyCinematicParticleSimulator.AddEmitterPlaybacksForFxName(fxTemplate, fx.NameId, fx.ElapsedSeconds, fx.GenerationEndSeconds, playbacks);

        return [.. playbacks.OrderBy(item => item.EmitterIndex)];
    }

    private static bool ShouldPlayDefaultFx(LegacyCinematicFxTemplate fxTemplate) => true;

    internal static bool HasPersistentBillboardDefaultFx(LegacyCinematicActor actor) =>
        actor.FxTemplate is { } fxTemplate && LegacyCinematicParticleSimulator.IsPersistentBillboardDefaultFx(fxTemplate);

    private static bool IsPersistentBillboardDefaultFx(LegacyCinematicFxTemplate fxTemplate)
    {
        if (!LegacyCinematicFxIds.IsValid(fxTemplate.DefaultFxNameId) ||
            !fxTemplate.ControlsByNameId.TryGetValue(fxTemplate.DefaultFxNameId, out LegacyCinematicFxControlTemplate? control))
        {
            return false;
        }

        bool foundEmitter = false;
        foreach (uint particleNameId in control.ParticleNameIds)
        {
            foreach (LegacyCinematicFxEmitterTemplate emitter in fxTemplate.Emitters)
            {
                if (emitter.DescriptorNameId != particleNameId)
                    continue;

                foundEmitter = true;
                if (emitter.ParticleTemplate.Parameters.MaxParticles > 1)
                    return false;
            }
        }

        return foundEmitter;
    }

    private static double GetDefaultFxElapsedSeconds(double elapsedSeconds)
    {
        if (elapsedSeconds < 0.0)
            return elapsedSeconds;

        return elapsedSeconds + LegacyCinematicParticleSimulator.GetDefaultFxPrerollSeconds();
    }

    private static double GetDefaultFxPrerollSeconds() => 0.5;

    private static void AddEmitterPlaybacksForFxName(
        LegacyCinematicFxTemplate fxTemplate,
        uint fxNameId,
        double elapsedSeconds,
        double? generationEndSeconds,
        List<ActiveFxEmitterPlayback> playbacks)
    {
        if (!LegacyCinematicFxIds.IsValid(fxNameId))
            return;

        if (fxTemplate.ControlsByNameId.TryGetValue(fxNameId, out LegacyCinematicFxControlTemplate? control))
        {
            foreach (uint particleNameId in control.ParticleNameIds)
                LegacyCinematicParticleSimulator.AddEmitterPlaybacksForDescriptor(
                    fxTemplate,
                    particleNameId,
                    elapsedSeconds,
                    generationEndSeconds,
                    FxPlaybackOptions.FromControl(control),
                    playbacks);
            return;
        }

        // FXClipPlayer calls FXControllerComponent::playFX(), which resolves only
        // FXControl events. Raw FxDescriptor names are not played directly by tape clips.
    }

    private static void AddEmitterPlaybacksForDescriptor(
        LegacyCinematicFxTemplate fxTemplate,
        uint descriptorNameId,
        double elapsedSeconds,
        double? generationEndSeconds,
        FxPlaybackOptions options,
        List<ActiveFxEmitterPlayback> playbacks)
    {
        foreach (LegacyCinematicFxEmitterTemplate emitter in fxTemplate.Emitters)
        {
            if (emitter.DescriptorNameId != descriptorNameId)
                continue;

            double descriptorDelaySeconds = LegacyCinematicParticleDrawItemBuilder.ComputeDescriptorDelay(emitter);
            double emitterElapsedSeconds = elapsedSeconds - descriptorDelaySeconds;
            if (emitterElapsedSeconds < 0.0)
                continue;

            double? emitterGenerationEndSeconds = generationEndSeconds.HasValue
                ? Math.Max(0.0, generationEndSeconds.Value - descriptorDelaySeconds)
                : null;
            if (emitterGenerationEndSeconds is <= 0.0)
                continue;

            FxPlaybackOptions emitterOptions = options with
            {
                DescriptorAngleOffsetRadians = emitter.AngleOffsetRadians,
                DescriptorMinDelaySeconds = emitter.MinDelaySeconds,
                DescriptorMaxDelaySeconds = emitter.MaxDelaySeconds,
                DescriptorDelaySeconds = descriptorDelaySeconds
            };
            playbacks.Add(new ActiveFxEmitterPlayback(emitter.Index, emitterElapsedSeconds, emitterGenerationEndSeconds, emitterOptions));
        }
    }
}