using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using SixLabors.ImageSharp;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicActorTiming
{
    internal static IReadOnlySet<string> ComputeStaticActorKeys(
        IReadOnlyList<RenderableCinematicActor> actors,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex)
    {
        Dictionary<string, bool> stateDynamicByKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (RenderableCinematicActor actor in actors)
        {
            if (actor.RenderKind != CinematicRenderKind.PleoVideo &&
                actor.Actor.FxTemplate == null &&
                actor.Actor.ParticleTemplate == null &&
                !LegacyCinematicActorTiming.HasAnimatedMaterial(actor) &&
                !LegacyCinematicActorTiming.HasAnimatedSinus(actor) &&
                !LegacyCinematicActorTiming.HasAnimatedParticleAtlas(actor) &&
                !LegacyCinematicActorTiming.IsActorStateDynamic(
                    actor.Actor,
                    runtime,
                    clipIndex,
                    stateDynamicByKey,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            {
                stateDynamicByKey[actor.Actor.Key] = false;
            }
            else
            {
                stateDynamicByKey[actor.Actor.Key] = true;
            }
        }

        return new HashSet<string>(
            actors
                .Where(actor => stateDynamicByKey.TryGetValue(actor.Actor.Key, out bool dynamic) && !dynamic)
                .Select(actor => actor.Actor.Key),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static bool IsActorStateDynamic(
        LegacyCinematicActor actor,
        LegacyCinematicRenderRuntime runtime,
        PropertyClipIndex clipIndex,
        Dictionary<string, bool> cache,
        HashSet<string> resolving)
    {
        if (cache.TryGetValue(actor.Key, out bool cached))
            return cached;

        if (!resolving.Add(actor.Key))
            return false;

        bool dynamic = clipIndex.HasClips(actor) ||
            (runtime.TryResolveParentBindActor(actor, out LegacyCinematicActor? bindParent) &&
                LegacyCinematicActorTiming.IsActorStateDynamic(bindParent, runtime, clipIndex, cache, resolving)) ||
            (runtime.TryGetSubSceneParent(actor, out LegacyCinematicActor? subSceneParent) &&
                LegacyCinematicActorTiming.IsActorStateDynamic(subSceneParent, runtime, clipIndex, cache, resolving));

        resolving.Remove(actor.Key);
        cache[actor.Key] = dynamic;
        return dynamic;
    }

    internal static bool HasAnimatedMaterial(RenderableCinematicActor actor) =>
        actor.Image?.Material.Layers.Any(layer =>
            layer.UvModifiers.Any(modifier =>
                modifier.AnimTranslationU ||
                modifier.AnimTranslationV ||
                modifier.AnimRotation)) == true;

    internal static bool HasAnimatedSinus(RenderableCinematicActor actor) =>
        actor.Actor.Sinus.HasAnimatedOffset &&
        actor.Geometry.HasSinusVertices;

    internal static bool HasAnimatedParticleAtlas(RenderableCinematicActor actor) =>
        LegacyCinematicActorTiming.HasAnimatedParticleAtlas(actor.Actor.ParticleTemplate) ||
        actor.Actor.FxTemplate?.Emitters.Any(emitter => LegacyCinematicActorTiming.HasAnimatedParticleAtlas(emitter.ParticleTemplate)) == true;

    internal static bool HasAnimatedParticleAtlas(LegacyCinematicParticleTemplate? template) =>
        template is { } particleTemplate &&
        (particleTemplate.Animation.HasAtlasAnimation ||
            particleTemplate.Curves.AtlasAnimation.IsSet ||
            particleTemplate.Curves.EmitAtlasAnimation.IsSet ||
            particleTemplate.Phases.Any(phase => phase.AnimStart >= 0 && phase.AnimEnd >= 0));

    internal static MaterializedCinematicImage? GetItemImage(FrameDrawItem item) =>
        item.ImageOverride ?? item.Actor.Image;

    internal static RenderGeometry GetItemGeometry(FrameDrawItem item) =>
        item.GeometryOverride ?? item.Actor.Geometry;

    internal static LegacyCinematicParticleTemplate? GetItemParticleTemplate(FrameDrawItem item) =>
        item.ParticleTemplateOverride ?? item.Actor.Actor.ParticleTemplate;

    internal static double GetItemMaterialElapsedSeconds(FrameDrawItem item, double fallback) =>
        double.IsNaN(item.MaterialElapsedSeconds) ? fallback : item.MaterialElapsedSeconds;

    internal static double GetMaterialElapsedSecondsForActor(LegacyCinematicActor actor, double baseElapsedSeconds)
    {
        double offsetSeconds = 0.0;
        foreach (MaterialActorTimeOffset offset in LegacyCinematicActorTiming.GetMaterialActorTimeOffsets())
        {
            if (LegacyCinematicActorTiming.MatchesMaterialActorOffsetSelector(actor, offset.Selector))
                offsetSeconds += offset.OffsetSeconds;
        }

        return baseElapsedSeconds + offsetSeconds;
    }

    internal static IReadOnlyList<MaterialActorTimeOffset> GetMaterialActorTimeOffsets() => [];

    internal static bool MatchesMaterialActorOffsetSelector(LegacyCinematicActor actor, string selector) =>
        string.Equals(actor.Key, selector, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(actor.Name, selector, StringComparison.OrdinalIgnoreCase) ||
        actor.Key.EndsWith($"/{selector}", StringComparison.OrdinalIgnoreCase) ||
        actor.Key.Contains(selector, StringComparison.OrdinalIgnoreCase);

    internal static CinematicMaterialRuntimeOverrides? GetMaterialRuntimeOverrides(
        LegacyCinematicActor actor,
        PropertyClipIndex clipIndex,
        double frame)
    {
        Dictionary<int, bool>? layerEnabled = null;
        Dictionary<int, CinematicMaterialColorOverride>? layerColors = null;
        Dictionary<CinematicMaterialUvModifierKey, CinematicMaterialUvModifierOverride>? uvModifiers = null;

        foreach (PropertyClip clip in clipIndex.GetMaterialGraphicClips(actor, frame))
        {
            CinematicMaterialGraphicClip? materialGraphic = clip.State.MaterialGraphic;
            if (materialGraphic == null)
            {
                if (clip.State.LayerEnable is { } layerEnable)
                {
                    layerEnabled ??= [];
                    layerEnabled[layerEnable.LayerIndex] = layerEnable.Enabled;
                }

                continue;
            }

            double localFrame = LegacyCinematicRenderEngine.GetClipLocalFrame(clip, frame);
            switch (materialGraphic.Kind)
            {
                case CinematicMaterialGraphicClipKind.EnableLayer:
                    layerEnabled ??= [];
                    layerEnabled[materialGraphic.LayerIndex] = materialGraphic.Enabled;
                    break;
                case CinematicMaterialGraphicClipKind.DiffuseAlpha:
                    if (materialGraphic.Alpha != null)
                    {
                        layerColors ??= [];
                        CinematicMaterialColorOverride color = LegacyCinematicActorTiming.GetLayerColorOverride(layerColors, materialGraphic.LayerIndex);
                        layerColors[materialGraphic.LayerIndex] = color with
                        {
                            Alpha = LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.Alpha, localFrame)
                        };
                    }

                    break;
                case CinematicMaterialGraphicClipKind.DiffuseColor:
                    if (materialGraphic.Red != null || materialGraphic.Green != null || materialGraphic.Blue != null)
                    {
                        layerColors ??= [];
                        CinematicMaterialColorOverride color = LegacyCinematicActorTiming.GetLayerColorOverride(layerColors, materialGraphic.LayerIndex);
                        layerColors[materialGraphic.LayerIndex] = color with
                        {
                            Red = materialGraphic.Red != null ? LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.Red, localFrame) : color.Red,
                            Green = materialGraphic.Green != null ? LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.Green, localFrame) : color.Green,
                            Blue = materialGraphic.Blue != null ? LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.Blue, localFrame) : color.Blue
                        };
                    }

                    break;
                case CinematicMaterialGraphicClipKind.UvTranslation:
                    uvModifiers ??= [];
                    CinematicMaterialUvModifierKey translationKey = new(materialGraphic.LayerIndex, materialGraphic.UvModifierIndex);
                    CinematicMaterialUvModifierOverride translation = LegacyCinematicActorTiming.GetUvModifierOverride(uvModifiers, translationKey);
                    uvModifiers[translationKey] = translation with
                    {
                        TranslationU = materialGraphic.U != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.U, localFrame) : translation.TranslationU,
                        TranslationV = materialGraphic.V != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.V, localFrame) : translation.TranslationV,
                        DisableAnimTranslation = true
                    };
                    break;
                case CinematicMaterialGraphicClipKind.UvRotation:
                    uvModifiers ??= [];
                    CinematicMaterialUvModifierKey rotationKey = new(materialGraphic.LayerIndex, materialGraphic.UvModifierIndex);
                    CinematicMaterialUvModifierOverride rotation = LegacyCinematicActorTiming.GetUvModifierOverride(uvModifiers, rotationKey);
                    uvModifiers[rotationKey] = rotation with
                    {
                        Rotation = materialGraphic.Angle != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.Angle, localFrame) : rotation.Rotation,
                        RotationOffsetU = materialGraphic.PivotX != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.PivotX, localFrame) : rotation.RotationOffsetU,
                        RotationOffsetV = materialGraphic.PivotY != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.PivotY, localFrame) : rotation.RotationOffsetV,
                        DisableAnimRotation = true
                    };
                    break;
                case CinematicMaterialGraphicClipKind.UvScale:
                    uvModifiers ??= [];
                    CinematicMaterialUvModifierKey scaleKey = new(materialGraphic.LayerIndex, materialGraphic.UvModifierIndex);
                    CinematicMaterialUvModifierOverride scale = LegacyCinematicActorTiming.GetUvModifierOverride(uvModifiers, scaleKey);
                    uvModifiers[scaleKey] = scale with
                    {
                        ScaleU = materialGraphic.ScaleU != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.ScaleU, localFrame) : scale.ScaleU,
                        ScaleV = materialGraphic.ScaleV != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.ScaleV, localFrame) : scale.ScaleV,
                        ScaleOffsetU = materialGraphic.PivotX != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.PivotX, localFrame) : scale.ScaleOffsetU,
                        ScaleOffsetV = materialGraphic.PivotY != null ? (float)LegacyCinematicRenderEngine.EvaluateCurve(materialGraphic.PivotY, localFrame) : scale.ScaleOffsetV
                    };
                    break;
            }
        }

        if ((layerEnabled == null || layerEnabled.Count == 0) &&
            (layerColors == null || layerColors.Count == 0) &&
            (uvModifiers == null || uvModifiers.Count == 0))
        {
            return null;
        }

        return new CinematicMaterialRuntimeOverrides(
            layerEnabled ?? [],
            layerColors ?? [],
            uvModifiers ?? []);
    }

    internal static CinematicMaterialColorOverride GetLayerColorOverride(
        IReadOnlyDictionary<int, CinematicMaterialColorOverride> overrides,
        int layerIndex) =>
        overrides.TryGetValue(layerIndex, out CinematicMaterialColorOverride color)
            ? color
            : default;

    internal static CinematicMaterialUvModifierOverride GetUvModifierOverride(
        IReadOnlyDictionary<CinematicMaterialUvModifierKey, CinematicMaterialUvModifierOverride> overrides,
        CinematicMaterialUvModifierKey key) =>
        overrides.TryGetValue(key, out CinematicMaterialUvModifierOverride uvModifier)
            ? uvModifier
            : default;

    internal static IReadOnlyList<ActiveFxPlayback> GetActiveFxPlaybacks(
        LegacyCinematicActor actor,
        PropertyClipIndex clipIndex,
        double actorClipFrame,
        LegacyCinematicTimeline? timeline,
        int renderStartFrame)
    {
        IReadOnlyList<ActiveFxClip> activeClips = clipIndex.GetActiveFxClips(actor, actorClipFrame);
        if (activeClips.Count == 0)
            return [];

        double currentSeconds = LegacyCinematicActorTiming.GetSongSecondsForTapeFrame(actorClipFrame, timeline, renderStartFrame);
        return [.. activeClips.Select(clip =>
        {
            double startSeconds = LegacyCinematicActorTiming.GetSongSecondsForTapeFrame(clip.StartFrame, timeline, renderStartFrame);
            double? generationEndSeconds = clip.IsInClipRange
                ? null
                : Math.Max(
                    0.0,
                    LegacyCinematicActorTiming.GetSongSecondsForTapeFrame(clip.StartFrame + clip.DurationFrames, timeline, renderStartFrame) - startSeconds);
            return new ActiveFxPlayback(
                clip.NameId,
                Math.Max(0.0, currentSeconds - startSeconds),
                generationEndSeconds);
        })];
    }

    internal static double GetDefaultFxElapsedSecondsForActor(
        double actorClipFrame,
        LegacyCinematicTimeline? timeline,
        int renderStartFrame)
    {
        // FXControllerComponent::onBecomeActive() starts defaultFx when the
        // actor/component becomes active. Alpha clips only affect rendering;
        // they must not restart persistent default FX such as lamp flames.
        return LegacyCinematicActorTiming.GetSongSecondsForTapeFrame(actorClipFrame, timeline, renderStartFrame);
    }

    internal static double GetSongSecondsForTapeFrame(
        double tapeFrame,
        LegacyCinematicTimeline? timeline,
        int renderStartFrame) =>
        timeline?.GetSongSecondsForTapeFrame(tapeFrame, renderStartFrame) ??
        (Math.Max(0.0, tapeFrame - renderStartFrame) / LegacyCinematicConstants.TapeTicksPerSecond);

    internal static LegacyCinematicUvRect? GetFrameUvOverride(RenderableCinematicActor actor, double materialElapsedSeconds)
    {
        if (actor.Image?.Atlas == null)
            return LegacyCinematicActorTiming.GetGeometryUvOverride(actor.Geometry);

        if (actor.Actor.ParticleTemplate != null &&
            LegacyCinematicRendererParticleCurves.TryGetParticleAtlasFrame(actor.Actor.ParticleTemplate, actor.Actor.Key, materialElapsedSeconds, out int particleAtlasIndex) &&
            LegacyCinematicAtlasContainer.TryGetUvRect(actor.Image.Atlas, particleAtlasIndex, out LegacyCinematicUvRect particleUv))
        {
            return particleUv;
        }

        return LegacyCinematicActorTiming.GetGeometryUvOverride(actor.Geometry);
    }

    internal static LegacyCinematicUvRect? GetGeometryUvOverride(RenderGeometry geometry)
    {
        if (geometry.Source != CinematicGeometrySource.RectangleAtlasQuad ||
            geometry.Vertices.Count < 3)
        {
            return null;
        }

        RenderVertex topLeft = geometry.Vertices[0];
        RenderVertex bottomRight = geometry.Vertices[2];
        LegacyCinematicUvRect rect = new((float)topLeft.U, (float)topLeft.V, (float)bottomRight.U, (float)bottomRight.V);
        return LegacyCinematicRendererParticleCurves.IsFullUvRect(rect) ? null : rect;
    }
}