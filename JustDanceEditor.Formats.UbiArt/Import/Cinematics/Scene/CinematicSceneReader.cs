using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicSceneReader
{
    private readonly record struct SceneHeader(
        int ActorCount,
        int ActorsStartOffset,
        bool HasTypedActorList,
        int? UnsupportedPickableListStartOffset);

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

    private static bool HasRootVideoOutputActor(
        IReadOnlyList<CinematicActor> actors,
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        CinematicScene resolvedScene = CinematicTemplateVisualResolver.ResolveTemplateVisuals(
            new CinematicScene(actors),
            fileSystem,
            logger);
        uint pleoTextureComponentId = LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>();

        return resolvedScene.Actors.Any(actor =>
            actor.VisualComponentTypeId == pleoTextureComponentId &&
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
            TypeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
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
        ILogger logger)
    {
        if (spawnClips.Count == 0)
            return scene;

        List<CinematicActor> actors = new(scene.Actors);
        HashSet<string> actorKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (CinematicActor actor in actors)
            actorKeys.Add(actor.Key);
        int addedCount = 0;
        foreach (CinematicSpawnActorRuntimeClip spawnClip in spawnClips.OrderBy(clip => clip.Order))
        {
            if (!TryCreateSpawnedActor(fileSystem, spawnClip, actors.Count, logger, out CinematicActor? actor) ||
                actor is null)
            {
                continue;
            }

            if (!actorKeys.Add(actor.Key))
                continue;

            actors.Add(actor);
            addedCount++;
        }

        if (addedCount == 0)
            return scene;

        logger.LogInformation(
            "Materialized {Count} cinematic SpawnActor actor(s) from tape data.",
            addedCount);
        return CinematicTemplateVisualResolver.ResolveTemplateVisuals(new CinematicScene([.. actors]), fileSystem, logger);
    }

    private static bool TryCreateSpawnedActor(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicSpawnActorRuntimeClip spawnClip,
        int siblingOrder,
        ILogger logger,
        out CinematicActor? actor)
    {
        actor = null;
        CinematicSpawnActorClip spawn = spawnClip.SpawnActor;
        string actorPath = CinematicNames.NormalizePath(spawn.ActorPath);
        string actorName = !string.IsNullOrWhiteSpace(spawn.ActorName)
            ? spawn.ActorName
            : Path.GetFileNameWithoutExtension(actorPath);
        if (string.IsNullOrWhiteSpace(actorName) || string.IsNullOrWhiteSpace(actorPath))
            return false;

        CinematicPickableFields pickable;
        if (Path.GetExtension(actorPath).Equals(".tpl", StringComparison.OrdinalIgnoreCase))
        {
            pickable = new CinematicPickableFields(
                RelativeZ: 0.0f,
                ScaleX: 1.0f,
                ScaleY: 1.0f,
                XFlipped: 0,
                Name: actorName,
                DefaultEnabled: false,
                PositionX: 0.0f,
                PositionY: 0.0f,
                Angle: 0.0f,
                TemplatePath: actorPath);
        }
        else
        {
            if (!fileSystem.GetFilePath(actorPath, out CookedFile? actorFile))
            {
                logger.LogDebug(
                    "Cinematic SpawnActor '{ActorName}' references missing actor file '{ActorPath}'.",
                    actorName,
                    actorPath);
                return false;
            }

            byte[] bytes = CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, actorFile);
            CinematicBinaryReader reader = new(bytes);
            int version = reader.ReadInt32();
            if (version != 1)
            {
                logger.LogDebug(
                    "Cinematic SpawnActor '{ActorName}' references unsupported actor file version {Version} in '{ActorPath}'.",
                    actorName,
                    version,
                    actorPath);
                return false;
            }

            LegacyBinarySerializerContext serializerContext = new((int)fileSystem.VersionProfile.EngineVersion);
            CinematicPickableBinary pickableBinary = LegacyBinarySerializer.Deserialize<CinematicPickableBinary>(reader, serializerContext);
            pickable = pickableBinary.ToRuntime() with
            {
                Name = actorName,
                DefaultEnabled = false
            };
        }

        CinematicActorBind? parentBind = CreateSpawnActorParentBind(spawn, pickable);
        float positionX = parentBind == null ? spawn.PositionX + pickable.PositionX : 0.0f;
        float positionY = parentBind == null ? spawn.PositionY + pickable.PositionY : 0.0f;
        float relativeZ = parentBind == null
            ? spawn.PositionZ + pickable.RelativeZ
            : pickable.RelativeZ;
        actor = new CinematicActor(
            [actorName],
            actorName,
            SourceOffset: -100000 + spawnClip.Order,
            SiblingOrder: siblingOrder,
            TypeId: LegacyBinarySerializer.GetTypeId<CinematicSceneActorBinary>(),
            RelativeZ: relativeZ,
            ScaleX: pickable.ScaleX,
            ScaleY: pickable.ScaleY,
            XFlipped: pickable.XFlipped,
            Angle: pickable.Angle,
            PositionX: positionX,
            PositionY: positionY,
            TemplatePath: CinematicNames.NormalizePath(pickable.TemplatePath),
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
            DefaultEnabled: false,
            BaseTint: RgbTint.White,
            BaseAlpha: 0.0f,
            ParentBind: parentBind);
        return true;
    }

    private static CinematicActorBind? CreateSpawnActorParentBind(
        CinematicSpawnActorClip spawn,
        CinematicPickableFields pickable)
    {
        string? parentActorName = spawn.ParentTarget?.Segments.LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment));
        if (string.IsNullOrWhiteSpace(parentActorName))
            return null;

        return new CinematicActorBind(
            parentActorName,
            spawn.PositionX + pickable.PositionX,
            spawn.PositionY + pickable.PositionY,
            spawn.PositionZ + pickable.RelativeZ,
            pickable.Angle,
            pickable.ScaleX,
            pickable.ScaleY,
            UseParentFlip: 1,
            ScaleInheritProp: CinematicConstants.BindScaleInheritCombine,
            UseParentAlpha: 1,
            UseParentColor: 1);
    }

    internal static bool TryReadSceneFile(
        JustDanceUbiArtFileSystem fileSystem,
        string relativePath,
        IReadOnlyList<string> parentPath,
        List<CinematicActor> actors,
        ILogger logger)
    {
        if (!fileSystem.GetFilePath(relativePath, out CookedFile? file))
            return false;

        try
        {
            byte[] bytes = CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, file);
            if (CinematicXmlSceneReader.IsXmlScene(bytes))
            {
                actors.AddRange(CinematicXmlSceneReader.ReadScene(fileSystem, bytes, parentPath, logger));
                return true;
            }

            SceneReadResult result = CinematicSceneReader.ReadScene(fileSystem, bytes, 0, parentPath, 0, logger);
            actors.AddRange(result.Actors);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException($"Could not read cinematic scene '{relativePath}'.", ex);
        }
    }

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
        ILogger logger)
    {
        CinematicBinaryReader reader = new(bytes, offset);
        LegacyBinarySerializerContext serializerContext = new((int)fileSystem.VersionProfile.EngineVersion);
        int version = reader.ReadInt32();
        if (version != 1)
            throw new InvalidDataException($"Unsupported legacy scene version {version} at 0x{offset:X}.");

        reader.SkipUInt32();
        SceneHeader header = CinematicSceneReader.ReadSceneHeader(bytes, offset);
        int actorCount = header.ActorCount;
        if (actorCount is < 0 or > 512)
            throw new InvalidDataException($"Invalid legacy scene actor count {actorCount} at 0x{offset:X}.");

        bool debugScene = false;
        List<CinematicActor> actors = [];
        if (header.UnsupportedPickableListStartOffset is int pickableListStartOffset)
        {
            int actorListOffset = CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(bytes, pickableListStartOffset) ??
                bytes.Length;
            bool hasDeclaredActorCount = TryReadActorCountBeforeList(bytes, actorListOffset, out int declaredActorCount);
            int friezePayloadEndOffset = hasDeclaredActorCount
                ? Math.Max(pickableListStartOffset, actorListOffset - sizeof(int))
                : actorListOffset;
            reader.Offset = actorListOffset;
            IReadOnlyList<CinematicActor> friezes = CinematicFriezeReader.ReadFriezes(
                fileSystem,
                bytes,
                pickableListStartOffset,
                friezePayloadEndOffset,
                parentPath,
                firstSiblingOrder: 0,
                serializerContext,
                logger);
            actors.AddRange(friezes);

            if (debugScene)
            {
                logger.LogInformation(
                    "Legacy scene frieze pickable-list read depth={Depth} parent={ParentPath} start=0x{Start:X} pickablesStart=0x{PickablesStart:X} nextActor=0x{NextActor:X} friezes={FriezeCount}",
                    depth,
                    string.Join("/", parentPath),
                    offset,
                    pickableListStartOffset,
                    reader.Offset,
                    friezes.Count);
            }

            int embeddedActorIndex = actors.Count;
            if (hasDeclaredActorCount)
            {
                for (int i = 0; i < declaredActorCount; i++)
                {
                    if (!TryReadSceneActorAtCurrentOffset(embeddedActorIndex))
                    {
                        throw new InvalidDataException(
                            $"Could not read legacy scene actor {i + 1}/{declaredActorCount} from embedded list at 0x{reader.Offset:X}.");
                    }

                    embeddedActorIndex++;
                }
            }
            else
            {
                while (embeddedActorIndex < 2048 &&
                    reader.Remaining >= 4 &&
                    CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
                {
                    uint actorType = reader.PeekUInt32();
                    if (!LegacyBinaryTypeRegistry.TryResolve(typeof(CinematicTypedPickableActorBinary), actorType, serializerContext, out Type? actorBinaryType) ||
                        actorBinaryType != typeof(CinematicSceneActorBinary))
                    {
                        break;
                    }

                    if (!TryReadSceneActorAtCurrentOffset(embeddedActorIndex))
                        break;

                    embeddedActorIndex++;
                }
            }

            if (debugScene)
            {
                logger.LogInformation(
                    "Legacy frieze-list scene end depth={Depth} parent={ParentPath} end=0x{End:X} actorsRead={ActorCount}",
                    depth,
                    string.Join("/", parentPath),
                    reader.Offset,
                    actors.Count);
            }

            return new SceneReadResult([.. actors], reader.Offset);
        }

        reader.Offset = header.ActorsStartOffset;
        if (actorCount == 0 && depth > 0)
        {
            int payloadEndOffset = CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(bytes, reader.Offset) ??
                reader.Offset;
            if (payloadEndOffset > reader.Offset)
            {
                actors.AddRange(CinematicFriezeReader.ReadFriezes(
                    fileSystem,
                    bytes,
                    reader.Offset,
                    payloadEndOffset,
                    parentPath,
                    firstSiblingOrder: 0,
                    serializerContext,
                    logger));
                reader.Offset = payloadEndOffset;
                return new SceneReadResult([.. actors], reader.Offset);
            }
        }

        if (actorCount > 0 && header.HasTypedActorList)
        {
            if (!CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset) &&
                CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(reader.Bytes, reader.Offset) is int firstActorBoundaryOffset)
            {
                actors.AddRange(CinematicFriezeReader.ReadFriezes(
                    fileSystem,
                    reader.Bytes,
                    reader.Offset,
                    firstActorBoundaryOffset,
                    parentPath,
                    firstSiblingOrder: 0,
                    serializerContext,
                    logger));
            }

            CinematicSceneBoundaryReader.SnapSceneActorListStartToNextBoundary(reader, depth, parentPath, logger, debugScene);
        }

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

        int actorIndex = 0;
        for (; actorIndex < actorCount && reader.Remaining >= 4; actorIndex++)
        {
            if (!TryReadSceneActorAtCurrentOffset(actorIndex))
                break;
        }

        if (depth == 0)
        {
            while (actorIndex < 2048 &&
                reader.Remaining >= 4 &&
                CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, reader.Offset))
            {
                if (!TryReadSceneActorAtCurrentOffset(actorIndex))
                    break;

                actorIndex++;
            }
        }

        if (depth > 0)
            CinematicSceneBoundaryReader.SnapEmbeddedSceneTailToNextActorBoundary(reader, depth, parentPath, logger, debugScene);

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

        bool TryReadSceneActorAtCurrentOffset(int currentActorIndex)
        {
            int actorOffset = reader.Offset;
            uint actorType = reader.PeekUInt32();
            if (!LegacyBinaryTypeRegistry.TryResolve(typeof(CinematicTypedPickableActorBinary), actorType, serializerContext, out Type? actorBinaryType))
                actorBinaryType = null;

            if (actorBinaryType == typeof(CinematicSceneActorBinary))
            {
                CinematicActor actor = CinematicSceneActorReader.ReadSceneActor(reader, parentPath, currentActorIndex, serializerContext, logger);
                actors.Add(actor);
                if (debugScene)
                {
                    logger.LogInformation(
                        "Legacy scene actor depth={Depth} index={Index} offset=0x{Offset:X} end=0x{End:X} path={ActorPath} texture={Texture}",
                        depth,
                        currentActorIndex,
                        actorOffset,
                        reader.Offset,
                        string.Join("/", actor.Path),
                        actor.TexturePath ?? actor.TemplatePath);
                }

                return true;
            }

            if (actorBinaryType == typeof(CinematicSubSceneActorBinary))
            {
                CinematicSceneActorReader.ReadSubSceneActor(fileSystem, reader, parentPath, currentActorIndex, depth, serializerContext, actors, logger);
                return true;
            }

            if (currentActorIndex > 0)
            {
                if (debugScene)
                {
                    logger.LogInformation(
                        "Legacy scene actor loop stopped at non-actor tail depth={Depth} parent={ParentPath} index={Index} offset=0x{Offset:X} type=0x{ActorType:X8}",
                        depth,
                        string.Join("/", parentPath),
                        currentActorIndex,
                        reader.Offset,
                        actorType);
                }

                return false;
            }

            throw new InvalidDataException($"Unsupported legacy scene actor type 0x{actorType:X8} at 0x{reader.Offset:X}.");
        }
    }

    private static SceneHeader ReadSceneHeader(byte[] bytes, int sceneStartOffset)
    {
        int shortHeaderCount = ReadInt32OrDefault(bytes, sceneStartOffset + 12, -1);
        int shortHeaderPayloadOffset = sceneStartOffset + 16;
        if (IsValidSceneCount(shortHeaderCount) &&
            shortHeaderCount > 0 &&
            shortHeaderPayloadOffset + 20 <= bytes.Length &&
            !CinematicSceneBoundaryReader.IsActorBoundaryAt(bytes, shortHeaderPayloadOffset) &&
            LooksLikeUntypedPickableList(bytes, shortHeaderPayloadOffset))
        {
            // Scene serializes frises/metafries before the typed ACTORS list.
            // Treating them as a typed actor-list prefix makes the next sibling
            // subscene become a child of this holder, so return a bounded payload
            // range for the frieze reader and resume at the next real actor boundary.
            return new SceneHeader(0, shortHeaderPayloadOffset, HasTypedActorList: false, shortHeaderPayloadOffset);
        }

        int longHeaderCount = ReadInt32OrDefault(bytes, sceneStartOffset + 20, -1);
        int longHeaderActorOffset = sceneStartOffset + 24;
        if (IsValidSceneCount(longHeaderCount))
            return new SceneHeader(longHeaderCount, longHeaderActorOffset, HasTypedActorList: true, null);

        if (IsValidSceneCount(shortHeaderCount))
            return new SceneHeader(shortHeaderCount, shortHeaderPayloadOffset, HasTypedActorList: true, null);

        throw new InvalidDataException($"Could not read legacy scene actor count at 0x{sceneStartOffset:X}.");
    }

    private static bool IsValidSceneCount(int count) => count is >= 0 and <= 512;

    private static int ReadInt32OrDefault(byte[] bytes, int offset, int defaultValue) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))
            : defaultValue;

    private static bool TryReadActorCountBeforeList(byte[] bytes, int actorListOffset, out int actorCount)
    {
        actorCount = ReadInt32OrDefault(bytes, actorListOffset - sizeof(int), -1);
        return IsValidSceneCount(actorCount);
    }

    private static bool LooksLikeUntypedPickableList(byte[] bytes, int offset)
    {
        float scaleX = ReadSingleOrDefault(bytes, offset + 4);
        float scaleY = ReadSingleOrDefault(bytes, offset + 8);
        int xFlipped = ReadInt32OrDefault(bytes, offset + 12, -1);
        int nameLength = ReadInt32OrDefault(bytes, offset + 16, -1);

        return float.IsFinite(scaleX) &&
            float.IsFinite(scaleY) &&
            Math.Abs(scaleX) < 1024.0f &&
            Math.Abs(scaleY) < 1024.0f &&
            xFlipped is 0 or 1 &&
            nameLength is > 0 and <= 256 &&
            offset + 20 + nameLength <= bytes.Length &&
            IsMostlyText(bytes, offset + 20, nameLength);
    }

    private static float ReadSingleOrDefault(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return float.NaN;

        uint raw = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    private static bool IsMostlyText(byte[] bytes, int offset, int length)
    {
        for (int i = 0; i < length; i++)
        {
            byte value = bytes[offset + i];
            if (value == 0)
                continue;
            if (value < 0x20 || value > 0x7E)
                return false;
        }

        return true;
    }
}
