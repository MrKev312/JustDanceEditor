using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class Jd2015CommunityMashupRenderPlanner
{
    private const string AnimationFolder =
        "world/jd2015/_ui/screens/cmu_ingame/grp_profile_info/animations";
    private const string InitTapePath = $"{AnimationFolder}/init.tape";
    private const string ShowTapePath = $"{AnimationFolder}/show.tape";
    private const string HideTapePath = $"{AnimationFolder}/hide.tape";

    internal static CommunityMashupRenderPlan Create(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        CinematicScene scene,
        IReadOnlyList<TapeVisit> stageVisits,
        int renderDurationFrames,
        ILogger logger)
    {
        CommunityDancerClip[] dancers =
        [.. songData.Clips
            .OfType<CommunityDancerClip>()
            .Where(clip => clip.Duration > 0)
            .OrderBy(clip => clip.StartTime)];
        CinematicActor[] templates = [.. scene.Actors.Where(IsCardTemplateActor)];
        if (dancers.Length == 0 || templates.Length == 0)
            return new CommunityMashupRenderPlan(scene, stageVisits, []);

        HashSet<string> templateKeys = new(templates.Select(actor => actor.Key), StringComparer.OrdinalIgnoreCase);
        List<CinematicActor> actors = [.. scene.Actors.Where(actor => !templateKeys.Contains(actor.Key))];
        List<PropertyClip> propertyClips = [];
        int propertyOrder = 1_000_000;
        int hideDuration = GetTapeDuration(fileSystem, HideTapePath, logger);
        for (int index = 0; index < dancers.Length; index++)
        {
            CommunityDancerClip dancer = dancers[index];
            string instanceGroup = $"cmu_dancer_{index:D3}";
            int startFrame = Math.Max(0, dancer.StartTime);
            int endFrame = Math.Max(startFrame + 1, dancer.StartTime + dancer.Duration);
            int disableFrame = Math.Min(
                Math.Max(endFrame + hideDuration, startFrame + 1),
                Math.Max(renderDurationFrames, startFrame + 1));
            foreach (CinematicActor template in templates)
            {
                CinematicActor actor = CreateCardActor(
                    fileSystem,
                    template,
                    dancer,
                    instanceGroup,
                    actors.Count,
                    logger);
                actors.Add(actor);
                AddEnableWindow(propertyClips, actor, startFrame, disableFrame, ref propertyOrder);
            }

            AppendAnimationClips(
                fileSystem,
                instanceGroup,
                startFrame,
                endFrame,
                renderDurationFrames,
                propertyClips,
                ref propertyOrder,
                logger);
        }

        logger.LogInformation(
            "Prepared {DancerCount} JD2015 CMU dancer-card instance(s) from the authored linked actor hierarchy and SHOW/HIDE tapes.",
            dancers.Length);
        return new CommunityMashupRenderPlan(scene with { Actors = actors }, stageVisits, propertyClips);
    }

    private static CinematicActor CreateCardActor(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicActor template,
        CommunityDancerClip dancer,
        string instanceGroup,
        int siblingOrder,
        ILogger logger)
    {
        CinematicActor actor = template with
        {
            Path = [instanceGroup, template.Name],
            SiblingOrder = siblingOrder,
            DefaultEnabled = false
        };
        if (template.Name.Equals("avatar", StringComparison.OrdinalIgnoreCase))
        {
            string avatarName = $"ui_avatar_{dancer.DancerAvatarId:D4}";
            string avatarPath = $"world/jd2015/_ui/avatars/{avatarName}/{avatarName}.act";
            if (fileSystem.GetFilePath(avatarPath, out _))
            {
                CinematicActor avatar = CinematicActorDocumentReader.Read(
                    fileSystem,
                    avatarPath,
                    template.Name,
                    [instanceGroup],
                    logger,
                    siblingOrder);
                return PlaceSpawnedAvatarAtAnchor(actor, avatar);
            }

            logger.LogWarning(
                "CMU avatar {AvatarId} for dancer '{DancerName}' was not found at '{AvatarPath}'.",
                dancer.DancerAvatarId,
                dancer.DancerName,
                avatarPath);
        }

        if (template.Name.Equals("flag", StringComparison.OrdinalIgnoreCase))
        {
            string countryCode = dancer.DancerCountryCode.Trim().ToLowerInvariant();
            string flagPath = $"world/jd2015/_ui/textures/flags/{countryCode}.tga";
            if (countryCode.Length > 0 && fileSystem.GetFilePath(flagPath, out _))
                return actor with { TexturePath = flagPath, TexturePaths = [flagPath] };

            logger.LogWarning(
                "CMU country flag '{CountryCode}' for dancer '{DancerName}' was not found.",
                dancer.DancerCountryCode,
                dancer.DancerName);
        }

        if (template.Name.Equals("txt_name", StringComparison.OrdinalIgnoreCase) && actor.UiText != null)
            return actor with { UiText = actor.UiText with { Text = dancer.DancerName } };

        return actor;
    }

    internal static CinematicActor PlaceSpawnedAvatarAtAnchor(
        CinematicActor anchor,
        CinematicActor avatar) => avatar with
        {
            Path = anchor.Path,
            Name = anchor.Name,
            SiblingOrder = anchor.SiblingOrder,
            RelativeZ = anchor.RelativeZ,
            ScaleX = anchor.ScaleX,
            ScaleY = anchor.ScaleY,
            XFlipped = anchor.XFlipped,
            Angle = anchor.Angle,
            PositionX = anchor.PositionX,
            PositionY = anchor.PositionY,
            ScenePriority = anchor.ScenePriority,
            DefaultEnabled = anchor.DefaultEnabled,
            BaseTint = anchor.BaseTint,
            BaseAlpha = anchor.BaseAlpha,
            ParentBind = anchor.ParentBind,
            Sinus = anchor.Sinus
        };

    private static void AppendAnimationClips(
        JustDanceUbiArtFileSystem fileSystem,
        string instanceGroup,
        int startFrame,
        int endFrame,
        int renderDurationFrames,
        List<PropertyClip> output,
        ref int propertyOrder,
        ILogger logger)
    {
        TapeVisit[] visits =
        [
            new(InitTapePath, startFrame),
            new(ShowTapePath, startFrame),
            new(HideTapePath, endFrame)
        ];
        CinematicTapeData data = CinematicTapeGraphReader.Read(
            fileSystem,
            primaryRootCandidates: [],
            additionalRoots: visits,
            videoDurationSeconds: renderDurationFrames / (double)CinematicConstants.TapeTicksPerSecond,
            resolveTapeLauncher: null,
            logger);
        foreach (PropertyClip clip in data.PropertyClips)
        {
            if (clip.Target.Segments.Count == 0)
                continue;

            output.Add(clip with
            {
                Target = new ActorTargetPath([instanceGroup, clip.Target.Segments[^1]]),
                Order = propertyOrder++
            });
        }
    }

    private static int GetTapeDuration(
        JustDanceUbiArtFileSystem fileSystem,
        string tapePath,
        ILogger logger) =>
        CinematicTapeClipReader.ReadLocalTapeClips(fileSystem, tapePath, logger)
            .Select(clip => clip.StartFrame + Math.Max(clip.DurationFrames, 0))
            .DefaultIfEmpty(0)
            .Max();

    private static bool IsCardTemplateActor(CinematicActor actor) =>
        actor.Path.Count >= 2 &&
        actor.Path[0].Equals("grp_profile_info", StringComparison.OrdinalIgnoreCase);

    private static void AddEnableWindow(
        List<PropertyClip> clips,
        CinematicActor actor,
        int startFrame,
        int endFrame,
        ref int order)
    {
        clips.Add(CreateActorEnableClip(actor, startFrame, true, order++));
        clips.Add(CreateActorEnableClip(actor, endFrame, false, order++));
    }

    private static PropertyClip CreateActorEnableClip(
        CinematicActor actor,
        int startFrame,
        bool enabled,
        int order)
    {
        CinematicCurve curve = CinematicVisualStateBuilder.CreateConstantCurve(enabled ? 1.0f : 0.0f);
        return new PropertyClip(
            new ActorTargetPath(actor.Path),
            CinematicClipIds.ActorEnable,
            startFrame,
            DurationFrames: 1,
            new CinematicVisualState(null, new CinematicMaterial(null, null, null, curve)),
            order);
    }
}
