using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using System.Globalization;
using System.Numerics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;

internal static class LegacyCinematicParticleDrawItemBuilder
{
    internal static double ComputeDescriptorDelay(LegacyCinematicFxEmitterTemplate emitter)
    {
        float min = Math.Min(emitter.MinDelaySeconds, emitter.MaxDelaySeconds);
        float max = Math.Max(emitter.MinDelaySeconds, emitter.MaxDelaySeconds);
        if (max <= LegacyCinematicParticleSimulator.MinimumPhaseTime)
            return 0.0;

        if (Math.Abs(max - min) <= 0.000001f)
            return min;

        float factor = LegacyCinematicParticleCurveEvaluator.StableUnitRandom(
            "fx_descriptor_delay",
            emitter.Index,
            emitter.DescriptorNameId.ToString(CultureInfo.InvariantCulture));
        return LegacyCinematicParticleCurveEvaluator.Lerp(min, max, factor);
    }

    internal static void AddParticleDrawItems(
        RenderableCinematicActor actor,
        ResolvedActorState actorState,
        CinematicMaterialRuntimeOverrides? materialOverrides,
        LegacyCinematicParticleTemplate template,
        MaterializedCinematicImage image,
        int emitterIndex,
        double elapsedSeconds,
        double? generationEndSeconds,
        FxPlaybackOptions playbackOptions,
        int outputWidth,
        int outputHeight,
        List<FrameDrawItem> frameItems)
    {
        string simulationKey = emitterIndex >= 0
            ? $"{actor.Actor.Key}:fx{emitterIndex}"
            : actor.Actor.Key;
        bool isFxEmitter = emitterIndex >= 0;
        ResolvedActorState emitterActorState = isFxEmitter
            ? actorState with { Angle = actorState.Angle + playbackOptions.DescriptorAngleOffsetRadians }
            : actorState;
        IReadOnlyList<SimulatedParticle> particles = LegacyCinematicParticleEmission.Simulate(simulationKey, template, emitterActorState, elapsedSeconds, generationEndSeconds, isFxEmitter, playbackOptions);
        int added = 0;
        for (int index = 0; index < particles.Count; index++)
        {
            SimulatedParticle particle = particles[index];
            if (particle.Alpha <= 0.001f ||
                Math.Abs(particle.Size.X) <= 0.0001f ||
                Math.Abs(particle.Size.Y) <= 0.0001f)
            {
                continue;
            }

            int atlasIndex = particle.AtlasIndex;
            if (atlasIndex < 0 && image.Atlas != null)
            {
                // Particle atlas images should draw a frame rather than the full sheet.
                atlasIndex = 0;
            }

            LegacyCinematicUvRect? uvOverride = null;
            if (atlasIndex >= 0 &&
                image.Atlas != null &&
                LegacyCinematicAtlasContainer.TryGetUvRect(image.Atlas, atlasIndex, out LegacyCinematicUvRect atlasUv))
            {
                uvOverride = atlasUv;
            }

            Vector2 atlasSizeScale = LegacyCinematicParticleCurveEvaluator.ComputeAtlasSizeScale(template.Parameters, uvOverride);
            ResolvedActorState particleState = LegacyCinematicParticleCurveEvaluator.CreateParticleState(emitterActorState, template.Parameters, particle, atlasSizeScale, isFxEmitter, playbackOptions);
            ProjectedQuad quad = LegacyCinematicRenderEngine.ProjectQuad(
                RenderGeometry.NoAtlasQuad,
                particleState,
                outputWidth,
                outputHeight);
            if (quad.Bounds.Width <= 0 || quad.Bounds.Height <= 0)
            {
                continue;
            }

            frameItems.Add(new FrameDrawItem(
                actor,
                particleState,
                quad,
                materialOverrides,
                uvOverride,
                index,
                atlasIndex,
                emitterIndex,
                image,
                RenderGeometry.NoAtlasQuad,
                template,
                elapsedSeconds,
                SortDepthOverride: LegacyCinematicParticleCurveEvaluator.ComputeParticleSortDepth(emitterActorState, template.Parameters)));
            added++;
        }
    }
}