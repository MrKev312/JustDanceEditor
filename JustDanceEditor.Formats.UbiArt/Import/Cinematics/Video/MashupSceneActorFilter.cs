using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Rendering;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class MashupSceneActorFilter
{
    private const string MashupTransitionFlashActorKeyMarker = "x_flashcoach";
    private static readonly string[] MashupCoachPlacementActorSuffixes =
    [
        "/coachs/circle03_coach06_center",
        "/txcomponent_4x_centerstraight01",
        "/txcomponent_4x_centerstraight02",
        "/txcomponent_8x_left",
        "/txcomponent_8x_top",
        "/txcomponent_8x_bottom",
        "/txcomponent_8x_righta"
    ];

    internal static void ValidateSharedScene(
        CinematicScene scene,
        string sceneSongName,
        string mashupName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneSongName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mashupName);

        if (scene.Actors.Any(actor => actor.Key.StartsWith("_mashup_graph/", StringComparison.OrdinalIgnoreCase)))
            return;

        throw new InvalidOperationException(
            $"Mashup '{mashupName}' must render the shared '{sceneSongName}' scene, but no authored '_mashup_graph' actors were loaded. Refusing to render over the original map background.");
    }

    internal static MashupCoachLayerPlacement ResolveCoachLayerPlacement(
        CinematicScene scene,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(logger);

        CinematicActor? placementActor = scene.Actors
            .FirstOrDefault(actor => IsCoachPlacementActorKey(actor.Key));
        if (placementActor == null)
        {
            logger.LogWarning(
                "Could not find a cooked coach placement actor in the shared '_mashup' scene; using fallback mashup coach layer placement.");
            return MashupCoachLayerPlacement.Fallback;
        }

        IReadOnlyDictionary<string, ResolvedActorState> baseStates =
            CinematicActorStateResolver.ResolveActorBaseStates(scene.Actors, new CinematicRenderRuntime(scene));
        ResolvedActorState centerState = baseStates.TryGetValue(placementActor.Key, out ResolvedActorState? resolved)
            ? resolved
            : new ResolvedActorState(
                placementActor.PositionX,
                placementActor.PositionY,
                placementActor.RelativeZ,
                placementActor.ScaleX,
                placementActor.ScaleY,
                placementActor.Angle,
                placementActor.BaseAlpha,
                placementActor.BaseTint,
                placementActor.XFlipped != 0);

        MashupCoachLayerPlacement placement = new(
            centerState.PositionZ,
            placementActor.ScenePriority,
            placementActor.SourceOffset);
        logger.LogInformation(
            "Using cooked mashup center-coach placement actor '{ActorKey}' for renderer-composited coach placement (z {Depth:0.###}, priority {Priority}, primitive {Primitive}).",
            placementActor.Key,
            placement.Depth,
            placement.ScenePriority,
            placement.PrimitiveTieBreak);
        return placement;
    }

    internal static CinematicActor CreateSyntheticCoachActor(int index, MashupCoachLayerPlacement placement) =>
        new(
            Path: ["_mashup_graph", "renderer_coaches", $"coach_{index:D2}"],
            Name: $"renderer_coach_{index:D2}",
            SourceOffset: placement.PrimitiveTieBreak - index,
            SiblingOrder: index,
            TypeId: 0,
            RelativeZ: placement.Depth,
            ScaleX: 1,
            ScaleY: 1,
            XFlipped: 0,
            Angle: 0,
            PositionX: 0,
            PositionY: 0,
            TemplatePath: string.Empty,
            SubScenePath: null,
            TexturePath: null,
            TexturePaths: [],
            MaterialPath: null,
            ExplicitAtlasPath: null,
            MeshPath: null,
            VisualComponentTypeId: null,
            AtlasIndex: -1,
            AtlasTextureSlot: -1,
            Anchor: TextureAnchor.MiddleCenter,
            CustomAnchorX: 0,
            CustomAnchorY: 0,
            ScenePriority: placement.ScenePriority,
            DefaultEnabled: true,
            BaseTint: RgbTint.White,
            BaseAlpha: 1,
            ParentBind: null);

    internal static bool IsBackgroundActorKey(string key) =>
        true;

    internal static bool ShouldRenderSceneActor(
        CinematicActor actor,
        IReadOnlySet<string> transitionFxActorKeys) =>
        (IsBackgroundActorKey(actor.Key) ||
            transitionFxActorKeys.Contains(actor.Key) ||
            IsAuthoredTransitionFxActor(actor));

    internal static bool ShouldRenderSceneActor(
        RenderableCinematicActor actor,
        IReadOnlySet<string> transitionFxActorKeys) =>
        (IsBackgroundActor(actor, transitionFxActorKeys) ||
            IsTransitionOverlayActor(actor, transitionFxActorKeys));

    internal static RenderableCinematicActor ApplyTransitionOverlayPlane(
        RenderableCinematicActor actor,
        IReadOnlySet<string> transitionFxActorKeys) =>
        IsTransitionOverlayActor(actor, transitionFxActorKeys)
            ? actor with { Plane = CinematicLayerPlane.TransitionOverlay }
            : actor;

    internal static bool IsCoachFlashFxActor(
        CinematicActor actor,
        IReadOnlySet<uint> transitionFxNameIds)
    {
        if (actor.FxTemplate is not { } fxTemplate)
            return false;

        foreach (CinematicFxControlTemplate control in fxTemplate.Controls)
        {
            if (!transitionFxNameIds.Contains(control.NameId))
                continue;

            foreach (uint particleNameId in control.ParticleNameIds)
            {
                foreach (CinematicFxEmitterTemplate emitter in fxTemplate.Emitters)
                {
                    if (emitter.DescriptorNameId != particleNameId)
                        continue;

                    if (IsCoachFlashEmitterName(emitter.Name))
                        return true;
                }
            }
        }

        return false;
    }

    internal static bool IsAuthoredTransitionFxActor(CinematicActor actor)
    {
        if (!actor.Key.Contains("/fx/", StringComparison.OrdinalIgnoreCase))
            return false;

        string key = actor.Key;
        if (key.Contains(MashupTransitionFlashActorKeyMarker, StringComparison.OrdinalIgnoreCase) ||
            key.Contains("x_mashup_godray", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (actor.FxTemplate is not { } fxTemplate)
            return false;

        foreach (CinematicFxEmitterTemplate emitter in fxTemplate.Emitters)
        {
            if (IsCoachFlashEmitterName(emitter.Name))
                return true;
        }

        return false;
    }

    internal static bool IsCoachPlacementActorKey(string key) =>
        MashupCoachPlacementActorSuffixes.Any(suffix => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    private static bool IsBackgroundActor(RenderableCinematicActor actor, IReadOnlySet<string> transitionFxActorKeys) =>
        IsBackgroundActorKey(actor.Actor.Key) &&
        !IsTransitionOverlayActor(actor, transitionFxActorKeys);

    private static bool IsTransitionOverlayActor(
        RenderableCinematicActor actor,
        IReadOnlySet<string> transitionFxActorKeys) =>
        (transitionFxActorKeys.Contains(actor.Actor.Key) ||
            IsAuthoredTransitionFxActor(actor.Actor));

    private static bool IsCoachFlashEmitterName(string? emitterName) =>
        !string.IsNullOrWhiteSpace(emitterName) &&
        emitterName.Contains("godray_coach", StringComparison.OrdinalIgnoreCase);
}
