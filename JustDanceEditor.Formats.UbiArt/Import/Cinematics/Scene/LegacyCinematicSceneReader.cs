using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class LegacyCinematicSceneReader
{
    public static LegacyCinematicScene ReadSceneGraph(JustDanceUbiArtFileSystem fileSystem, ILogger logger)
    {
        string mainScenePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, $"{fileSystem.SongName}_main_scene.isc");
        string graphScenePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "graph", $"{fileSystem.SongName}_graph.isc");
        string videoScenePath = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "videoscoach", $"{fileSystem.SongName}_video.isc");
        List<LegacyCinematicActor> actors = [];

        bool readGraphScene = LegacyCinematicSceneReader.TryReadSceneFile(fileSystem, graphScenePath, [$"{fileSystem.SongName}_GRAPH"], actors, logger);
        bool readVideoScene = LegacyCinematicSceneReader.TryReadSceneFile(fileSystem, videoScenePath, [$"{fileSystem.SongName}_VIDEO"], actors, logger);

        if (actors.Count == 0 && !readGraphScene && !readVideoScene)
            LegacyCinematicSceneReader.TryReadSceneFile(fileSystem, mainScenePath, [], actors, logger);

        LegacyCinematicSceneReader.AddJustDanceRuntimeCameras(actors);
        return LegacyCinematicTemplateVisualResolver.ResolveTemplateVisuals(new LegacyCinematicScene([.. actors]), fileSystem, logger);
    }

    internal static void AddJustDanceRuntimeCameras(List<LegacyCinematicActor> actors)
    {
        LegacyCinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD", "world/_common/camera/fixedcamera.act");
        LegacyCinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD_Remote", "world/_common/camera/fixedcamera_remote.act");
        LegacyCinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD_WDF", "world/_common/camera/fixedcamera_wdf.act");
        LegacyCinematicSceneReader.AddJustDanceRuntimeCamera(actors, "Camera_JD_WDF_Remote", "world/_common/camera/fixedcamera_remote_wdf.act");
    }

    internal static void AddJustDanceRuntimeCamera(
        List<LegacyCinematicActor> actors,
        string name,
        string templatePath)
    {
        string key = LegacyCinematicNames.NormalizeKey([name]);
        if (actors.Any(actor => string.Equals(actor.Key, key, StringComparison.OrdinalIgnoreCase)))
            return;

        // JD_GameManager::initCamerasWorld spawns these actors outside the graph
        // scene from FixedCamera.act. JD2014 camera tapes are authored against
        // that runtime actor, so the virtual actor starts at the spawn position
        // and the PositionClips provide the camera transform.
        actors.Add(new LegacyCinematicActor(
            [name],
            name,
            SourceOffset: -1,
            SiblingOrder: actors.Count,
            TypeId: LegacyBinarySerializer.GetTypeId<LegacyCinematicSceneActorBinary>(),
            RelativeZ: 0.0f,
            ScaleX: 1.0f,
            ScaleY: 1.0f,
            XFlipped: 0,
            Angle: 0.0f,
            PositionX: 0.0f,
            PositionY: 0.0f,
            TemplatePath: LegacyCinematicNames.NormalizePath(templatePath),
            SubScenePath: null,
            TexturePath: null,
            TexturePaths: [],
            MaterialPath: null,
            MeshPath: null,
            VisualComponentTypeId: null,
            AtlasIndex: 0,
            Anchor: TextureAnchor.MiddleCenter,
            CustomAnchorX: 0.0f,
            CustomAnchorY: 0.0f,
            ScenePriority: 0,
            BaseTint: RgbTint.White,
            BaseAlpha: 1.0f,
            ParentBind: null));
    }

    internal static bool TryReadSceneFile(
        JustDanceUbiArtFileSystem fileSystem,
        string relativePath,
        IReadOnlyList<string> parentPath,
        List<LegacyCinematicActor> actors,
        ILogger logger)
    {
        if (!fileSystem.GetFilePath(relativePath, out CookedFile? file))
            return false;

        try
        {
            byte[] bytes = LegacyCinematicSceneBoundaryReader.ReadFileBytes(fileSystem, file);
            SceneReadResult result = LegacyCinematicSceneReader.ReadScene(fileSystem, bytes, 0, parentPath, 0, logger);
            actors.AddRange(result.Actors);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException($"Could not read legacy cinematic scene '{relativePath}'.", ex);
        }
    }

    internal static SceneReadResult ReadScene(
        JustDanceUbiArtFileSystem fileSystem,
        byte[] bytes,
        int offset,
        IReadOnlyList<string> parentPath,
        int depth,
        ILogger logger)
    {
        LegacyCinematicBinaryReader reader = new(bytes, offset);
        LegacyBinarySerializerContext serializerContext = new((int)fileSystem.VersionProfile.EngineVersion);
        int version = reader.ReadInt32();
        if (version != 1)
            throw new InvalidDataException($"Unsupported legacy scene version {version} at 0x{offset:X}.");

        _ = reader.ReadUInt32();
        reader.Skip(15);
        int actorCount = reader.ReadByte();
        if (actorCount is < 0 or > 512)
            throw new InvalidDataException($"Invalid legacy scene actor count {actorCount} at 0x{reader.Offset - 1:X}.");

        bool debugScene = false;
        if (actorCount > 0)
            LegacyCinematicSceneBoundaryReader.SnapSceneActorListStartToNextBoundary(reader, depth, parentPath, logger, debugScene);

        if (debugScene)
        {
            logger.LogInformation(
                "Legacy scene depth={Depth} parent={ParentPath} start=0x{Start:X} actorCount={ActorCount} actorsStart=0x{ActorStart:X}",
                depth,
                string.Join("/", parentPath),
                offset,
                actorCount,
                reader.Offset);
        }

        List<LegacyCinematicActor> actors = [];
        for (int actorIndex = 0; actorIndex < actorCount && reader.Remaining >= 4; actorIndex++)
        {
            int actorOffset = reader.Offset;
            uint actorType = reader.PeekUInt32();
            if (!LegacyBinaryTypeRegistry.TryResolve(typeof(LegacyCinematicTypedPickableActorBinary), actorType, serializerContext, out Type? actorBinaryType))
                actorBinaryType = null;

            if (actorBinaryType == typeof(LegacyCinematicSceneActorBinary))
            {
                LegacyCinematicActor actor = LegacyCinematicSceneActorReader.ReadSceneActor(reader, parentPath, actorIndex, serializerContext, logger);
                actors.Add(actor);
                if (debugScene)
                {
                    logger.LogInformation(
                        "Legacy scene actor depth={Depth} index={Index} offset=0x{Offset:X} end=0x{End:X} path={ActorPath} texture={Texture}",
                        depth,
                        actorIndex,
                        actorOffset,
                        reader.Offset,
                        string.Join("/", actor.Path),
                        actor.TexturePath ?? actor.TemplatePath);
                }

                continue;
            }

            if (actorBinaryType == typeof(LegacyCinematicSubSceneActorBinary))
            {
                LegacyCinematicSceneActorReader.ReadSubSceneActor(fileSystem, reader, parentPath, actorIndex, depth, serializerContext, actors, logger);
                continue;
            }

            if (actorIndex > 0)
            {
                if (debugScene)
                {
                    logger.LogInformation(
                        "Legacy scene actor loop stopped at non-actor tail depth={Depth} parent={ParentPath} index={Index} offset=0x{Offset:X} type=0x{ActorType:X8}",
                        depth,
                        string.Join("/", parentPath),
                        actorIndex,
                        reader.Offset,
                        actorType);
                }

                break;
            }

            throw new InvalidDataException($"Unsupported legacy scene actor type 0x{actorType:X8} at 0x{reader.Offset:X}.");
        }

        if (depth > 0)
            LegacyCinematicSceneBoundaryReader.SnapEmbeddedSceneTailToNextActorBoundary(reader, depth, parentPath, logger, debugScene);

        if (debugScene)
        {
            logger.LogInformation(
                "Legacy scene end depth={Depth} parent={ParentPath} end=0x{End:X} actorsRead={ActorCount}",
                depth,
                string.Join("/", parentPath),
                reader.Offset,
                actors.Count);
        }

        return new SceneReadResult([.. actors], reader.Offset);
    }
}