using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class CommunityMashupRenderPlanner
{
    internal static CommunityMashupRenderPlan Create(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        CinematicScene scene,
        IReadOnlyList<TapeVisit> stageVisits,
        int renderDurationFrames,
        ILogger logger)
    {
        return fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015
            ? Jd2015CommunityMashupRenderPlanner.Create(
                fileSystem,
                songData,
                scene,
                stageVisits,
                renderDurationFrames,
                logger)
            : CreateModern(fileSystem, songData, scene, stageVisits, logger);
    }

    private static CommunityMashupRenderPlan CreateModern(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        CinematicScene scene,
        IReadOnlyList<TapeVisit> stageVisits,
        ILogger logger)
    {
        const string cardRoot = "cmr_dancercard_ingame";
        CinematicActor[] cardActors = [.. scene.Actors
            .Where(actor => actor.Path.Count > 0 &&
                string.Equals(actor.Path[0], cardRoot, StringComparison.OrdinalIgnoreCase))];
        if (cardActors.Length == 0)
        {
            logger.LogWarning("The authored modern CMU dancer-card scene was not available.");
            return new CommunityMashupRenderPlan(scene, stageVisits, []);
        }

        List<CinematicActor> actors = [.. scene.Actors.Select(actor =>
            cardActors.Contains(actor) ? NormalizeModernCardActor(actor) : actor)];
        cardActors = [.. actors.Where(actor => actor.Path.Count > 0 &&
            string.Equals(actor.Path[0], cardRoot, StringComparison.OrdinalIgnoreCase))];
        CommunityDancerClip[] dancers = [.. songData.Clips
            .OfType<CommunityDancerClip>()
            .Where(clip => clip.Duration > 0)
            .OrderBy(clip => clip.StartTime)];

        CinematicActor? avatarTemplate = cardActors.FirstOrDefault(actor =>
            string.Equals(actor.Name, "avatar", StringComparison.OrdinalIgnoreCase));
        CinematicActor? flagTemplate = cardActors.FirstOrDefault(actor =>
            string.Equals(actor.Name, "flag", StringComparison.OrdinalIgnoreCase));
        HashSet<string> dynamicTemplateKeys = new(StringComparer.OrdinalIgnoreCase);
        if (avatarTemplate != null)
            dynamicTemplateKeys.Add(avatarTemplate.Key);
        if (flagTemplate != null)
            dynamicTemplateKeys.Add(flagTemplate.Key);

        actors.RemoveAll(actor =>
            dynamicTemplateKeys.Contains(actor.Key) ||
            string.Equals(actor.Name, "DANCER_NAME", StringComparison.OrdinalIgnoreCase));

        List<PropertyClip> propertyClips = [];
        int propertyOrder = 1_000_000;
        CinematicActor[] sharedActors = [.. cardActors.Where(actor =>
            !dynamicTemplateKeys.Contains(actor.Key) &&
            !string.Equals(actor.Name, "DANCER_NAME", StringComparison.OrdinalIgnoreCase))];
        foreach (CommunityDancerClip dancer in dancers)
        {
            int startFrame = Math.Max(0, dancer.StartTime);
            int endFrame = Math.Max(startFrame + 1, dancer.StartTime + dancer.Duration);
            foreach (CinematicActor sharedActor in sharedActors)
                AddEnableWindow(propertyClips, sharedActor, startFrame, endFrame, ref propertyOrder);
        }

        for (int index = 0; index < dancers.Length; index++)
        {
            CommunityDancerClip dancer = dancers[index];
            int startFrame = Math.Max(0, dancer.StartTime);
            int endFrame = Math.Max(startFrame + 1, dancer.StartTime + dancer.Duration);
            if (avatarTemplate != null && TryCreateModernAvatar(
                    fileSystem,
                    avatarTemplate,
                    dancer,
                    index,
                    actors.Count,
                    logger,
                    out CinematicActor? avatar))
            {
                actors.Add(avatar);
                AddEnableWindow(propertyClips, avatar, startFrame, endFrame, ref propertyOrder);
            }

            if (flagTemplate != null && TryCreateModernFlag(
                    fileSystem,
                    flagTemplate,
                    dancer,
                    index,
                    actors.Count,
                    logger,
                    out CinematicActor? flag))
            {
                actors.Add(flag);
                AddEnableWindow(propertyClips, flag, startFrame, endFrame, ref propertyOrder);
            }
        }

        logger.LogInformation(
            "Prepared {DancerCount} authored modern CMU dancer-card interval(s) with current avatar and country assets.",
            dancers.Length);
        return new CommunityMashupRenderPlan(scene with { Actors = actors }, stageVisits, propertyClips);
    }

    private static CinematicActor NormalizeModernCardActor(CinematicActor actor)
    {
        CinematicActorBind? bind = actor.ParentBind;
        return actor with
        {
            PositionX = actor.PositionX / CinematicConstants.GraphPixelsPerWorldUnit,
            PositionY = actor.PositionY / CinematicConstants.GraphPixelsPerWorldUnit,
            DefaultEnabled = false,
            ParentBind = bind == null
                ? null
                : bind with
                {
                    OffsetX = bind.OffsetX / CinematicConstants.GraphPixelsPerWorldUnit,
                    OffsetY = bind.OffsetY / CinematicConstants.GraphPixelsPerWorldUnit,
                    LocalScaleX = NormalizeUiScale(bind.LocalScaleX),
                    LocalScaleY = NormalizeUiScale(bind.LocalScaleY)
                }
        };
    }

    private static float NormalizeUiScale(float scale) =>
        MathF.Abs(scale) > 8.0f
            ? scale / CinematicConstants.GraphPixelsPerWorldUnit
            : scale;

    private static bool TryCreateModernAvatar(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicActor template,
        CommunityDancerClip dancer,
        int dancerIndex,
        int siblingOrder,
        ILogger logger,
        [NotNullWhen(true)] out CinematicActor? actor)
    {
        string texturePath = $"world/avatars/{dancer.DancerAvatarId:D4}/avatar.png";
        if (!fileSystem.GetFilePath(texturePath, out _))
        {
            logger.LogWarning(
                "CMU avatar {AvatarId} for dancer '{DancerName}' was not found at '{AvatarPath}'.",
                dancer.DancerAvatarId,
                dancer.DancerName,
                texturePath);
            actor = null;
            return false;
        }

        actor = template with
        {
            Path = [.. template.Path.Take(template.Path.Count - 1), $"avatar_{dancerIndex:D3}"],
            Name = $"avatar_{dancerIndex:D3}",
            SiblingOrder = siblingOrder,
            TexturePath = texturePath,
            TexturePaths = [.. template.TexturePaths.Select(path =>
                path.Contains("avatar_placeholder", StringComparison.OrdinalIgnoreCase)
                    ? texturePath
                    : path)],
            DefaultEnabled = false
        };
        return true;
    }

    private static bool TryCreateModernFlag(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicActor template,
        CommunityDancerClip dancer,
        int dancerIndex,
        int siblingOrder,
        ILogger logger,
        [NotNullWhen(true)] out CinematicActor? actor)
    {
        string countryCode = dancer.DancerCountryCode.Trim().ToLowerInvariant();
        string texturePath = $"world/ui/textures/flags/{countryCode}.tga";
        if (countryCode.Length == 0 || !fileSystem.GetFilePath(texturePath, out _))
        {
            logger.LogWarning(
                "CMU country flag '{CountryCode}' for dancer '{DancerName}' was not found.",
                dancer.DancerCountryCode,
                dancer.DancerName);
            actor = null;
            return false;
        }

        actor = template with
        {
            Path = [.. template.Path.Take(template.Path.Count - 1), $"flag_{dancerIndex:D3}"],
            Name = $"flag_{dancerIndex:D3}",
            SiblingOrder = siblingOrder,
            TexturePath = texturePath,
            TexturePaths = [texturePath],
            DefaultEnabled = false
        };
        return true;
    }

    private static void AddEnableWindow(
        List<PropertyClip> clips,
        CinematicActor actor,
        int startFrame,
        int endFrame,
        ref int order)
    {
        clips.Add(CreateActorEnableClip(actor, startFrame, enabled: true, order++));
        clips.Add(CreateActorEnableClip(actor, endFrame, enabled: false, order++));
    }

    private static PropertyClip CreateActorEnableClip(
        CinematicActor actor,
        int startFrame,
        bool enabled,
        int order)
    {
        float value = enabled ? 1.0f : 0.0f;
        CinematicCurve curve = new([new CinematicKeyframe(0, value, 0, value, 0, value)]);
        return new PropertyClip(
            new ActorTargetPath(actor.Path),
            CinematicClipIds.ActorEnable,
            startFrame,
            DurationFrames: 1,
            new CinematicVisualState(
                Transform: null,
                Material: new CinematicMaterial(null, null, null, curve)),
            order);
    }
}

internal sealed record CommunityMashupRenderPlan(
    CinematicScene Scene,
    IReadOnlyList<TapeVisit> TapeVisits,
    IReadOnlyList<PropertyClip> PropertyClips);
