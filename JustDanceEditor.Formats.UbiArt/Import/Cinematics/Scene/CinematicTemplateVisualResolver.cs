using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Particles;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Scene;

internal static class CinematicTemplateVisualResolver
{
    internal static CinematicScene ResolveTemplateVisuals(
        CinematicScene scene,
        JustDanceUbiArtFileSystem fileSystem,
        ILogger logger)
    {
        Dictionary<string, TemplateVisualInfo?> cache = new(StringComparer.OrdinalIgnoreCase);
        List<CinematicActor> actors = new(scene.Actors.Count);
        int resolvedCount = 0;

        foreach (CinematicActor actor in scene.Actors)
        {
            if (string.IsNullOrWhiteSpace(actor.TemplatePath))
            {
                actors.Add(actor);
                continue;
            }

            if (!CinematicTemplateVisualResolver.TryReadTemplateVisualInfo(fileSystem, actor.TemplatePath, cache, logger, out TemplateVisualInfo? visual))
            {
                actors.Add(actor);
                continue;
            }

            actors.Add(actor with
            {
                TexturePath = actor.TexturePath ?? visual.TexturePath,
                TexturePaths = actor.TexturePaths.Count > 0 ? actor.TexturePaths : visual.TexturePaths,
                MaterialPath = actor.MaterialPath ?? visual.MaterialPath,
                ExplicitAtlasPath = actor.ExplicitAtlasPath ?? visual.ExplicitAtlasPath,
                MeshPath = actor.MeshPath ?? visual.MeshPath,
                VisualComponentTypeId = actor.VisualComponentTypeId ?? visual.VisualComponentTypeId,
                AtlasIndex = actor.AtlasIndex == 0 ? visual.AtlasIndex : actor.AtlasIndex,
                AtlasTextureSlot = actor.AtlasTextureSlot == 0 ? visual.AtlasTextureSlot : actor.AtlasTextureSlot,
                Anchor = actor.Anchor == TextureAnchor.MiddleCenter ? visual.Anchor : actor.Anchor,
                CustomAnchorX = actor.CustomAnchorX != 0.0f ? actor.CustomAnchorX : visual.CustomAnchorX,
                CustomAnchorY = actor.CustomAnchorY != 0.0f ? actor.CustomAnchorY : visual.CustomAnchorY,
                ParticleTemplate = visual.ParticleTemplate,
                FxTemplate = visual.FxTemplate,
                AnimLightTemplate = actor.AnimLightTemplate ?? visual.AnimLightTemplate
            });
            resolvedCount++;
        }

        if (resolvedCount > 0)
            logger.LogDebug("Resolved {Count} cinematic actor template visual(s).", resolvedCount);

        return new CinematicScene([.. actors]);
    }

    private static IReadOnlyList<string> MergeTexturePaths(
        IReadOnlyList<string> texturePaths,
        IEnumerable<string> additionalTexturePaths)
    {
        List<string> merged = [.. texturePaths];
        HashSet<string> seen = new(merged, StringComparer.OrdinalIgnoreCase);
        foreach (string texturePath in additionalTexturePaths)
        {
            if (!string.IsNullOrWhiteSpace(texturePath) && seen.Add(texturePath))
                merged.Add(texturePath);
        }

        return merged;
    }

    internal static bool TryReadTemplateVisualInfo(
        JustDanceUbiArtFileSystem fileSystem,
        string templatePath,
        Dictionary<string, TemplateVisualInfo?> cache,
        ILogger logger,
        [NotNullWhen(true)] out TemplateVisualInfo? visual)
    {
        string normalizedTemplatePath = CinematicNames.NormalizePath(templatePath);
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
            byte[] bytes = CinematicSceneBoundaryReader.ReadFileBytes(fileSystem, templateFile);
            CinematicBinaryReader reader = new(bytes);
            List<string> texturePaths = [];
            List<string> materialPaths = [];
            List<string> meshPaths = [];
            List<string> atlasPaths = [];
            List<string> orderedTexturePaths = [];
            List<string> orderedMaterialPaths = [];
            HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

            for (int offset = 0; offset <= bytes.Length - 8; offset++)
            {
                if (!CinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out _))
                    continue;

                string normalized = CinematicNames.NormalizePath(candidate);
                if (!CinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized))
                    continue;

                if (CinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
                {
                    orderedTexturePaths.Add(normalized);
                    if (seenPaths.Add(normalized))
                        texturePaths.Add(normalized);
                }
                else if (CinematicSceneBoundaryReader.IsLikelyAtlasPath(normalized))
                {
                    if (seenPaths.Add(normalized))
                        atlasPaths.Add(normalized);
                }
                else if (CinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
                {
                    orderedMaterialPaths.Add(normalized);
                    if (seenPaths.Add(normalized))
                        materialPaths.Add(normalized);
                }
                else if (CinematicSceneBoundaryReader.IsLikelyMeshPath(normalized))
                {
                    if (seenPaths.Add(normalized))
                        meshPaths.Add(normalized);
                }
            }

            bool hasParticleGeneratorComponent =
                CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<CinematicParticleGeneratorComponentTemplateBinary>());
            bool hasPleoTextureGraphicComponent =
                CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentTemplateBinary>());
            bool hasAnimLightComponent =
                CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<CinematicAnimLightComponentTemplateBinary>()) ||
                CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<CinematicAnimatedComponentTemplateBinary>());
            bool isFxTemplate =
                CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<CinematicFxControllerComponentTemplateBinary>()) ||
                CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                    bytes,
                    LegacyBinarySerializer.GetTypeId<CinematicFxBankComponentTemplateBinary>());
            IReadOnlyList<string> normalizedTexturePaths =
                NormalizeTextureOrderForMaterial(texturePaths);
            string? texturePath = normalizedTexturePaths.FirstOrDefault();
            CinematicParticleTemplate? particleTemplate = null;
            if (hasParticleGeneratorComponent)
                CinematicParticleTemplateReader.TryRead(bytes, out particleTemplate);
            CinematicFxTemplate? fxTemplate = null;
            if (isFxTemplate)
            {
                CinematicFxTemplateReader.TryRead(
                    bytes,
                    orderedTexturePaths.Count > 0 ? orderedTexturePaths : texturePaths,
                    orderedMaterialPaths.Count > 0 ? orderedMaterialPaths : materialPaths,
                    out fxTemplate);
            }
            CinematicAnimLightTemplate? animLightTemplate = null;
            if (hasAnimLightComponent)
                CinematicAnimLightTemplateReader.TryRead(fileSystem, bytes, logger, out animLightTemplate);

            if (animLightTemplate != null && animLightTemplate.TexturePathsByPatchBankNameId.Count > 0)
            {
                normalizedTexturePaths = MergeTexturePaths(
                    normalizedTexturePaths,
                    animLightTemplate.TexturePathsByPatchBankNameId.Values);
                texturePath ??= normalizedTexturePaths.FirstOrDefault();
            }

            if ((hasParticleGeneratorComponent || isFxTemplate) &&
                particleTemplate == null &&
                fxTemplate == null)
            {
                cache[normalizedTemplatePath] = null;
                return false;
            }

            if (texturePath == null &&
                materialPaths.Count == 0 &&
                !hasPleoTextureGraphicComponent &&
                animLightTemplate == null &&
                particleTemplate == null &&
                fxTemplate == null)
            {
                cache[normalizedTemplatePath] = null;
                return false;
            }

            uint componentType = CinematicSceneBoundaryReader.ContainsBigEndianUInt32(
                bytes,
                LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentTemplateBinary>())
                    ? LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentBinary>()
                    : animLightTemplate != null
                        ? LegacyBinarySerializer.GetTypeId<CinematicAnimLightComponentBinary>()
                        : hasPleoTextureGraphicComponent
                        ? LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>()
                        : LegacyBinarySerializer.GetTypeId<CinematicMaterialGraphicComponentBinary>();

            visual = new TemplateVisualInfo(
                texturePath,
                normalizedTexturePaths,
                materialPaths.FirstOrDefault(),
                atlasPaths.FirstOrDefault(),
                componentType == LegacyBinarySerializer.GetTypeId<CinematicMesh3DComponentBinary>()
                    ? meshPaths.FirstOrDefault()
                    : null,
                componentType,
                0,
                0,
                TextureAnchor.MiddleCenter,
                0.0f,
                0.0f,
                particleTemplate,
                fxTemplate,
                animLightTemplate);
            cache[normalizedTemplatePath] = visual;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Could not resolve cinematic actor template visual '{TemplatePath}'.", normalizedTemplatePath);
            cache[normalizedTemplatePath] = null;
            return false;
        }
    }

    internal static bool TryFindMaterialPathRecord(
        CinematicBinaryReader reader,
        int searchStartOffset,
        int searchLength,
        out string? materialPath,
        out IReadOnlyList<string> texturePaths,
        out string? explicitAtlasPath,
        out int atlasTextureSlot,
        out int materialTailOffset,
        out int componentEndOffset)
    {
        materialPath = null;
        explicitAtlasPath = null;
        atlasTextureSlot = 0;
        List<string> foundTexturePaths = [];
        texturePaths = foundTexturePaths;
        materialTailOffset = 0;
        componentEndOffset = 0;
        string? pendingMaterialPath = null;
        int pendingMaterialStartOffset = 0;
        int pendingMaterialTailOffset = 0;
        int explicitAtlasPathStartOffset = 0;
        int lastTexturePathEndOffset = 0;
        int lastDynamicTexturePathEndOffset = 0;

        int searchEndOffset = Math.Min(reader.Bytes.Length - 8, searchStartOffset + searchLength);
        int nextPathSearchOffset = searchStartOffset;
        for (int offset = searchStartOffset; offset <= searchEndOffset; offset++)
        {
            if (offset < nextPathSearchOffset)
                continue;

            if (offset > searchStartOffset && CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                break;

            if (!CinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out int candidateEndOffset))
                continue;

            string normalized = CinematicNames.NormalizePath(candidate);
            if (!CinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized))
                continue;

            if (CinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
            {
                foundTexturePaths.Add(normalized);
                lastTexturePathEndOffset = candidateEndOffset;
                if (CinematicNames.IsDynamicPleoTexturePath(normalized))
                    lastDynamicTexturePathEndOffset = candidateEndOffset;

                nextPathSearchOffset = candidateEndOffset;
                continue;
            }

            if (explicitAtlasPath == null &&
                CinematicSceneBoundaryReader.IsLikelyAtlasPath(normalized))
            {
                explicitAtlasPath = normalized;
                explicitAtlasPathStartOffset = offset;
                nextPathSearchOffset = candidateEndOffset;
                continue;
            }

            if (!CinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
                continue;

            if (!CinematicTemplateVisualResolver.TryGetActorBoundaryAfterMaterialTail(reader, candidateEndOffset, out int boundaryOffset))
            {
                pendingMaterialPath ??= normalized;
                if (pendingMaterialStartOffset == 0)
                    pendingMaterialStartOffset = offset;
                if (pendingMaterialTailOffset == 0)
                    pendingMaterialTailOffset = candidateEndOffset;

                nextPathSearchOffset = candidateEndOffset;
                continue;
            }

            materialPath = normalized;
            atlasTextureSlot = CinematicTemplateVisualResolver.ReadAtlasTextureSlotOrDefault(
                reader.Bytes,
                explicitAtlasPathStartOffset > 0 ? explicitAtlasPathStartOffset - 4 : offset - 8);
            materialTailOffset = candidateEndOffset;
            componentEndOffset = boundaryOffset;
            return true;
        }

        if (pendingMaterialPath != null)
        {
            materialPath = pendingMaterialPath;
            atlasTextureSlot = CinematicTemplateVisualResolver.ReadAtlasTextureSlotOrDefault(
                reader.Bytes,
                explicitAtlasPathStartOffset > 0 ? explicitAtlasPathStartOffset - 4 : pendingMaterialStartOffset - 8);
            componentEndOffset = Math.Max(pendingMaterialTailOffset, lastTexturePathEndOffset);
            if (lastDynamicTexturePathEndOffset > 0)
                materialTailOffset = pendingMaterialTailOffset;
            return true;
        }

        if (foundTexturePaths.Count > 0)
        {
            if (CinematicTemplateVisualResolver.TryGetActorBoundaryAfterMaterialTail(reader, lastTexturePathEndOffset, out int boundaryOffset))
            {
                materialTailOffset = lastTexturePathEndOffset;
                componentEndOffset = boundaryOffset;
                return true;
            }

            componentEndOffset = lastTexturePathEndOffset;
            return true;
        }

        return false;
    }

    internal static IReadOnlyList<string> NormalizeTextureOrderForMaterial(
        IReadOnlyList<string> texturePaths)
    {
        // Material layers bind fixed texture-set slots
        // (diffuse, diffuse_2, diffuse_3, diffuse_4). A shader/material filename
        // matching a later texture does not promote that texture to layer 0; in
        // JD2014 data those later matches are often animated masks.
        return texturePaths;
    }

    private static int ReadAtlasTextureSlotOrDefault(byte[] bytes, int offset)
    {
        int value = CinematicSceneBoundaryReader.ReadInt32OrDefault(bytes, offset);
        return value is >= 0 and <= 3 ? value : 0;
    }

    internal static bool TryFindMesh3DPathRecords(
        CinematicBinaryReader reader,
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

            if (offset > searchStartOffset && CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, offset))
                break;

            if (!CinematicTemplateVisualResolver.TryReadPathAt(reader, offset, out string? candidate, out int candidateEndOffset))
                continue;

            string normalized = CinematicNames.NormalizePath(candidate);
            if (!CinematicSceneBoundaryReader.IsCleanLegacyAssetPath(normalized))
                continue;

            if (CinematicSceneBoundaryReader.IsLikelyTexturePath(normalized))
            {
                foundTexturePaths.Add(normalized);
            }
            else if (materialPath == null && CinematicSceneBoundaryReader.IsLikelyMaterialPath(normalized))
            {
                materialPath = normalized;
            }
            else if (meshPath == null && CinematicSceneBoundaryReader.IsLikelyMeshPath(normalized))
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
        CinematicBinaryReader reader,
        int materialPathEndOffset,
        out int boundaryOffset)
    {
        boundaryOffset = materialPathEndOffset + CinematicConstants.MaterialGraphicTailLength;
        if (CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, boundaryOffset))
            return true;

        CinematicBinaryReader probe = new(reader.Bytes, boundaryOffset);
        int zeroTailBoundaryOffset = CinematicSceneBoundaryReader.GetActorBoundaryOffsetAfterZeroTail(probe);
        if (zeroTailBoundaryOffset != boundaryOffset && CinematicSceneBoundaryReader.IsActorBoundaryAt(reader.Bytes, zeroTailBoundaryOffset))
        {
            boundaryOffset = zeroTailBoundaryOffset;
            return true;
        }

        return false;
    }

    internal static bool TryReadPathAt(
        CinematicBinaryReader reader,
        int offset,
        [NotNullWhen(true)] out string? path,
        out int endOffset)
    {
        path = null;
        endOffset = 0;
        if (!CinematicTemplateVisualResolver.IsPathRecordAt(reader.Bytes, offset))
            return false;

        try
        {
            CinematicBinaryReader probe = new(reader.Bytes, offset);
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