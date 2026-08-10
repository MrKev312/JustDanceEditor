using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Scene;
using KevInc.UbiArt.Cinematics.Timeline;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicSceneReader
{
    public static CinematicScene ReadSceneGraph(JustDanceUbiArtFileSystem fileSystem, ILogger logger)
    {
        string mainScenePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, $"{fileSystem.SongName}_main_scene.isc");
        string graphScenePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "graph", $"{fileSystem.SongName}_graph.isc");
        string videoScenePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "videoscoach", $"{fileSystem.SongName}_video.isc");
        List<CinematicActor> actors = [];

        bool readMainScene = CinematicSceneReader.TryReadSceneFile(fileSystem, mainScenePath, [], actors, logger);
        if (!readMainScene || actors.Count == 0)
        {
            CinematicSceneReader.TryReadSceneFile(fileSystem, graphScenePath, [$"{fileSystem.SongName}_GRAPH"], actors, logger);
            CinematicSceneReader.TryReadSceneFile(fileSystem, videoScenePath, [$"{fileSystem.SongName}_VIDEO"], actors, logger);
        }
        else
        {
            int actorCountBeforeGraphScene = actors.Count;
            CinematicSceneReader.TryAppendUniqueSceneFile(fileSystem, graphScenePath, [$"{fileSystem.SongName}_GRAPH"], actors, logger);
            if (actors.Count > actorCountBeforeGraphScene)
            {
                logger.LogInformation(
                    "Legacy main scene was missing {Count} graph actor(s); appended unique actors from '{GraphScenePath}'.",
                    actors.Count - actorCountBeforeGraphScene,
                    graphScenePath);
            }

            if (!CinematicSceneReader.HasRootVideoOutputActor(actors, fileSystem, logger))
            {
                int actorCountBeforeVideoScene = actors.Count;
                CinematicSceneReader.TryReadSceneFile(fileSystem, videoScenePath, [$"{fileSystem.SongName}_VIDEO"], actors, logger);
                if (actors.Count > actorCountBeforeVideoScene)
                {
                    logger.LogInformation(
                        "Legacy main scene did not resolve a video output actor; appended videoscoach scene '{VideoScenePath}'.",
                        videoScenePath);
                }
            }
        }

        CinematicSceneReader.AddJustDanceRuntimeCameras(actors);
        return CinematicTemplateVisualResolver.ResolveTemplateVisuals(new CinematicScene([.. actors]), fileSystem, logger);
    }

    public static CinematicScene ReadCommunityMashupSceneGraph(
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        string? mapsFolder = Path.GetDirectoryName(fileSystem.InputFolders.SelectedMapWorldFolder);
        if (string.IsNullOrWhiteSpace(mapsFolder))
            throw new InvalidDataException("The map folder has no parent from which to resolve the CMU generic stage.");

        string stageFolder = Path.Combine(mapsFolder, "_communitymashup");
        string stageMainScenePath = fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015
            ? Path.Combine("world", "jd2015", "_ui", "screens", "cmu_ingame", "pag_cmu_ingame.isc")
            : Path.Combine(stageFolder, "_communitymashup_main_scene.isc");
        string stageGraphScenePath = Path.Combine(stageFolder, "graph", "_communitymashup_graph.isc");
        string selectedVideoScenePath = Path.Combine(
            fileSystem.InputFolders.SelectedMapWorldFolder,
            "videoscoach",
            $"{fileSystem.SongName}_video.isc");
        List<CinematicActor> actors = [];
        CinematicViewFamily viewFamily = CinematicViewFamily.World;

        bool readStage = false;
        try
        {
            readStage = TryReadSceneFile(
                fileSystem,
                stageMainScenePath,
                [],
                actors,
                out viewFamily,
                logger);
        }
        catch (InvalidDataException ex)
        {
            actors.Clear();
            logger.LogDebug(
                ex,
                "Could not decode the generic CMU stage wrapper '{StagePath}'; loading its graph scene directly.",
                stageMainScenePath);
        }

        if (!readStage || actors.Count == 0)
        {
            try
            {
                if (TryReadSceneFile(
                    fileSystem,
                    stageGraphScenePath,
                    ["_CommunityMashup_GRAPH"],
                    actors,
                    out CinematicViewFamily graphViewFamily,
                    logger))
                {
                    viewFamily = graphViewFamily;
                }
            }
            catch (InvalidDataException ex)
            {
                logger.LogError(ex, "Could not decode the generic CMU graph '{StagePath}'.", stageGraphScenePath);
                throw;
            }
        }

        if (actors.Count == 0)
            throw new FileNotFoundException("The JD generic CMU stage could not be loaded.", stageMainScenePath);

        if (fileSystem.VersionProfile.EngineVersion == UbiArtEngineVersion.JD2015)
            AppendJd2015CommunityDancerCardActors(fileSystem, actors, logger);
        else
            TryReadSceneFile(
                fileSystem,
                Path.Combine("world", "ui", "objects", "cmr_dancercard_ingame", "cmr_dancercard_ingame.isc"),
                ["cmr_dancercard_ingame"],
                actors,
                logger);

        bool readVideoScene = TryReadSceneFile(
            fileSystem,
            selectedVideoScenePath,
            [$"{fileSystem.SongName}_VIDEO"],
            actors,
            logger);

        AddJustDanceRuntimeCameras(actors);
        if (readVideoScene)
        {
            logger.LogInformation(
                "Loaded the JD-configured CMU stage '{StagePath}' with authored song video scene '{VideoPath}'.",
                readStage ? stageMainScenePath : stageGraphScenePath,
                selectedVideoScenePath);
        }
        else
        {
            logger.LogInformation(
                "Loaded the JD-configured CMU stage '{StagePath}' without a local song video scene; the remix video is externally bound by the game.",
                readStage ? stageMainScenePath : stageGraphScenePath);
        }
        return CinematicTemplateVisualResolver.ResolveTemplateVisuals(
            new CinematicScene([.. actors], viewFamily),
            fileSystem,
            logger);
    }

    private static void AppendJd2015CommunityDancerCardActors(
        JustDanceUbiArtFileSystem fileSystem,
        List<CinematicActor> actors,
        ILogger logger)
    {
        const string profileFolder = "world/jd2015/_ui/screens/cmu_ingame/grp_profile_info";
        CinematicScene hierarchy = CinematicActorDocumentReader.ReadHierarchy(
            fileSystem,
            $"{profileFolder}/grp_profile_info.act",
            "grp_profile_info",
            parentPath: [],
            logger,
            siblingOrder: actors.Count);
        actors.AddRange(hierarchy.Actors.Skip(1).Select(actor => actor with { DefaultEnabled = false }));
    }

    private static bool HasRootVideoOutputActor(
        IReadOnlyList<CinematicActor> actors,
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        CinematicScene resolvedScene = CinematicTemplateVisualResolver.ResolveTemplateVisuals(
            new CinematicScene(actors),
            fileSystem,
            logger);
        return resolvedScene.Actors.Any(actor =>
            actor.VisualComponentTypeId == CinematicComponentIds.PleoTextureGraphic &&
            actor.Path.Count >= 2 &&
            actor.Path[0].EndsWith("_VIDEO", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(actor.Path[^1], "VideoOutput", StringComparison.OrdinalIgnoreCase));
    }

    internal static void AddJustDanceRuntimeCameras(List<CinematicActor> actors)
    {
        CinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD", "world/_common/camera/fixedcamera.act");
        CinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD_Remote", "world/_common/camera/fixedcamera_remote.act");
        CinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD_WDF", "world/_common/camera/fixedcamera_wdf.act");
        CinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD_WDF_Remote", "world/_common/camera/fixedcamera_remote_wdf.act");
    }

    internal static void AddJustDanceRuntimeCamera(
        List<CinematicActor> actors,
        string name,
        string templatePath)
    {
        string key = CinematicNames.NormalizeKey([name]);
        if (actors.Any(actor => string.Equals(actor.Key, key, StringComparison.OrdinalIgnoreCase)))
            return;

        // JD2014 camera tapes target a camera actor that is not serialized in
        // the graph scene, so the virtual actor starts at the spawn position
        // and PositionClips provide the camera transform.
        actors.Add(new CinematicActor(
            [name],
            name,
            SourceOffset: -1,
            SiblingOrder: actors.Count,
            TypeId: CinematicActorIds.SceneActor,
            RelativeZ: 0.0f,
            ScaleX: 1.0f,
            ScaleY: 1.0f,
            XFlipped: 0,
            Angle: 0.0f,
            PositionX: 0.0f,
            PositionY: 0.0f,
            TemplatePath: CinematicNames.NormalizePath(templatePath),
            SubScenePath: null,
            TexturePath: null,
            TexturePaths: [],
            MaterialPath: null,
            ExplicitAtlasPath: null,
            MeshPath: null,
            VisualComponentTypeId: null,
            AtlasIndex: 0,
            AtlasTextureSlot: 0,
            Anchor: TextureAnchor.MiddleCenter,
            CustomAnchorX: 0.0f,
            CustomAnchorY: 0.0f,
            ScenePriority: 0,
            DefaultEnabled: true,
            BaseTint: RgbTint.White,
            BaseAlpha: 1.0f,
            ParentBind: null));
    }

    internal static CinematicScene AddSpawnedActors(
        CinematicScene scene,
        JustDanceUbiArtFileSystem fileSystem,
        IReadOnlyList<CinematicSpawnActorRuntimeClip> spawnClips,
        ILogger logger) =>
        CinematicSpawnActorMaterializer.AddSpawnedActors(scene, fileSystem, spawnClips, logger);

    internal static bool TryReadSceneFile(
        JustDanceUbiArtFileSystem fileSystem,
        string relativePath,
        IReadOnlyList<string> parentPath,
        List<CinematicActor> actors,
        ILogger logger)
    {
        return CinematicSceneDocumentReader.TryRead(fileSystem, relativePath, parentPath, actors, logger);
    }

    internal static bool TryReadSceneFile(
        JustDanceUbiArtFileSystem fileSystem,
        string relativePath,
        IReadOnlyList<string> parentPath,
        List<CinematicActor> actors,
        out CinematicViewFamily viewFamily,
        ILogger logger) =>
        CinematicSceneDocumentReader.TryRead(
            fileSystem,
            relativePath,
            parentPath,
            actors,
            out viewFamily,
            logger);

    private static bool TryAppendUniqueSceneFile(
        JustDanceUbiArtFileSystem fileSystem,
        string relativePath,
        IReadOnlyList<string> parentPath,
        List<CinematicActor> actors,
        ILogger logger)
    {
        List<CinematicActor> loadedActors = [];
        if (!CinematicSceneReader.TryReadSceneFile(fileSystem, relativePath, parentPath, loadedActors, logger))
            return false;

        HashSet<string> existingKeys = new(actors.Select(actor => actor.Key), StringComparer.OrdinalIgnoreCase);
        foreach (CinematicActor actor in loadedActors)
        {
            if (existingKeys.Add(actor.Key))
                actors.Add(actor);
        }

        return true;
    }

    internal static SceneReadResult ReadScene(
        JustDanceUbiArtFileSystem fileSystem,
        byte[] bytes,
        int offset,
        IReadOnlyList<string> parentPath,
        int depth,
        ILogger logger) =>
        CinematicSceneDocumentReader.Read(fileSystem, bytes, offset, parentPath, depth, logger);

}
