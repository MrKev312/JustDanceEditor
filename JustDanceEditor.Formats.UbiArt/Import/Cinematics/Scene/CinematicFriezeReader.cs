using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicFriezeReader
{
    private const uint FriezeTypeId = 0x99BA2AA8;
    private const int MaxFriezeVertices = 16_384;
    private const int MaxFriezeIndices = 196_608;
    private const int MaxFriezeIndexLists = 64;

    public static IReadOnlyList<CinematicActor> ReadFriezes(
        JustDanceUbiArtFileSystem fileSystem,
        byte[] bytes,
        int startOffset,
        int endOffset,
        IReadOnlyList<string> parentPath,
        int firstSiblingOrder,
        LegacyBinarySerializerContext serializerContext,
        ILogger logger)
    {
        int start = Math.Clamp(startOffset, 0, bytes.Length);
        int end = Math.Clamp(endOffset, start, bytes.Length);
        if (end - start < 64)
            return [];

        List<CinematicActor> actors = [];
        HashSet<string> actorKeys = new(StringComparer.OrdinalIgnoreCase);
        int siblingOrder = firstSiblingOrder;
        for (int offset = start; offset < end - 64; offset++)
        {
            if (!TryReadFriezeAt(
                fileSystem,
                bytes,
                offset,
                end,
                parentPath,
                siblingOrder,
                logger,
                out CinematicActor? actor,
                out int nextSearchOffset))
            {
                if (!TryReadUntypedMaterialGraphicAt(
                    bytes,
                    offset,
                    end,
                    parentPath,
                    siblingOrder,
                    serializerContext,
                    out actor,
                    out nextSearchOffset))
                {
                    continue;
                }
            }

            if (actorKeys.Add(actor.Key))
            {
                actors.Add(actor);
                siblingOrder++;
            }

            if (nextSearchOffset > offset)
                offset = Math.Min(nextSearchOffset - 1, end - 1);
        }

        if (actors.Count > 0)
        {
            logger.LogDebug(
                "Materialized {Count} legacy frieze actor(s) under '{ParentPath}' from 0x{Start:X}..0x{End:X}.",
                actors.Count,
                string.Join("/", parentPath),
                start,
                end);
        }

        return actors;
    }

    private static bool TryReadUntypedMaterialGraphicAt(
        byte[] bytes,
        int offset,
        int limit,
        IReadOnlyList<string> parentPath,
        int siblingOrder,
        LegacyBinarySerializerContext serializerContext,
        [NotNullWhen(true)] out CinematicActor? actor,
        out int nextSearchOffset)
    {
        actor = null;
        nextSearchOffset = offset + 1;

        if (!TryReadPickableFields(bytes, offset, limit, out CinematicPickableFields pickable, out int commonEndOffset))
            return false;

        int searchEnd = Math.Min(
            limit,
            CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(bytes, offset + 1) ?? limit);
        if (!TryFindPath(
            bytes,
            commonEndOffset,
            searchEnd,
            IsMaterialGraphicTemplatePath,
            out string? templatePath,
            out _,
            out int templatePathEndOffset))
        {
            return false;
        }

        CinematicBinaryReader pathReader = new(bytes);
        if (!CinematicTemplateVisualResolver.TryFindMaterialPathRecord(
            pathReader,
            commonEndOffset,
            searchEnd - commonEndOffset,
            out string? materialPath,
            out IReadOnlyList<string> texturePaths,
            out string? explicitAtlasPath,
            out int atlasTextureSlot,
            out int materialTailOffset,
            out int componentEndOffset) ||
            texturePaths.Count == 0)
        {
            return false;
        }

        string? texturePath = CinematicTemplateVisualResolver
            .NormalizeTextureOrderForMaterial(texturePaths)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(texturePath))
            return false;

        uint materialGraphicComponentTypeId = LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicComponentBinary>();
        RgbTint baseTint = RgbTint.White;
        float baseAlpha = 1.0f;
        TextureAnchor anchor = TextureAnchor.MiddleCenter;
        float customAnchorX = 0.0f;
        float customAnchorY = 0.0f;
        int atlasIndex = 0;
        CinematicSinusParameters sinus = default;
        if (TryFindBigEndianUInt32(bytes, commonEndOffset, searchEnd, materialGraphicComponentTypeId, out int componentTypeOffset))
        {
            int componentStart = componentTypeOffset + sizeof(uint);
            baseTint = CinematicSceneBoundaryReader.ReadComponentTint(bytes, componentStart);
            baseAlpha = CinematicSceneBoundaryReader.ReadComponentAlpha(bytes, componentStart);
            CinematicMaterialGraphicFields materialFields = CinematicSceneBoundaryReader.ReadMaterialGraphicFields(
                bytes,
                componentStart,
                serializerContext);
            atlasIndex = materialFields.AtlasIndex;
            anchor = materialFields.Anchor;
            customAnchorX = materialFields.CustomAnchorX;
            customAnchorY = materialFields.CustomAnchorY;
            if (materialTailOffset > 0)
                sinus = CinematicSceneBoundaryReader.ReadMaterialGraphicSinusParameters(bytes, materialTailOffset);
        }

        string[] path = [.. parentPath, pickable.Name];
        actor = new CinematicActor(
            path,
            pickable.Name,
            offset,
            siblingOrder,
            FriezeTypeId,
            pickable.RelativeZ,
            pickable.ScaleX,
            pickable.ScaleY,
            pickable.XFlipped,
            pickable.Angle,
            pickable.PositionX,
            pickable.PositionY,
            templatePath,
            SubScenePath: null,
            texturePath,
            CinematicTemplateVisualResolver.NormalizeTextureOrderForMaterial(texturePaths),
            materialPath,
            explicitAtlasPath,
            MeshPath: null,
            VisualComponentTypeId: materialGraphicComponentTypeId,
            atlasIndex,
            atlasTextureSlot,
            anchor,
            customAnchorX,
            customAnchorY,
            ScenePriority: 0,
            pickable.DefaultEnabled,
            baseTint,
            baseAlpha,
            ParentBind: null,
            Sinus: sinus);
        nextSearchOffset = Math.Max(
            Math.Max(commonEndOffset + 1, templatePathEndOffset),
            componentEndOffset > 0 ? componentEndOffset : templatePathEndOffset);
        return true;
    }

    private static bool TryReadFriezeAt(
        JustDanceUbiArtFileSystem fileSystem,
        byte[] bytes,
        int offset,
        int limit,
        IReadOnlyList<string> parentPath,
        int siblingOrder,
        ILogger logger,
        [NotNullWhen(true)] out CinematicActor? actor,
        out int nextSearchOffset)
    {
        actor = null;
        nextSearchOffset = offset + 1;

        if (!TryReadPickableFields(bytes, offset, limit, out CinematicPickableFields pickable, out int commonEndOffset))
            return false;

        int searchEnd = Math.Min(
            limit,
            CinematicSceneBoundaryReader.FindNextActorBoundaryOffset(bytes, offset + 1) ?? limit);
        if (!TryFindPath(
            bytes,
            commonEndOffset,
            searchEnd,
            IsFriezeConfigPath,
            out string? configPath,
            out int configPathOffset,
            out int configPathEndOffset))
        {
            return false;
        }

        if (!TryReadFriezeVisuals(fileSystem, configPath, logger, out string? texturePath, out string? materialPath))
            return false;

        if (!TryFindMeshGeometry(bytes, commonEndOffset, configPathOffset, out RenderGeometry? geometry))
            return false;

        string[] path = [.. parentPath, pickable.Name];
        actor = new CinematicActor(
            path,
            pickable.Name,
            offset,
            siblingOrder,
            FriezeTypeId,
            pickable.RelativeZ,
            pickable.ScaleX,
            pickable.ScaleY,
            pickable.XFlipped,
            pickable.Angle,
            pickable.PositionX,
            pickable.PositionY,
            configPath,
            SubScenePath: null,
            texturePath,
            [texturePath],
            materialPath,
            ExplicitAtlasPath: null,
            MeshPath: null,
            VisualComponentTypeId: FriezeTypeId,
            AtlasIndex: 0,
            AtlasTextureSlot: 0,
            Anchor: TextureAnchor.MiddleCenter,
            CustomAnchorX: 0.0f,
            CustomAnchorY: 0.0f,
            ScenePriority: 0,
            pickable.DefaultEnabled,
            RgbTint.White,
            BaseAlpha: 1.0f,
            ParentBind: null,
            GeometryOverride: geometry);
        nextSearchOffset = Math.Max(commonEndOffset + 1, configPathEndOffset);
        return true;
    }

    private static bool TryReadPickableFields(
        byte[] bytes,
        int offset,
        int limit,
        out CinematicPickableFields fields,
        out int commonEndOffset)
    {
        fields = default!;
        commonEndOffset = 0;
        if (offset < 0 || offset + 40 > limit)
            return false;

        float relativeZ = ReadSingleOrDefault(bytes, offset, float.NaN);
        float scaleX = ReadSingleOrDefault(bytes, offset + 4, float.NaN);
        float scaleY = ReadSingleOrDefault(bytes, offset + 8, float.NaN);
        uint xFlipped = ReadUInt32OrDefault(bytes, offset + 12, uint.MaxValue);
        int nameLength = ReadInt32OrDefault(bytes, offset + 16, -1);
        if (!float.IsFinite(relativeZ) ||
            !float.IsFinite(scaleX) ||
            !float.IsFinite(scaleY) ||
            Math.Abs(scaleX) > 1024.0f ||
            Math.Abs(scaleY) > 1024.0f ||
            xFlipped is not (0 or 1) ||
            nameLength is <= 0 or > 256 ||
            offset + 20 + nameLength + 16 > limit ||
            !IsMostlyText(bytes, offset + 20, nameLength))
        {
            return false;
        }

        string name = System.Text.Encoding.UTF8.GetString(bytes, offset + 20, nameLength).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains('/', StringComparison.Ordinal) ||
            name.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        int transformOffset = offset + 20 + nameLength;
        uint defaultEnable = ReadUInt32OrDefault(bytes, transformOffset, 0);
        float positionX = ReadSingleOrDefault(bytes, transformOffset + 4, float.NaN);
        float positionY = ReadSingleOrDefault(bytes, transformOffset + 8, float.NaN);
        float angle = ReadSingleOrDefault(bytes, transformOffset + 12, float.NaN);
        if (defaultEnable > 1 ||
            !float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !float.IsFinite(angle) ||
            Math.Abs(positionX) > 100_000.0f ||
            Math.Abs(positionY) > 100_000.0f ||
            Math.Abs(angle) > 10_000.0f)
        {
            return false;
        }

        fields = new CinematicPickableFields(
            relativeZ,
            scaleX,
            scaleY,
            xFlipped,
            name,
            defaultEnable != 0,
            positionX,
            positionY,
            angle,
            TemplatePath: string.Empty);
        commonEndOffset = transformOffset + 16;
        return true;
    }

    private static bool TryFindMeshGeometry(
        byte[] bytes,
        int startOffset,
        int endOffset,
        [NotNullWhen(true)] out RenderGeometry? geometry)
    {
        geometry = null;
        int start = Math.Clamp(startOffset, 0, bytes.Length);
        int end = Math.Clamp(endOffset, start, bytes.Length);
        for (int offset = start; offset <= end - 28; offset++)
        {
            if (TryReadMeshGeometryAt(bytes, offset, end, out geometry, out _))
                return true;
        }

        return false;
    }

    private static bool TryReadMeshGeometryAt(
        byte[] bytes,
        int offset,
        int limit,
        [NotNullWhen(true)] out RenderGeometry? geometry,
        out int endOffset)
    {
        geometry = null;
        endOffset = offset;
        try
        {
            CinematicBinaryReader reader = new(bytes, offset);
            if (!TrySkipIndexListArray(reader, limit))
                return false;

            int animatedVertexCount = reader.ReadInt32();
            if (animatedVertexCount is < 0 or > MaxFriezeVertices ||
                reader.Offset + (animatedVertexCount * 24) > limit)
            {
                return false;
            }

            reader.Skip(animatedVertexCount * 24);
            int staticIndexListCount = reader.ReadInt32();
            if (staticIndexListCount is <= 0 or > MaxFriezeIndexLists)
                return false;

            List<int>? indices = null;
            for (int indexList = 0; indexList < staticIndexListCount; indexList++)
            {
                if (!TryReadIndexList(reader, limit, out List<int> indexListValues))
                    return false;

                if (indices == null && indexListValues.Count >= 3)
                    indices = indexListValues;
            }

            int vertexCount = reader.ReadInt32();
            if (vertexCount is < 3 or > MaxFriezeVertices ||
                reader.Offset + (vertexCount * 24) > limit ||
                indices == null ||
                indices.Count < 3)
            {
                return false;
            }

            List<RenderVertex> vertices = new(vertexCount);
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                vertices.Add(ReadVertexPCT(reader));

            if (indices.Any(index => index < 0 || index >= vertices.Count))
                return false;

            geometry = RenderGeometry.FromMesh(vertices, indices, CinematicGeometrySource.MeshAtlas);
            endOffset = reader.Offset;
            return geometry.Vertices.Count >= 3 && geometry.Indices.Count >= 3;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        {
            geometry = null;
            endOffset = offset;
            return false;
        }
    }

    private static bool TrySkipIndexListArray(CinematicBinaryReader reader, int limit)
    {
        int count = reader.ReadInt32();
        if (count is < 0 or > MaxFriezeIndexLists)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!TryReadIndexList(reader, limit, out _))
                return false;
        }

        return true;
    }

    private static bool TryReadIndexList(
        CinematicBinaryReader reader,
        int limit,
        out List<int> indices)
    {
        indices = [];
        int indexCount = reader.ReadInt32();
        if (indexCount is < 0 or > MaxFriezeIndices ||
            reader.Offset + (indexCount * 2) + 4 > limit)
        {
            return false;
        }

        indices = new List<int>(indexCount);
        for (int index = 0; index < indexCount; index++)
            indices.Add(reader.ReadUInt16());

        reader.SkipInt32(); // IdTexConfig.
        return true;
    }

    private static RenderVertex ReadVertexPCT(CinematicBinaryReader reader)
    {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();
        uint color = reader.ReadUInt32();
        float u = reader.ReadSingle();
        float v = reader.ReadSingle();
        return new RenderVertex(
            x,
            y,
            z,
            u,
            v,
            ColorRed: ((color >> 16) & 0xFF) / 255.0,
            ColorGreen: ((color >> 8) & 0xFF) / 255.0,
            ColorBlue: (color & 0xFF) / 255.0,
            ColorAlpha: ((color >> 24) & 0xFF) / 255.0);
    }

    private static bool TryReadFriezeVisuals(
        JustDanceUbiArtFileSystem fileSystem,
        string configPath,
        ILogger logger,
        [NotNullWhen(true)] out string? texturePath,
        out string? materialPath)
    {
        texturePath = null;
        materialPath = null;
        if (!TryGetCookedFile(fileSystem, configPath, out CookedFile? configFile))
            return false;

        try
        {
            byte[] configBytes = CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, configFile);
            CinematicBinaryReader pathReader = new(configBytes);
            for (int offset = 0; offset <= configBytes.Length - 8; offset++)
            {
                if (!CinematicTemplateVisualResolver.TryReadPathAt(pathReader, offset, out string? candidate, out int endOffset))
                    continue;

                string normalized = NormalizeFriezePath(candidate);
                if (!CinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized))
                    continue;

                if (texturePath == null && CinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
                {
                    texturePath = normalized;
                    offset = Math.Max(offset, endOffset - 1);
                    continue;
                }

                if (materialPath == null && CinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
                {
                    materialPath = normalized;
                    offset = Math.Max(offset, endOffset - 1);
                }
            }

            return texturePath != null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not read legacy frieze config '{ConfigPath}'.", configPath);
            return false;
        }
    }

    private static bool TryFindPath(
        byte[] bytes,
        int startOffset,
        int endOffset,
        Func<string, bool> predicate,
        [NotNullWhen(true)] out string? path,
        out int pathOffset,
        out int pathEndOffset)
    {
        path = null;
        pathOffset = 0;
        pathEndOffset = 0;
        CinematicBinaryReader pathReader = new(bytes);
        int start = Math.Clamp(startOffset, 0, bytes.Length);
        int end = Math.Clamp(endOffset, start, bytes.Length);
        for (int offset = start; offset <= end - 8; offset++)
        {
            if (!CinematicTemplateVisualResolver.TryReadPathAt(pathReader, offset, out string? candidate, out int candidateEndOffset))
                continue;

            string normalized = NormalizeFriezePath(candidate);
            if (!CinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized) ||
                !predicate(normalized))
            {
                continue;
            }

            path = normalized;
            pathOffset = offset;
            pathEndOffset = candidateEndOffset;
            return true;
        }

        return false;
    }

    private static bool IsFriezeConfigPath(string path) =>
        Path.GetExtension(path).Equals(".fcg", StringComparison.OrdinalIgnoreCase);

    private static bool IsMaterialGraphicTemplatePath(string path) =>
        path.EndsWith("tpl_materialgraphiccomponent.tpl", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("materialgraphiccomponent.tpl", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFriezePath(string path)
    {
        string normalized = CinematicNames.NormalizePath(path);
        return normalized.EndsWith(".msht", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^1]
            : normalized;
    }

    private static bool TryFindBigEndianUInt32(
        byte[] bytes,
        int startOffset,
        int endOffset,
        uint value,
        out int offset)
    {
        offset = 0;
        int start = Math.Clamp(startOffset, 0, bytes.Length);
        int end = Math.Clamp(endOffset, start, bytes.Length);
        for (int candidate = start; candidate <= end - sizeof(uint); candidate++)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(candidate, sizeof(uint))) != value)
                continue;

            offset = candidate;
            return true;
        }

        return false;
    }

    private static bool TryGetCookedFile(
        JustDanceUbiArtFileSystem fileSystem,
        string path,
        [NotNullWhen(true)] out CookedFile? file)
    {
        string normalized = NormalizeFriezePath(path);
        if (fileSystem.GetFilePath(normalized, out file))
            return true;

        if (!normalized.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) &&
            fileSystem.GetFilePath($"{normalized}.ckd", out file))
        {
            return true;
        }

        file = null;
        return false;
    }

    private static int ReadInt32OrDefault(byte[] bytes, int offset, int defaultValue) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4))
            : defaultValue;

    private static uint ReadUInt32OrDefault(byte[] bytes, int offset, uint defaultValue) =>
        offset >= 0 && offset + 4 <= bytes.Length
            ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))
            : defaultValue;

    private static float ReadSingleOrDefault(byte[] bytes, int offset, float defaultValue)
    {
        if (offset < 0 || offset + 4 > bytes.Length)
            return defaultValue;

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

            if (value is < 0x20 or > 0x7E)
                return false;
        }

        return true;
    }
}