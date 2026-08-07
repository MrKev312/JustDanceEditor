using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using KevInc.UbiArt.Cinematics.Serialization.Legacy;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;

using SixLabors.ImageSharp;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Formats.UbiArt.Tests;

internal static class CinematicRenderTimelineTestSupport
{
    internal static PropertyClip CreateAlphaClip(
        CinematicActor actor,
        int startFrame,
        int durationFrames,
        float alpha,
        int order) =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<CinematicAlphaClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: null,
                Material: new CinematicMaterial(
                    Red: null,
                    Green: null,
                    Blue: null,
                Alpha: new CinematicCurve([new CinematicKeyframe(0, alpha, 0, alpha, 0, alpha)]))),
            order);

    internal static PropertyClip CreateActorEnableClip(
        CinematicActor actor,
        int startFrame,
        int durationFrames,
        bool enabled,
        int order) =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<CinematicActorEnableClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: null,
                Material: new CinematicMaterial(
                    Red: null,
                    Green: null,
                    Blue: null,
                Alpha: new CinematicCurve([new CinematicKeyframe(0, enabled ? 1 : 0, 0, enabled ? 1 : 0, 0, enabled ? 1 : 0)]))),
            order);

    internal static PropertyClip CreatePropertyFxClip(
        CinematicActor actor,
        int startFrame,
        int durationFrames,
        uint fxNameId,
        int order,
        bool killParticlesOnEnd = false) =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<CinematicFxClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: null,
                Material: null),
            order,
            fxNameId,
            killParticlesOnEnd,
            FxGenerationDurationFrames: durationFrames);

    internal static PropertyClip CreateAnimationClip(
        CinematicActor actor,
        int startFrame,
        int durationFrames,
        int order,
        string animationName = "0x6158A88A") =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<CinematicAnimationClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: null,
                Material: null,
                Animation: new CinematicAnimationClip(animationName, Loop: true, PlayRate: 1.0f, StartFrame: 0)),
            order);

    internal static PropertyClip CreateSizeClip(
        CinematicActor actor,
        int startFrame,
        int durationFrames,
        float? scaleX,
        float? scaleY,
        int order) =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<CinematicSecondaryTransformClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: new CinematicTransform(
                    PositionX: null,
                    PositionY: null,
                    PositionZ: null,
                    Rotation: null,
                    ScaleX: scaleX == null ? null : new CinematicCurve([new CinematicKeyframe(0, scaleX.Value, 0, scaleX.Value, 0, scaleX.Value)]),
                    ScaleY: scaleY == null ? null : new CinematicCurve([new CinematicKeyframe(0, scaleY.Value, 0, scaleY.Value, 0, scaleY.Value)])),
                Material: null),
            order);

    internal static CinematicCurve CreateSingleKeyCurve(float value) =>
        new([new CinematicKeyframe(0, value, 0, value, 0, value)]);

    internal static PropertyClip CreatePivotClip(
        CinematicActor actor,
        int startFrame,
        int durationFrames,
        TextureAnchor anchor,
        int order,
        CinematicCurve? pivotX = null,
        CinematicCurve? pivotY = null) =>
        new(
            new ActorTargetPath(actor.Path),
            LegacyBinarySerializer.GetTypeId<CinematicPivotClipBinary>(),
            startFrame,
            durationFrames,
            new CinematicVisualState(
                Transform: null,
                Material: null,
                Pivot: new CinematicPivotClip(anchor, pivotX, pivotY)),
            order);

    internal static TapeClip CreateFxClip(IReadOnlyList<string> targetPath, uint fxNameId) =>
        new(
            LegacyBinarySerializer.GetTypeId<CinematicFxClipBinary>(),
            StartFrame: 10,
            DurationFrames: 64,
            Path: null,
            Targets: [new ActorTargetPath(targetPath)],
            Curves: [],
            FxNameId: fxNameId);

    internal static TapeClip CreateProportionClip(IReadOnlyList<string> targetPath) =>
        new(
            LegacyBinarySerializer.GetTypeId<CinematicProportionClipBinary>(),
            StartFrame: 10,
            DurationFrames: 21,
            Path: null,
            Targets: [new ActorTargetPath(targetPath)],
            Curves: []);

    internal static CinematicFxTemplate CreateFxTemplate(
        uint controlNameId,
        params (uint DescriptorNameId, string Name)[] emitters)
    {
        CinematicFxEmitterTemplate[] emitterTemplates =
        [
            .. emitters.Select((emitter, index) => new CinematicFxEmitterTemplate(
                index,
                emitter.DescriptorNameId,
                AngleOffsetRadians: 0.0f,
                MinDelaySeconds: 0.0f,
                MaxDelaySeconds: 0.0f,
                emitter.Name,
                TexturePath: "world/maps/test/fx.png",
                MaterialPath: null,
                ParticleTemplate: null!))
        ];
        CinematicFxControlTemplate control = new(
            controlNameId,
            StopOnEndAnim: false,
            PlayOnce: false,
            EmitFromBase: true,
            UseActorSpeed: true,
            UseActorOrientation: false,
            UseActorAlpha: true,
            UseBoneOrientation: 0,
            ParticleNameIds: [.. emitters.Select(emitter => emitter.DescriptorNameId)]);
        return new(
            emitterTemplates,
            [control],
            new Dictionary<uint, CinematicFxControlTemplate> { [controlNameId] = control },
            DefaultFxNameId: 0,
            TriggerFxNameId: 0);
    }

    internal static CinematicActor CreateActor(
        IReadOnlyList<string> path,
        uint? typeId = null,
        string? subScenePath = null,
        float x = 0,
        float y = 0,
        float z = 0,
        float scaleX = 1,
        float scaleY = 1,
        uint xFlipped = 0,
        float angle = 0,
        string? texturePath = null,
        int atlasIndex = 0,
        int atlasTextureSlot = 0,
        TextureAnchor anchor = TextureAnchor.MiddleCenter,
        float customAnchorX = 0,
        float customAnchorY = 0,
        int scenePriority = 0,
        bool defaultEnabled = true,
        int? sourceOffset = null,
        CinematicActorBind? bind = null,
        uint? visualComponentTypeId = null,
        RgbTint? baseTint = null,
        float baseAlpha = 1.0f,
        float meshInitialScaleZ = 0.0f) =>
        new(
            path,
            path[^1],
            SourceOffset: sourceOffset ?? path.Count,
            SiblingOrder: path.Count,
            typeId ?? LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            z,
            scaleX,
            scaleY,
            xFlipped,
            angle,
            x,
            y,
            "world/maps/test/template.tpl",
            subScenePath,
            texturePath,
            texturePath == null ? [] : [texturePath],
            texturePath == null ? null : "world/maps/test/material.mat",
            null,
            null,
            visualComponentTypeId ?? (texturePath == null ? null : LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicComponentBinary>()),
            atlasIndex,
            atlasTextureSlot,
            anchor,
            customAnchorX,
            customAnchorY,
            scenePriority,
            defaultEnabled,
            baseTint ?? RgbTint.White,
            baseAlpha,
            bind,
            MeshInitialScaleZ: meshInitialScaleZ);

    internal static CinematicActor CreateAnimLightActor(bool defaultEnabled = true)
    {
        const uint animationNameId = 0x6158A88A;
        RenderGeometry defaultGeometry = RenderGeometry.FromAabb(-1, -1, 1, 1, CinematicGeometrySource.AnimLightBounds);
        CinematicAnimLightAnimation animation = new(
            animationNameId,
            "world/maps/test/animation/stand.anm",
            DurationFrames: 24,
            defaultGeometry);
        CinematicAnimLightTemplate template = new(
            animationNameId,
            new Dictionary<uint, CinematicAnimLightAnimation> { [animationNameId] = animation },
            new Dictionary<string, CinematicAnimLightAnimation>(StringComparer.OrdinalIgnoreCase)
            {
                [animation.AnimationPath] = animation
            },
            defaultGeometry,
            SkeletonPath: null,
            PatchBankPath: null,
            TexturePathsByPatchBankNameId: new Dictionary<uint, string>());

        return CreateActor(
            ["map_graph", "animated_light"],
            texturePath: "world/maps/test/graph/textures/light.tga",
            defaultEnabled: defaultEnabled,
            visualComponentTypeId: LegacyBinarySerializer.GetTypeId<CinematicAnimLightComponentBinary>()) with
        {
            AnimLightTemplate = template
        };
    }

    internal static CinematicAnimLightRig CreateAnimLightRig(
        CinematicAnimLightSkeleton skeleton,
        uint patchBankNameId,
        CinematicAnimLightPatchBank patchBank) =>
        new(
            skeleton,
            patchBank,
            new Dictionary<uint, CinematicAnimLightPatchBank> { [patchBankNameId] = patchBank });

}
