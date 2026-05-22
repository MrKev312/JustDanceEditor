using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class LegacyCinematicSceneActorReader
{
    internal static LegacyCinematicActor ReadSceneActor(
        LegacyCinematicBinaryReader reader,
        IReadOnlyList<string> parentPath,
        int siblingOrder,
        LegacyBinarySerializerContext serializerContext,
        ILogger logger)
    {
        int actorOffset = reader.Offset;
        LegacyCinematicSceneActorBinary actorHeader = LegacyBinarySerializer.Deserialize<LegacyCinematicSceneActorBinary>(reader, serializerContext);
        uint typeId = LegacyBinarySerializer.GetTypeId(actorHeader.GetType());
        LegacyCinematicPickableFields pickable = actorHeader.Pickable.ToRuntime();
        LegacyCinematicActorTrailer trailer = LegacyCinematicSceneActorReader.ReadActorTrailer(reader);

        string? texturePath = null;
        List<string> texturePaths = [];
        string? materialPath = null;
        string? meshPath = null;
        uint? visualComponentTypeId = null;
        int atlasIndex = 0;
        TextureAnchor anchor = TextureAnchor.MiddleCenter;
        float customAnchorX = 0.0f;
        float customAnchorY = 0.0f;
        RgbTint baseTint = RgbTint.White;
        float baseAlpha = 1.0f;
        LegacyCinematicSinusParameters sinus = default;

        if (trailer.ComponentVersion != 0 && reader.Remaining >= 4)
        {
            uint componentType = reader.ReadUInt32();
            if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialGraphicComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
                int componentStart = reader.Offset;
                baseTint = LegacyCinematicSceneBoundaryReader.ReadComponentTint(reader.Bytes, componentStart);
                baseAlpha = LegacyCinematicSceneBoundaryReader.ReadComponentAlpha(reader.Bytes, componentStart);
                atlasIndex = LegacyCinematicSceneBoundaryReader.ReadInt32OrDefault(reader.Bytes, componentStart + LegacyCinematicConstants.MaterialGraphicAtlasIndexOffset);
                anchor = LegacyCinematicSceneBoundaryReader.ReadTextureAnchorOrDefault(reader.Bytes, componentStart + LegacyCinematicConstants.MaterialGraphicAnchorOffset);
                customAnchorX = LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(reader.Bytes, componentStart + LegacyCinematicConstants.MaterialGraphicCustomAnchorXOffset, 0);
                customAnchorY = LegacyCinematicSceneBoundaryReader.ReadSingleOrDefault(reader.Bytes, componentStart + LegacyCinematicConstants.MaterialGraphicCustomAnchorYOffset, 0);
                if (LegacyCinematicTemplateVisualResolver.TryFindMaterialPathRecord(
                    reader,
                    componentStart,
                    768,
                    out string? materialRecord,
                    out IReadOnlyList<string> materialTexturePaths,
                    out int materialTailOffset,
                    out int componentEndOffset))
                {
                    texturePaths.AddRange(materialTexturePaths);
                    texturePath = texturePaths.FirstOrDefault();
                    materialPath = LegacyCinematicNames.NormalizePath(materialRecord);
                    sinus = LegacyCinematicSceneBoundaryReader.ReadMaterialGraphicSinusParameters(reader.Bytes, materialTailOffset);
                    reader.Offset = componentEndOffset;
                }
                else
                {
                    LegacyCinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 2048);
                }
            }
            else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicMesh3DComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
                int componentStart = reader.Offset;
                baseTint = LegacyCinematicSceneBoundaryReader.ReadComponentTint(reader.Bytes, componentStart);
                baseAlpha = LegacyCinematicSceneBoundaryReader.ReadComponentAlpha(reader.Bytes, componentStart);
                atlasIndex = LegacyCinematicSceneBoundaryReader.ReadInt32OrDefault(reader.Bytes, componentStart + LegacyCinematicConstants.MaterialGraphicAtlasIndexOffset);
                anchor = LegacyCinematicSceneBoundaryReader.ReadTextureAnchorOrDefault(reader.Bytes, componentStart + LegacyCinematicConstants.MaterialGraphicAnchorOffset);
                if (LegacyCinematicTemplateVisualResolver.TryFindMesh3DPathRecords(
                    reader,
                    componentStart,
                    4096,
                    out IReadOnlyList<string> meshTexturePaths,
                    out string? meshMaterialPath,
                    out string? meshRecordPath,
                    out int componentEndOffset))
                {
                    texturePaths.AddRange(meshTexturePaths);
                    texturePath = texturePaths.FirstOrDefault();
                    materialPath = meshMaterialPath;
                    meshPath = meshRecordPath;
                    reader.Offset = componentEndOffset;
                }
                else
                {
                    LegacyCinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 4096);
                }
            }
            else if (LegacyBinarySerializer.IsTypeId<LegacyCinematicPleoTextureGraphicComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
            }
            else
            {
                logger.LogDebug(
                    "Skipping non-visual legacy cinematic component 0x{ComponentType:X8} on actor {ActorName} at 0x{Offset:X}.",
                    componentType,
                    pickable.Name,
                    reader.Offset - 4);
            }

            LegacyCinematicSceneBoundaryReader.SkipActorBoundaryZeroTail(reader);
            LegacyCinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 2048);
        }

        string[] actorPath = [.. parentPath, pickable.Name];
        return new LegacyCinematicActor(
            actorPath,
            pickable.Name,
            actorOffset,
            siblingOrder,
            typeId,
            pickable.RelativeZ,
            pickable.ScaleX,
            pickable.ScaleY,
            pickable.XFlipped,
            pickable.Angle,
            pickable.PositionX,
            pickable.PositionY,
            LegacyCinematicNames.NormalizePath(pickable.TemplatePath),
            null,
            texturePath,
            [.. texturePaths],
            materialPath,
            meshPath,
            visualComponentTypeId,
            atlasIndex,
            anchor,
            customAnchorX,
            customAnchorY,
            ScenePriority: 0,
            baseTint,
            baseAlpha,
            trailer.ParentBind,
            sinus);
    }

    internal static void ReadSubSceneActor(
        JustDanceUbiArtFileSystem fileSystem,
        LegacyCinematicBinaryReader reader,
        IReadOnlyList<string> parentPath,
        int siblingOrder,
        int depth,
        LegacyBinarySerializerContext serializerContext,
        List<LegacyCinematicActor> actors,
        ILogger logger)
    {
        int actorOffset = reader.Offset;
        LegacyCinematicSubSceneActorBinary actorHeader = LegacyBinarySerializer.Deserialize<LegacyCinematicSubSceneActorBinary>(reader, serializerContext);
        uint typeId = LegacyBinarySerializer.GetTypeId(actorHeader.GetType());
        LegacyCinematicPickableFields pickable = actorHeader.Pickable.ToRuntime();
        LegacyCinematicActorTrailer trailer = LegacyCinematicSceneActorReader.ReadActorTrailer(reader);
        LegacyCinematicSubSceneActorTail subSceneTail = LegacyCinematicSceneActorReader.ReadSubSceneActorTail(reader, logger, actorOffset, pickable.Name);

        string[] actorPath = [.. parentPath, pickable.Name];
        actors.Add(new LegacyCinematicActor(
            actorPath,
            pickable.Name,
            actorOffset,
            siblingOrder,
            typeId,
            pickable.RelativeZ,
            pickable.ScaleX,
            pickable.ScaleY,
            pickable.XFlipped,
            pickable.Angle,
            pickable.PositionX,
            pickable.PositionY,
            LegacyCinematicNames.NormalizePath(pickable.TemplatePath),
            subSceneTail.Path,
            null,
            [],
            null,
            null,
            null,
            0,
            TextureAnchor.MiddleCenter,
            0.0f,
            0.0f,
            ScenePriority: 0,
            RgbTint.White,
            1.0f,
            trailer.ParentBind));

        bool debugScene = false;
        bool nextLooksLikeScene = reader.Remaining >= 24 && reader.PeekInt32() == 1;
        if (debugScene)
        {
            logger.LogInformation(
                "Legacy subscene actor depth={Depth} index={Index} offset=0x{Offset:X} tailEnd=0x{TailEnd:X} path={ActorPath} relativePath={SubScenePath} embedSceneFlag={EmbedSceneFlag} singlePiece={SinglePiece} zForced={ZForced} directPicking={DirectPicking} ignoreSave={IgnoreSave} viewType={ViewType} nextLooksLikeScene={NextLooksLikeScene}",
                depth,
                siblingOrder,
                actorOffset,
                reader.Offset,
                string.Join("/", actorPath),
                subSceneTail.Path ?? string.Empty,
                subSceneTail.EmbedScene,
                subSceneTail.IsSinglePiece,
                subSceneTail.ZForced,
                subSceneTail.DirectPicking,
                subSceneTail.IgnoreSave,
                subSceneTail.ViewType,
                nextLooksLikeScene);
        }

        // JD2014 cooked scenes can embed the child scene after this tail even
        // when the later-source EMBED_SCENE field position reads as false.
        // Treat the actual scene version marker as the authoritative legacy
        // cooked-data signal while still logging the parsed flag above.
        if ((subSceneTail.EmbedScene || nextLooksLikeScene) && nextLooksLikeScene)
        {
            SceneReadResult childScene = LegacyCinematicSceneReader.ReadScene(fileSystem, reader.Bytes, reader.Offset, actorPath, depth + 1, logger);
            actors.AddRange(childScene.Actors);
            reader.Offset = childScene.EndOffset;
            return;
        }

        if (!string.IsNullOrWhiteSpace(subSceneTail.Path) &&
            depth < 12 &&
            fileSystem.GetFilePath(subSceneTail.Path, out CookedFile? subSceneFile))
        {
            try
            {
                byte[] childBytes = LegacyCinematicSceneBoundaryReader.ReadFileBytes(fileSystem, subSceneFile);
                SceneReadResult childScene = LegacyCinematicSceneReader.ReadScene(fileSystem, childBytes, 0, actorPath, depth + 1, logger);
                actors.AddRange(childScene.Actors);
                logger.LogDebug(
                    "Loaded external legacy subscene {SubScenePath} for {ActorPath}.",
                    subSceneTail.Path,
                    string.Join("/", actorPath));
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(
                    ex,
                    "Could not read external legacy subscene {SubScenePath} for {ActorPath}.",
                    subSceneTail.Path,
                    string.Join("/", actorPath));
            }
        }

        logger.LogDebug(
            "Legacy subscene actor {ActorPath} at 0x{Offset:X} did not embed child scene data.",
            string.Join("/", actorPath),
            actorOffset);
    }

    internal static LegacyCinematicSubSceneActorTail ReadSubSceneActorTail(
        LegacyCinematicBinaryReader reader,
        ILogger logger,
        int actorOffset,
        string actorName)
    {
        int tailStart = reader.Offset;

        try
        {
            string subScenePath = LegacyCinematicNames.NormalizePath(reader.ReadPath());
            bool embedScene = reader.ReadInt32() != 0;
            bool isSinglePiece = reader.ReadInt32() != 0;
            bool zForced = reader.ReadInt32() != 0;
            bool directPicking = reader.ReadInt32() != 0;
            bool ignoreSave = reader.ReadInt32() != 0;
            int viewType = reader.ReadInt32();

            return new LegacyCinematicSubSceneActorTail(
                string.IsNullOrWhiteSpace(subScenePath) ? null : subScenePath,
                embedScene,
                isSinglePiece,
                zForced,
                directPicking,
                ignoreSave,
                viewType);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            reader.Offset = tailStart;
            reader.Skip(36);
            logger.LogDebug(
                ex,
                "Fell back to legacy subscene tail skip for {ActorName} at 0x{Offset:X}.",
                actorName,
                actorOffset);
            return new LegacyCinematicSubSceneActorTail(null, false, false, false, false, false, 0);
        }
    }

    internal sealed record LegacyCinematicSubSceneActorTail(
        string? Path,
        bool EmbedScene,
        bool IsSinglePiece,
        bool ZForced,
        bool DirectPicking,
        bool IgnoreSave,
        int ViewType);

    internal static LegacyCinematicActorTrailer ReadActorTrailer(LegacyCinematicBinaryReader reader)
    {
        int trailerType = reader.ReadInt32();
        int trailerSubType = reader.ReadInt32();
        if (trailerType != 0)
            throw new InvalidDataException($"Unsupported legacy actor trailer type {trailerType} at 0x{reader.Offset - 8:X}.");

        if (trailerSubType == 0)
            return new LegacyCinematicActorTrailer(reader.ReadInt32(), null);

        if (trailerSubType != 1)
            throw new InvalidDataException($"Unsupported legacy actor trailer subtype {trailerSubType} at 0x{reader.Offset - 4:X}.");

        int targetVersion = reader.ReadInt32();
        string parentActor;
        if (targetVersion == 0)
        {
            parentActor = reader.ReadString();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
        }
        else if (targetVersion == 1)
        {
            parentActor = LegacyCinematicSceneActorReader.ReadObjectPathTerminalName(reader);
        }
        else if (targetVersion == 2)
        {
            parentActor = LegacyCinematicSceneActorReader.ReadSerializedObjectPathTerminalName(reader, hasSerializedObjectReference: true);
        }
        else
        {
            throw new InvalidDataException($"Unsupported legacy actor reference target version {targetVersion} at 0x{reader.Offset - 4:X}.");
        }

        LegacyCinematicActorBind parentBind = new(
            parentActor,
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadUInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32());

        return new LegacyCinematicActorTrailer(reader.ReadInt32(), parentBind);
    }

    internal static string ReadObjectPathTerminalName(LegacyCinematicBinaryReader reader)
    {
        _ = reader.ReadInt32();
        int levelCount = reader.ReadInt32();
        if (levelCount is < 0 or > 64)
            throw new InvalidDataException($"Invalid legacy object path level count {levelCount} at 0x{reader.Offset - 4:X}.");

        string terminalName = string.Empty;
        for (int i = 0; i < levelCount; i++)
        {
            string levelName = reader.ReadString();
            int parent = reader.ReadInt32();
            if (parent == 0 && !string.IsNullOrWhiteSpace(levelName))
                terminalName = levelName;
        }

        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        return terminalName;
    }

    internal static string ReadSerializedObjectPathTerminalName(
        LegacyCinematicBinaryReader reader,
        bool hasSerializedObjectReference)
    {
        _ = reader.ReadInt32();
        int levelCount = reader.ReadInt32();
        if (levelCount is < 0 or > 64)
            throw new InvalidDataException($"Invalid legacy object path level count {levelCount} at 0x{reader.Offset - 4:X}.");

        string terminalLevelName = string.Empty;
        for (int i = 0; i < levelCount; i++)
        {
            string levelName = reader.ReadString();
            int parent = reader.ReadInt32();
            if (parent == 0 && !string.IsNullOrWhiteSpace(levelName))
                terminalLevelName = levelName;
        }

        string objectName = reader.ReadString();
        _ = reader.ReadInt32();
        if (hasSerializedObjectReference)
        {
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
        }

        return string.IsNullOrWhiteSpace(objectName) ? terminalLevelName : objectName;
    }
}