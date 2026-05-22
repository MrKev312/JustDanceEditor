using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

internal static class LegacyCinematicActorBuilder
{
    public static IReadOnlyList<RenderableCinematicActor> BuildRenderableActors(
        LegacyCinematicScene scene,
        IReadOnlyDictionary<string, MaterializedCinematicImage> images,
        bool includeVideoOutput = false)
    {
        List<RenderableCinematicActor> actors = [];
        HashSet<string> skippedActors = LegacyCinematicActorBuilder.ReadSkippedActors();
        const bool includeUnsupportedBlend = false;

        foreach (LegacyCinematicActor actor in scene.Actors)
        {
            if (LegacyCinematicActorBuilder.IsExplicitlySkipped(actor, skippedActors))
            {
                continue;
            }

            if (actor.TexturePath == null || !images.TryGetValue(actor.Key, out MaterializedCinematicImage? image))
                continue;

            if (!includeUnsupportedBlend && LegacyCinematicActorBuilder.RequiresUnsupportedBlendMode(image.Material.BlendMode))
                continue;

            RenderGeometry geometry = LegacyCinematicRenderEngine.CreateRenderGeometry(image);
            actors.Add(new RenderableCinematicActor(
                actor,
                image,
                geometry,
                CinematicLayerPlane.Background,
                actor.ScenePriority,
                actor.RelativeZ,
                actor.SourceOffset));
        }

        if (includeVideoOutput)
        {
            foreach (LegacyCinematicActor actor in scene.Actors.Where(IsVideoOutputActor))
            {
                if (LegacyCinematicActorBuilder.IsExplicitlySkipped(actor, skippedActors))
                    continue;

                actors.Add(new RenderableCinematicActor(
                    actor,
                    null,
                    LegacyCinematicRenderEngine.CreatePleoVideoGeometry(),
                    CinematicLayerPlane.Composite,
                    actor.ScenePriority,
                    actor.RelativeZ,
                    actor.SourceOffset,
                    CinematicRenderKind.PleoVideo));
            }
        }

        return [.. actors
            .OrderBy(actor => actor.ScenePriority)
            .ThenBy(actor => actor.DrawOrderZ)
            .ThenByDescending(actor => actor.PrimitiveTieBreak)];
    }

    internal static HashSet<string> ReadSkippedActors() => [];

    internal static bool IsExplicitlySkipped(LegacyCinematicActor actor, IReadOnlySet<string> skippedActors) =>
        skippedActors.Contains(LegacyCinematicNames.NormalizeKey([actor.Name])) ||
        skippedActors.Contains(actor.Key);

    internal static bool RequiresUnsupportedBlendMode(int blendMode) =>
        blendMode is not (
            LegacyCinematicFrameRenderer.GfxBlendCopy or
            LegacyCinematicFrameRenderer.GfxBlendAlpha or
            LegacyCinematicFrameRenderer.GfxBlendAlphaPremult or
            LegacyCinematicFrameRenderer.GfxBlendAdd or
            LegacyCinematicFrameRenderer.GfxBlendAddAlpha or
            LegacyCinematicFrameRenderer.GfxBlendSubAlpha or
            LegacyCinematicFrameRenderer.GfxBlendSub or
            LegacyCinematicFrameRenderer.GfxBlendMul or
            LegacyCinematicFrameRenderer.GfxBlendAlphaMul or
            LegacyCinematicFrameRenderer.GfxBlendInvAlphaMul or
            LegacyCinematicFrameRenderer.GfxBlendMul2X or
            LegacyCinematicFrameRenderer.GfxBlendScreen);

    internal static bool IsVideoOutputActor(LegacyCinematicActor actor) =>
        actor.VisualComponentTypeId == LegacyBinarySerializer.GetTypeId<LegacyCinematicPleoTextureGraphicComponentBinary>() &&
        actor.Path.Count > 0 &&
        string.Equals(actor.Path[^1], "VideoOutput", StringComparison.OrdinalIgnoreCase);

}