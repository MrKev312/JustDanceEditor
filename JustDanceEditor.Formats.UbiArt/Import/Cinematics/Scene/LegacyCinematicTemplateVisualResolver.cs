using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class LegacyCinematicTemplateVisualResolver
{
    internal static LegacyCinematicScene ResolveTemplateVisuals(
        LegacyCinematicScene scene,
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        Dictionary<string, TemplateVisualInfo?> cache = new(StringComparer.OrdinalIgnoreCase);
        List<LegacyCinematicActor> actors = new(scene.Actors.Count);
        int resolvedCount = 0;

        foreach (LegacyCinematicActor actor in scene.Actors)
        {
            if (actor.TexturePath != null ||
                string.IsNullOrWhiteSpace(actor.TemplatePath) ||
                !LegacyCinematicTemplateVisualResolver.TryReadTemplateVisualInfo(fileSystem, actor.TemplatePath, cache, logger, out TemplateVisualInfo? visual))
            {
                actors.Add(actor);
                continue;
            }

            actors.Add(actor with
            {
                TexturePath = visual.TexturePath,
                TexturePaths = visual.TexturePaths,
                MaterialPath = visual.MaterialPath,
                MeshPath = visual.MeshPath,
                VisualComponentTypeId = visual.VisualComponentTypeId,
                AtlasIndex = actor.AtlasIndex == 0 ? visual.AtlasIndex : actor.AtlasIndex,
                Anchor = actor.Anchor == TextureAnchor.MiddleCenter ? visual.Anchor : actor.Anchor,
                CustomAnchorX = actor.CustomAnchorX != 0.0f ? actor.CustomAnchorX : visual.CustomAnchorX,
                CustomAnchorY = actor.CustomAnchorY != 0.0f ? actor.CustomAnchorY : visual.CustomAnchorY,
                ParticleTemplate = visual.ParticleTemplate,
                FxTemplate = visual.FxTemplate
            });
            resolvedCount++;
        }

        if (resolvedCount > 0)
            logger.LogDebug("Resolved {Count} legacy cinematic actor template visual(s).", resolvedCount);

        return new LegacyCinematicScene([.. actors]);
    }

    internal static bool TryReadTemplateVisualInfo(
        JustDanceUbiArtFileSystem fileSystem,
        string templatePath,
        Dictionary<string, TemplateVisualInfo?> cache,
        ILogger logger,
        [NotNullWhen(true)] out TemplateVisualInfo? visual)
    {
        string normalizedTemplatePath = LegacyCinematicNames.NormalizePath(templatePath);
        if (cache.TryGetValue(normalizedTemplatePath, out visual))
            return visual != null;

        visual = null;
        if (!fileSystem.GetFilePath(normalizedTemplatePath, out CookedFile? templateFile))
        {
            cache[normalizedTemplatePath] = null;
            return false;
        }

        try
        {
            byte[] bytes = LegacyCinematicSceneBoundaryReader.ReadFileBytes(fileSystem, templateFile);
            LegacyCinematicBinaryReader reader = new(bytes);
            List<string> texturePaths = [];
            List<string> materialPaths = [];
            HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

            for (int offset = 0; offset <= bytes.Length - 8; offset++)
            {
                if (!LegacyCinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out _))
                    continue;

                string normalized = LegacyCinematicNames.NormalizePath(candidate);
                if (!LegacyCinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized) || !seenPaths.Add(normalized))
                    continue;

                if (LegacyCinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
                    texturePaths.Add(normalized);
                else if (LegacyCinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
                    materialPaths.Add(normalized);
            }

            string? texturePath = texturePaths.FirstOrDefault();
            if (texturePath == null)
            {
                cache[normalizedTemplatePath] = null;
                return false;
            }

            bool hasParticleGeneratorComponent =
                LegacyCinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<LegacyCinematicParticleGeneratorComponentTemplateBinary>());
            bool isFxTemplate =
                LegacyCinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<LegacyCinematicFxControllerComponentTemplateBinary>()) ||
                LegacyCinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<LegacyCinematicFxBankComponentTemplateBinary>());
            LegacyCinematicParticleTemplate? particleTemplate = null;
            if (hasParticleGeneratorComponent)
                _ = LegacyCinematicParticleTemplateReader.TryRead(bytes, out particleTemplate);
            LegacyCinematicFxTemplate? fxTemplate = null;
            if (isFxTemplate)
                _ = LegacyCinematicFxTemplateReader.TryRead(bytes, texturePaths, materialPaths, out fxTemplate);

            if ((hasParticleGeneratorComponent || isFxTemplate) &&
                particleTemplate == null &&
                fxTemplate == null)
            {
                cache[normalizedTemplatePath] = null;
                return false;
            }

            uint componentType = LegacyCinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                bytes,
                LegacyBinarySerializer.GetTypeId<LegacyCinematicMesh3DComponentTemplateBinary>())
                    ? LegacyBinarySerializer.GetTypeId<LegacyCinematicMesh3DComponentBinary>()
                    : LegacyBinarySerializer.GetTypeId<LegacyCinematicMaterialGraphicComponentBinary>();

            visual = new TemplateVisualInfo(
                texturePath,
                [.. texturePaths],
                materialPaths.FirstOrDefault(),
                componentType == LegacyBinarySerializer.GetTypeId<LegacyCinematicMesh3DComponentBinary>()
                    ? materialPaths.FirstOrDefault(path => Path.GetExtension(path).Equals(".msh", StringComparison.OrdinalIgnoreCase))
                    : null,
                componentType,
                0,
                TextureAnchor.MiddleCenter,
                0.0f,
                0.0f,
                particleTemplate,
                fxTemplate);
            cache[normalizedTemplatePath] = visual;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not resolve legacy cinematic actor template visual '{TemplatePath}'.", normalizedTemplatePath);
            cache[normalizedTemplatePath] = null;
            return false;
        }
    }

    internal static bool TryFindMaterialPathRecord(
        LegacyCinematicBinaryReader reader,
        int searchStartOffset,
        int searchLength,
        [NotNullWhen(true)] out string? materialPath,
        out IReadOnlyList<string> texturePaths,
        out int materialTailOffset,
        out int componentEndOffset)
    {
        materialPath = null;
        List<string> foundTexturePaths = [];
        texturePaths = foundTexturePaths;
        materialTailOffset = 0;
        componentEndOffset = 0;

        int searchEndOffset = Math.Min(reader.Bytes.Length - 8, searchStartOffset + searchLength);
        int nextPathSearchOffset = searchStartOffset;
        for (int offset = searchStartOffset; offset <= searchEndOffset; offset++)
        {
            if (offset < nextPathSearchOffset)
                continue;

            if (!LegacyCinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out int candidateEndOffset))
                continue;

            string normalized = LegacyCinematicNames.NormalizePath(candidate);
            if (LegacyCinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized) && LegacyCinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
            {
                foundTexturePaths.Add(normalized);
                nextPathSearchOffset = candidateEndOffset;
                continue;
            }

            if (!LegacyCinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
                continue;

            if (!LegacyCinematicTemplateVisualResolver.TryGetActorBoundaryAfterMaterialTail(reader, candidateEndOffset, out int boundaryOffset))
                continue;

            materialPath = normalized;
            materialTailOffset = candidateEndOffset;
            componentEndOffset = boundaryOffset;
            return true;
        }

        return false;
    }

    internal static bool TryFindMesh3DPathRecords(
        LegacyCinematicBinaryReader reader,
        int searchStartOffset,
        int searchLength,
        out IReadOnlyList<string> texturePaths,
        out string? materialPath,
        out string? meshPath,
        out int componentEndOffset)
    {
        List<string> foundTexturePaths = [];
        texturePaths = foundTexturePaths;
        materialPath = null;
        meshPath = null;
        componentEndOffset = 0;

        int searchEndOffset = Math.Min(reader.Bytes.Length - 8, searchStartOffset + searchLength);
        int nextPathSearchOffset = searchStartOffset;
        for (int offset = searchStartOffset; offset <= searchEndOffset; offset++)
        {
            if (offset < nextPathSearchOffset)
                continue;

            if (offset > searchStartOffset && LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                break;

            if (!LegacyCinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out int candidateEndOffset))
                continue;

            string normalized = LegacyCinematicNames.NormalizePath(candidate);
            if (!LegacyCinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized))
                continue;

            if (LegacyCinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
            {
                foundTexturePaths.Add(normalized);
            }
            else if (materialPath == null && LegacyCinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
            {
                materialPath = normalized;
            }
            else if (meshPath == null && LegacyCinematicSceneBoundaryReader.IsLikelyMeshPath(normalized))
            {
                meshPath = normalized;
            }
            else
            {
                continue;
            }

            componentEndOffset = candidateEndOffset;
            nextPathSearchOffset = candidateEndOffset;
        }

        return foundTexturePaths.Count > 0 || materialPath != null || meshPath != null;
    }

    internal static bool TryGetActorBoundaryAfterMaterialTail(
        LegacyCinematicBinaryReader reader,
        int materialPathEndOffset,
        out int boundaryOffset)
    {
        boundaryOffset = materialPathEndOffset + LegacyCinematicConstants.MaterialGraphicTailLength;
        if (LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, boundaryOffset))
            return true;

        LegacyCinematicBinaryReader probe = new(reader.Bytes, boundaryOffset);
        int zeroTailBoundaryOffset = LegacyCinematicSceneBoundaryReader.GetActorBoundaryOffsetAfterZeroTail(probe);
        if (zeroTailBoundaryOffset != boundaryOffset && LegacyCinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, zeroTailBoundaryOffset))
        {
            boundaryOffset = zeroTailBoundaryOffset;
            return true;
        }

        return false;
    }

    internal static bool TryReadPathAt(
        LegacyCinematicBinaryReader reader,
        int offset,
        [NotNullWhen(true)] out string? path,
        out int endOffset)
    {
        path = null;
        endOffset = 0;
        if (!LegacyCinematicTemplateVisualResolver.IsPathRecordAt(reader.Bytes, offset))
            return false;

        try
        {
            LegacyCinematicBinaryReader probe = new(reader.Bytes, offset);
            path = probe.ReadPath();
            endOffset = probe.Offset;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    internal static bool IsPathRecordAt(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + 8 > bytes.Length)
            return false;

        int folderLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
        if (folderLength is <= 0 or > 4096)
            return false;

        int fileLengthOffset = offset + 4 + folderLength;
        if (fileLengthOffset + 8 > bytes.Length)
            return false;

        int fileLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(fileLengthOffset, 4));
        return fileLength > 0 &&
            fileLength <= 4096 &&
            fileLengthOffset + 4 + fileLength + 4 <= bytes.Length;
    }
}