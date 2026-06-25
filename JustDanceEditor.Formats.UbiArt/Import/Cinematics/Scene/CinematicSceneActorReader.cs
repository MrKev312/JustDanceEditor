using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Particles;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicSceneActorReader
{
    internal static CinematicActor ReadSceneActor(
        CinematicBinaryReader reader,
        IReadOnlyList<string> parentPath,
        int siblingOrder,
        LegacyBinarySerializerContext serializerContext,
        ILogger logger)
    {
        int actorOffset = reader.Offset;
        CinematicSceneActorBinary actorHeader = LegacyBinarySerializer.Deserialize<CinematicSceneActorBinary>(reader, serializerContext);
        uint typeId = LegacyBinarySerializer.GetTypeId(actorHeader.GetType());
        CinematicPickableFields pickable = actorHeader.Pickable.ToRuntime();
        CinematicActorTrailer trailer = CinematicSceneActorReader.ReadActorTrailer(reader, serializerContext);

        string? texturePath = null;
        List<string> texturePaths = [];
        string? materialPath = null;
        string? explicitAtlasPath = null;
        string? meshPath = null;
        uint? visualComponentTypeId = null;
        int atlasIndex = 0;
        int atlasTextureSlot = 0;
        TextureAnchor anchor = TextureAnchor.MiddleCenter;
        float customAnchorX = 0.0f;
        float customAnchorY = 0.0f;
        RgbTint baseTint = RgbTint.White;
        float baseAlpha = 1.0f;
        CinematicSinusParameters sinus = default;
        float meshInitialScaleZ = 0.0f;
        CinematicMeshOrientation meshOrientation = default;
        bool meshForce2DRender = false;

        if (trailer.ComponentVersion != 0 && reader.Remaining >= 4)
        {
            uint componentType = reader.ReadUInt32();
            if (LegacyBinarySerializer.IsTypeId<CinematicMaterialGraphicComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
                int componentStart = reader.Offset;
                baseTint = CinematicSceneBoundaryReader.ReadComponentTint(reader.Bytes, componentStart);
                baseAlpha = CinematicSceneBoundaryReader.ReadComponentAlpha(reader.Bytes, componentStart);
                CinematicMaterialGraphicFields materialFields = CinematicSceneBoundaryReader.ReadMaterialGraphicFields(
                    reader.Bytes,
                    componentStart,
                    serializerContext);
                atlasIndex = materialFields.AtlasIndex;
                anchor = materialFields.Anchor;
                customAnchorX = materialFields.CustomAnchorX;
                customAnchorY = materialFields.CustomAnchorY;
                if (CinematicTemplateVisualResolver.TryFindMaterialPathRecord(
                    reader,
                    componentStart,
                    768,
                    out string? materialRecord,
                    out IReadOnlyList<string> materialTexturePaths,
                    out string? materialAtlasPath,
                    out int materialAtlasTextureSlot,
                    out int materialTailOffset,
                    out int componentEndOffset))
                {
                    atlasTextureSlot = materialAtlasTextureSlot;
                    materialPath = string.IsNullOrWhiteSpace(materialRecord)
                        ? null
                        : CinematicNames.NormalizePath(materialRecord);
                    texturePaths.AddRange(CinematicTemplateVisualResolver.NormalizeTextureOrderForMaterial(
                        materialTexturePaths));
                    texturePath = texturePaths.FirstOrDefault();
                    explicitAtlasPath = string.IsNullOrWhiteSpace(materialAtlasPath)
                        ? null
                        : CinematicNames.NormalizePath(materialAtlasPath);
                    sinus = materialTailOffset > 0
                        ? CinematicSceneBoundaryReader.ReadMaterialGraphicSinusParameters(reader.Bytes, materialTailOffset)
                        : default;
                    reader.Offset = componentEndOffset;
                }
                else
                {
                    CinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 2048);
                }
            }
            else if (LegacyBinarySerializer.IsTypeId<CinematicMesh3DComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
                int componentStart = reader.Offset;
                baseTint = CinematicSceneBoundaryReader.ReadComponentTint(reader.Bytes, componentStart);
                baseAlpha = CinematicSceneBoundaryReader.ReadComponentAlpha(reader.Bytes, componentStart);
                meshInitialScaleZ = CinematicSceneBoundaryReader.ReadMesh3DScaleZOrDefault(
                    reader.Bytes,
                    componentStart,
                    serializerContext);
                atlasIndex = CinematicSceneBoundaryReader.ReadMaterialGraphicAtlasIndexOrDefault(reader.Bytes, componentStart + CinematicConstants.MaterialGraphicAtlasIndexOffset);
                anchor = CinematicSceneBoundaryReader.ReadTextureAnchorOrDefault(reader.Bytes, componentStart + CinematicConstants.MaterialGraphicAnchorOffset);
                if (CinematicTemplateVisualResolver.TryFindMesh3DPathRecords(
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
                    CinematicMesh3DComponentFields meshFields = CinematicSceneBoundaryReader.ReadMesh3DComponentFieldsOrDefault(
                        reader.Bytes,
                        componentStart,
                        componentEndOffset,
                        4096);
                    meshOrientation = meshFields.Orientation;
                    meshForce2DRender = meshFields.Force2DRender;
                    reader.Offset = componentEndOffset;
                }
                else
                {
                    CinematicMesh3DComponentFields meshFields = CinematicSceneBoundaryReader.ReadMesh3DComponentFieldsOrDefault(
                        reader.Bytes,
                        componentStart,
                        componentStart,
                        4096);
                    meshOrientation = meshFields.Orientation;
                    meshForce2DRender = meshFields.Force2DRender;
                    CinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 4096);
                }
            }
            else if (LegacyBinarySerializer.IsTypeId<CinematicPleoTextureGraphicComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
            }
            else if (LegacyBinarySerializer.IsTypeId<CinematicAnimLightComponentBinary>(componentType) ||
                LegacyBinarySerializer.IsTypeId<CinematicAnimatedComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
                int componentStart = reader.Offset;
                baseTint = CinematicSceneBoundaryReader.ReadComponentTint(reader.Bytes, componentStart);
                baseAlpha = CinematicSceneBoundaryReader.ReadComponentAlpha(reader.Bytes, componentStart);
                CinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 4096);
            }
            else if (LegacyBinarySerializer.IsTypeId<CinematicClearColorComponentBinary>(componentType))
            {
                visualComponentTypeId = componentType;
                int componentStart = reader.Offset;
                baseTint = CinematicSceneBoundaryReader.ReadComponentTint(reader.Bytes, componentStart);
                baseAlpha = CinematicSceneBoundaryReader.ReadComponentAlpha(reader.Bytes, componentStart);
                reader.Offset = Math.Min(reader.Bytes.Length, componentStart + 16);
            }
            else
            {
                logger.LogDebug(
                    "Skipping non-visual cinematic component 0x{ComponentType:X8} on actor {ActorName} at 0x{Offset:X}.",
                    componentType,
                    pickable.Name,
                    reader.Offset - 4);
            }

            CinematicSceneBoundaryReader.SkipActorBoundaryZeroTail(reader);
            CinematicSceneBoundaryReader.SnapToNearbyActorBoundary(reader, 2048);
        }

        int actorEndOffset = CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(reader.Bytes, actorOffset + 4) ?? reader.Offset;
        CinematicBezierBranchReader.TryReadFromActorBytes(
            reader.Bytes,
            actorOffset,
            actorEndOffset,
            out CinematicBezierBranch? bezierBranch);

        string[] actorPath = [.. parentPath, pickable.Name];
        return new CinematicActor(
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
            CinematicNames.NormalizePath(pickable.TemplatePath),
            null,
            texturePath,
            [.. texturePaths],
            materialPath,
            explicitAtlasPath,
            meshPath,
            visualComponentTypeId,
            atlasIndex,
            atlasTextureSlot,
            anchor,
            customAnchorX,
            customAnchorY,
            ScenePriority: 0,
            pickable.DefaultEnabled,
            baseTint,
            baseAlpha,
            trailer.ParentBind,
            sinus,
            MeshInitialScaleZ: meshInitialScaleZ,
            MeshOrientation: meshOrientation,
            MeshForce2DRender: meshForce2DRender,
            BezierBranch: bezierBranch);
    }

    internal static void ReadSubSceneActor(
        JustDanceUbiArtFileSystem fileSystem,
        CinematicBinaryReader reader,
        IReadOnlyList<string> parentPath,
        int siblingOrder,
        int depth,
        LegacyBinarySerializerContext serializerContext,
        List<CinematicActor> actors,
        ILogger logger)
    {
        int actorOffset = reader.Offset;
        CinematicSubSceneActorBinary actorHeader = LegacyBinarySerializer.Deserialize<CinematicSubSceneActorBinary>(reader, serializerContext);
        uint typeId = LegacyBinarySerializer.GetTypeId(actorHeader.GetType());
        CinematicPickableFields pickable = actorHeader.Pickable.ToRuntime();
        CinematicActorTrailer trailer = CinematicSceneActorReader.ReadActorTrailer(reader, serializerContext);
        CinematicSubSceneActorTail subSceneTail = CinematicSceneActorReader.ReadSubSceneActorTail(reader, logger, actorOffset, pickable.Name);

        string[] actorPath = [.. parentPath, pickable.Name];
        actors.Add(new CinematicActor(
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
            CinematicNames.NormalizePath(pickable.TemplatePath),
            subSceneTail.Path,
            null,
            [],
            null,
            null,
            null,
            null,
            0,
            0,
            TextureAnchor.MiddleCenter,
            0.0f,
            0.0f,
            ScenePriority: 0,
            pickable.DefaultEnabled,
            RgbTint.White,
            1.0f,
            trailer.ParentBind));

        bool debugScene = false;
        bool nextLooksLikeScene = reader.Remaining >= 24 && reader.PeekInt32() == 1;
        CookedFile? subSceneFile = null;
        bool hasExternalSubScene = !string.IsNullOrWhiteSpace(subSceneTail.Path) &&
            depth < 12 &&
            fileSystem.GetFilePath(subSceneTail.Path, out subSceneFile);
        if (debugScene)
        {
            logger.LogInformation(
                "Legacy subscene actor depth={Depth} index={Index} offset=0x{Offset:X} tailEnd=0x{TailEnd:X} path={ActorPath} relativePath={SubScenePath} embedSceneFlag={EmbedSceneFlag} singlePiece={SinglePiece} zForced={ZForced} directPicking={DirectPicking} ignoreSave={IgnoreSave} viewType={ViewType} nextLooksLikeScene={NextLooksLikeScene} hasExternalSubScene={HasExternalSubScene}",
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
                nextLooksLikeScene,
                hasExternalSubScene);
        }

        if (ShouldReadEmbeddedSubScene(subSceneTail.EmbedScene, nextLooksLikeScene, hasExternalSubScene))
        {
            SceneReadResult childScene = CinematicSceneReader.ReadScene(fileSystem, reader.Bytes, reader.Offset, actorPath, depth + 1, logger);
            actors.AddRange(childScene.Actors);
            reader.Offset = childScene.EndOffset;
            return;
        }

        if (hasExternalSubScene && subSceneFile != null)
        {
            try
            {
                byte[] childBytes = CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, subSceneFile);
                SceneReadResult childScene = CinematicSceneReader.ReadScene(fileSystem, childBytes, 0, actorPath, depth + 1, logger);
                actors.AddRange(childScene.Actors);
                if (nextLooksLikeScene)
                    CinematicSceneActorReader.SkipInlineExternalSubScenePayload(reader, childBytes.Length, depth, actorPath, logger);
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

    internal static bool ShouldReadEmbeddedSubScene(
        bool embedScene,
        bool nextLooksLikeScene,
        bool hasExternalSubScene) =>
        nextLooksLikeScene && (embedScene || !hasExternalSubScene);

    internal static void SkipInlineExternalSubScenePayload(
        CinematicBinaryReader reader,
        int externalSceneByteLength,
        int depth,
        IReadOnlyList<string> actorPath,
        ILogger logger)
    {
        if (externalSceneByteLength <= 0)
            return;

        int payloadStart = reader.Offset;
        int payloadEnd = payloadStart + externalSceneByteLength;
        if (payloadEnd > reader.Bytes.Length)
            return;

        if (!CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, payloadEnd))
        {
            logger.LogDebug(
                "Inline external legacy subscene payload for {ActorPath} was not skipped because 0x{PayloadEnd:X} is not an actor boundary.",
                string.Join("/", actorPath),
                payloadEnd);
            return;
        }

        reader.Offset = payloadEnd;
        logger.LogDebug(
            "Skipped inline external legacy subscene payload for {ActorPath} at depth {Depth}: 0x{Start:X}..0x{End:X}.",
            string.Join("/", actorPath),
            depth,
            payloadStart,
            payloadEnd);
    }

    internal static CinematicSubSceneActorTail ReadSubSceneActorTail(
        CinematicBinaryReader reader,
        ILogger logger,
        int actorOffset,
        string actorName)
    {
        int tailStart = reader.Offset;

        try
        {
            string subScenePath = CinematicNames.NormalizePath(reader.ReadPath());
            bool embedScene = reader.ReadInt32() != 0;
            bool isSinglePiece = reader.ReadInt32() != 0;
            bool zForced = reader.ReadInt32() != 0;
            bool directPicking = reader.ReadInt32() != 0;
            bool ignoreSave = reader.ReadInt32() != 0;
            int viewType = reader.ReadInt32();

            return new CinematicSubSceneActorTail(
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
            return new CinematicSubSceneActorTail(null, false, false, false, false, false, 0);
        }
    }

    internal sealed record CinematicSubSceneActorTail(
        string? Path,
        bool EmbedScene,
        bool IsSinglePiece,
        bool ZForced,
        bool DirectPicking,
        bool IgnoreSave,
        int ViewType);

    internal static CinematicActorTrailer ReadActorTrailer(
        CinematicBinaryReader reader,
        LegacyBinarySerializerContext serializerContext)
    {
        int trailerType = reader.ReadInt32();
        int trailerSubType = reader.ReadInt32();
        if (trailerType != 0)
            throw new InvalidDataException($"Unsupported legacy actor trailer type {trailerType} at 0x{reader.Offset - 8:X}.");

        if (trailerSubType == 0)
            return new CinematicActorTrailer(reader.ReadInt32(), null);

        if (trailerSubType != 1)
            throw new InvalidDataException($"Unsupported legacy actor trailer subtype {trailerSubType} at 0x{reader.Offset - 4:X}.");

        int targetVersion = reader.ReadInt32();
        string parentActor;
        if (targetVersion == 0)
        {
            parentActor = reader.ReadString();
            reader.SkipInt32();
            reader.SkipInt32();
            reader.SkipInt32();
        }
        else if (targetVersion == 1)
        {
            parentActor = CinematicSceneActorReader.ReadObjectPathTerminalName(reader);
        }
        else if (targetVersion == 2)
        {
            parentActor = CinematicSceneActorReader.ReadSerializedObjectPathTerminalName(reader, hasSerializedObjectReference: true);
        }
        else
        {
            throw new InvalidDataException($"Unsupported legacy actor reference target version {targetVersion} at 0x{reader.Offset - 4:X}.");
        }

        float offsetX = reader.ReadSingle();
        float offsetY = reader.ReadSingle();
        float offsetZ = reader.ReadSingle();
        float offsetAngle = reader.ReadSingle();
        float localScaleX = reader.ReadSingle();
        float localScaleY = reader.ReadSingle();
        uint useParentFlip = reader.ReadUInt32();
        int scaleInheritProp = reader.ReadInt32();
        uint useParentAlpha = reader.ReadUInt32();
        uint useParentColor = reader.ReadUInt32();
        uint removeWithParent = serializerContext.EngineVersion >= 2015
            ? reader.ReadUInt32()
            : 0;

        CinematicActorBind parentBind = new(
            parentActor,
            offsetX,
            offsetY,
            offsetZ,
            offsetAngle,
            localScaleX,
            localScaleY,
            useParentFlip,
            scaleInheritProp,
            useParentAlpha,
            useParentColor,
            removeWithParent);

        return new CinematicActorTrailer(reader.ReadInt32(), parentBind);
    }

    internal static string ReadObjectPathTerminalName(CinematicBinaryReader reader)
    {
        reader.SkipInt32();
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

        reader.SkipInt32();
        reader.SkipInt32();
        return terminalName;
    }

    internal static string ReadSerializedObjectPathTerminalName(
        CinematicBinaryReader reader,
        bool hasSerializedObjectReference)
    {
        reader.SkipInt32();
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
        reader.SkipInt32();
        if (hasSerializedObjectReference)
        {
            reader.SkipInt32();
            reader.SkipInt32();
        }

        return string.IsNullOrWhiteSpace(objectName) ? terminalLevelName : objectName;
    }
}