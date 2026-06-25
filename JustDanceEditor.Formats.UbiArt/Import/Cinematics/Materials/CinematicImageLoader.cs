using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Materials;
using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class CinematicImageLoader
{
    public static async Task<Dictionary<string, MaterializedCinematicImage>> LoadActorImagesAsync(
        CinematicScene scene,
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        ILogger logger)
    {
        Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CinematicRenderMaterial> materials = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, CinematicAtlas?> looseAtlases = new(StringComparer.OrdinalIgnoreCase);
        CinematicAtlasContainer atlasContainer = CinematicAtlasLoader.LoadDefault(fileSystem, logger);
        foreach (CinematicActor actor in scene.Actors)
        {
            if (images.ContainsKey(actor.Key))
                continue;

            if (actor.TexturePath == null)
            {
                if (CinematicImageLoader.TryCreatePleoOutputMaterializedImage(actor, fileSystem, materials, logger, out MaterializedCinematicImage? pleoImage))
                {
                    images[actor.Key] = pleoImage;
                    continue;
                }

                List<MaterializedCinematicFxEmitter> fxOnlyEmitters = LoadFxEmitters(
                    actor,
                    fileSystem,
                    textureService,
                    atlasContainer,
                    materials,
                    looseAtlases,
                    logger);
                if (fxOnlyEmitters.Count > 0)
                {
                    Image<Bgra32> placeholder = new(1, 1);
                    images[actor.Key] = new MaterializedCinematicImage(
                        actor.Key,
                        "fxcontroller/placeholder",
                        placeholder,
                        RenderGeometry.NoAtlasQuad,
                        LoadMaterial(fileSystem, actor.MaterialPath, materials, logger)) with
                    {
                        FxEmitters = fxOnlyEmitters
                    };
                }

                continue;
            }

            IReadOnlyList<string> texturePaths = actor.TexturePaths.Count > 0
                ? actor.TexturePaths
                : [actor.TexturePath];
            if (!TryLoadMaterializedImage(
                actor.Key,
                actor.TexturePath,
                texturePaths,
                actor.MaterialPath,
                actor.ExplicitAtlasPath,
                actor.MeshPath,
                actor.AtlasIndex,
                actor.AtlasTextureSlot,
                actor.GeometryOverride ?? actor.AnimLightTemplate?.DefaultGeometry,
                fileSystem,
                textureService,
                atlasContainer,
                materials,
                looseAtlases,
                logger,
                out MaterializedCinematicImage? image))
            {
                continue;
            }

            List<MaterializedCinematicFxEmitter> fxEmitters = LoadFxEmitters(
                actor,
                fileSystem,
                textureService,
                atlasContainer,
                materials,
                looseAtlases,
                logger);
            images[actor.Key] = image with { FxEmitters = fxEmitters };
        }

        return images;
    }

    private static List<MaterializedCinematicFxEmitter> LoadFxEmitters(
        CinematicActor actor,
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        CinematicAtlasContainer atlasContainer,
        Dictionary<string, CinematicRenderMaterial> materials,
        Dictionary<string, CinematicAtlas?> looseAtlases,
        ILogger logger)
    {
        List<MaterializedCinematicFxEmitter> fxEmitters = [];
        if (actor.FxTemplate is not { } fxTemplate)
            return fxEmitters;

        foreach (CinematicFxEmitterTemplate emitter in fxTemplate.Emitters)
        {
            if (!TryLoadMaterializedImage(
                $"{actor.Key}#fx{emitter.Index}",
                emitter.TexturePath,
                [emitter.TexturePath],
                emitter.MaterialPath,
                explicitAtlasPath: null,
                meshPath: null,
                0,
                atlasTextureSlot: 0,
                geometryOverride: null,
                fileSystem,
                textureService,
                atlasContainer,
                materials,
                looseAtlases,
                logger,
                out MaterializedCinematicImage? emitterImage))
            {
                continue;
            }

            fxEmitters.Add(new MaterializedCinematicFxEmitter(emitter, emitterImage));
        }

        return fxEmitters;
    }

    private static bool TryCreatePleoOutputMaterializedImage(
        CinematicActor actor,
        JustDanceUbiArtFileSystem fileSystem,
        Dictionary<string, CinematicRenderMaterial> materials,
        ILogger logger,
        [NotNullWhen(true)] out MaterializedCinematicImage? image)
    {
        image = null;
        if (actor.VisualComponentTypeId != LegacyBinarySerializer.GetTypeId<CinematicPleoTextureGraphicComponentBinary>() ||
            string.IsNullOrWhiteSpace(actor.MaterialPath))
        {
            return false;
        }

        CinematicRenderMaterial material = LoadMaterial(fileSystem, actor.MaterialPath, materials, logger);
        Image<Bgra32> placeholder = new(1, 1);
        image = new MaterializedCinematicImage(
            actor.Key,
            "pleotexturedyn/mainchannel",
            placeholder,
            RenderGeometry.NoAtlasQuad,
            material);
        return true;
    }

    private static bool TryLoadMaterializedImage(
        string actorKey,
        string texturePath,
        IReadOnlyList<string> texturePaths,
        string? materialPath,
        string? explicitAtlasPath,
        string? meshPath,
        int atlasIndex,
        int atlasTextureSlot,
        RenderGeometry? geometryOverride,
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        CinematicAtlasContainer atlasContainer,
        Dictionary<string, CinematicRenderMaterial> materials,
        Dictionary<string, CinematicAtlas?> looseAtlases,
        ILogger logger,
        [NotNullWhen(true)] out MaterializedCinematicImage? image)
    {
        image = null;
        if (CinematicNames.IsDynamicPleoTexturePath(texturePath))
        {
            CinematicRenderMaterial dynamicMaterial = LoadMaterial(fileSystem, materialPath, materials, logger);
            RenderGeometry dynamicGeometry = geometryOverride ??
                (!string.IsNullOrWhiteSpace(meshPath) &&
                    TryLoadMeshGeometry(fileSystem, meshPath, logger, out RenderGeometry? meshGeometry)
                        ? meshGeometry
                        : RenderGeometry.NoAtlasQuad);
            Image<Bgra32> placeholder = new(1, 1);
            List<MaterializedCinematicTexture> dynamicTextures =
            [
                MaterializedCinematicTexture.Create(texturePath, placeholder)
            ];
            for (int index = 1; index < texturePaths.Count; index++)
            {
                string layerTexturePath = texturePaths[index];
                if (string.Equals(layerTexturePath, texturePath, StringComparison.OrdinalIgnoreCase) ||
                    CinematicNames.IsDynamicPleoTexturePath(layerTexturePath))
                {
                    dynamicTextures.Add(dynamicTextures[0]);
                    continue;
                }

                if (!TryLoadTexture(fileSystem, textureService, layerTexturePath, logger, out Image<Bgra32>? layerImage))
                    continue;

                dynamicTextures.Add(MaterializedCinematicTexture.Create(layerTexturePath, layerImage));
            }

            image = new MaterializedCinematicImage(
                actorKey,
                texturePath,
                placeholder,
                dynamicGeometry,
                dynamicMaterial,
                dynamicTextures);
            return true;
        }

        CinematicRenderMaterial material = LoadMaterial(fileSystem, materialPath, materials, logger);
        if (!TryLoadTexture(fileSystem, textureService, texturePath, logger, out Image<Bgra32>? baseImage))
            return false;

        List<MaterializedCinematicTexture> textures =
        [
            MaterializedCinematicTexture.Create(texturePath, baseImage)
        ];

        for (int index = 1; index < texturePaths.Count; index++)
        {
            string layerTexturePath = texturePaths[index];
            if (string.Equals(layerTexturePath, texturePath, StringComparison.OrdinalIgnoreCase))
            {
                textures.Add(textures[0]);
                continue;
            }

            if (!TryLoadTexture(fileSystem, textureService, layerTexturePath, logger, out Image<Bgra32>? layerImage))
                continue;

            textures.Add(MaterializedCinematicTexture.Create(layerTexturePath, layerImage));
        }

        string atlasTexturePath = CinematicImageLoader.GetAtlasTexturePath(texturePaths, texturePath, atlasTextureSlot);
        CinematicAtlas? atlas = null;
        RenderGeometry geometry;
        if (geometryOverride != null)
        {
            geometry = geometryOverride;
        }
        else if (!string.IsNullOrWhiteSpace(meshPath) &&
            TryLoadMeshGeometry(fileSystem, meshPath, logger, out RenderGeometry? meshGeometry))
        {
            geometry = meshGeometry;
        }
        else if (!TryCreateAtlasGeometry(
            atlasTexturePath,
            explicitAtlasPath,
            atlasIndex,
            fileSystem,
            atlasContainer,
            looseAtlases,
            logger,
            out geometry,
            out atlas,
            out string atlasPath))
        {
            if (!string.IsNullOrWhiteSpace(explicitAtlasPath))
            {
                throw new FileNotFoundException(
                    $"The cinematic material for actor '{actorKey}' explicitly references atlas '{atlasPath}', but that atlas was not found in any loaded atlascontainer or loose atlas file.",
                    atlasPath);
            }

            geometry = RenderGeometry.NoAtlasQuad;
            logger.LogDebug(
                "Cinematic atlas '{AtlasPath}' was not found for texture '{TexturePath}', using no-atlas quad geometry.",
                atlasPath,
                texturePath);
        }

        image = new MaterializedCinematicImage(
            actorKey,
            texturePath,
            baseImage,
            geometry,
            material,
            textures,
            atlas);
        return true;
    }

    private static string GetAtlasTexturePath(
        IReadOnlyList<string> texturePaths,
        string texturePath,
        int atlasTextureSlot)
    {
        if (atlasTextureSlot >= 0 &&
            atlasTextureSlot < texturePaths.Count &&
            !string.IsNullOrWhiteSpace(texturePaths[atlasTextureSlot]))
        {
            return texturePaths[atlasTextureSlot];
        }

        return texturePath;
    }

    private static bool TryLoadMeshGeometry(
        JustDanceUbiArtFileSystem fileSystem,
        string meshPath,
        ILogger logger,
        [NotNullWhen(true)] out RenderGeometry? geometry)
    {
        geometry = null;
        string normalized = CinematicNames.NormalizePath(meshPath);
        if (!TryGetCookedFile(fileSystem, normalized, out CookedFile? meshFile))
        {
            logger.LogDebug("Cinematic Mesh3D '{MeshPath}' was not found.", normalized);
            return false;
        }

        try
        {
            using Stream stream = fileSystem.GetFileStream(meshFile);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            geometry = CinematicMesh3DReader.Read(memory.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Cinematic Mesh3D '{MeshPath}' could not be decoded.", normalized);
            return false;
        }
    }

    private static bool TryCreateAtlasGeometry(
        string texturePath,
        string? explicitAtlasPath,
        int atlasIndex,
        JustDanceUbiArtFileSystem fileSystem,
        CinematicAtlasContainer atlasContainer,
        Dictionary<string, CinematicAtlas?> looseAtlases,
        ILogger logger,
        out RenderGeometry geometry,
        [NotNullWhen(true)] out CinematicAtlas? atlas,
        out string atlasPath)
    {
        atlasPath = string.IsNullOrWhiteSpace(explicitAtlasPath)
            ? CinematicAtlasContainer.GetAtlasPathForTexture(texturePath)
            : CinematicPath.Normalize(explicitAtlasPath);
        if (!string.IsNullOrWhiteSpace(explicitAtlasPath))
        {
            if (atlasContainer.TryGetAtlas(atlasPath, out atlas) &&
                CinematicAtlasContainer.TryCreateGeometry(atlas, atlasIndex, out geometry))
            {
                return true;
            }

            if (TryLoadLooseAtlas(fileSystem, atlasPath, looseAtlases, logger, out atlas) &&
                CinematicAtlasContainer.TryCreateGeometry(atlas, atlasIndex, out geometry))
            {
                return true;
            }
        }

        atlasPath = CinematicAtlasContainer.GetAtlasPathForTexture(texturePath);
        if (atlasContainer.TryGetAtlasForTexture(texturePath, out atlas, out atlasPath) &&
            CinematicAtlasContainer.TryCreateGeometry(atlas, atlasIndex, out geometry))
        {
            return true;
        }

        if (TryLoadLooseAtlas(fileSystem, atlasPath, looseAtlases, logger, out atlas) &&
            CinematicAtlasContainer.TryCreateGeometry(atlas, atlasIndex, out geometry))
        {
            return true;
        }

        atlas = null;
        geometry = RenderGeometry.NoAtlasQuad;
        return false;
    }

    private static bool TryLoadLooseAtlas(
        JustDanceUbiArtFileSystem fileSystem,
        string atlasPath,
        Dictionary<string, CinematicAtlas?> looseAtlases,
        ILogger logger,
        [NotNullWhen(true)] out CinematicAtlas? atlas)
    {
        atlas = null;
        string normalizedAtlasPath = CinematicPath.Normalize(atlasPath);
        if (looseAtlases.TryGetValue(normalizedAtlasPath, out CinematicAtlas? cachedAtlas))
        {
            atlas = cachedAtlas;
            return atlas != null;
        }

        if (!TryGetCookedFile(fileSystem, normalizedAtlasPath, out CookedFile? atlasFile))
        {
            looseAtlases[normalizedAtlasPath] = null;
            return false;
        }

        using Stream stream = fileSystem.GetFileStream(atlasFile);
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        byte[] bytes = memory.ToArray();

        if (!CinematicAtlasLoader.TryReadLooseAtlas(bytes, out atlas))
        {
            looseAtlases[normalizedAtlasPath] = null;
            logger.LogDebug("Cinematic loose atlas '{AtlasPath}' could not be decoded.", normalizedAtlasPath);
            return false;
        }

        looseAtlases[normalizedAtlasPath] = atlas;
        logger.LogDebug("Loaded cinematic loose atlas '{AtlasPath}'.", normalizedAtlasPath);
        return true;
    }

    private static bool TryLoadTexture(
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        string texturePath,
        ILogger logger,
        [NotNullWhen(true)] out Image<Bgra32>? image)
    {
        image = null;
        if (!TryGetTextureFile(fileSystem, texturePath, out CookedFile? textureFile))
        {
            logger.LogDebug("Cinematic texture '{TexturePath}' was not found.", texturePath);
            return false;
        }

        using Stream stream = fileSystem.GetFileStream(textureFile);
        image = textureService.ConvertToImage(stream);
        if (image != null)
            return true;

        logger.LogDebug("Cinematic texture '{TexturePath}' could not be decoded.", texturePath);
        return false;
    }

    private static CinematicRenderMaterial LoadMaterial(
        JustDanceUbiArtFileSystem fileSystem,
        string? materialPath,
        Dictionary<string, CinematicRenderMaterial> cache,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return CinematicRenderMaterial.Empty;

        string normalized = CinematicNames.NormalizePath(materialPath);
        if (cache.TryGetValue(normalized, out CinematicRenderMaterial? material))
            return material;

        if (!TryGetCookedFile(fileSystem, normalized, out CookedFile? materialFile))
        {
            if (CinematicMaterialReader.TryCreateShaderFallback(normalized, out material))
            {
                material = CinematicMaterialReader.ApplyMaterialPathConventions(normalized, material);
                cache[normalized] = material;
                logger.LogDebug("Using cinematic shader fallback material for '{MaterialPath}'.", normalized);
                return material;
            }

            cache[normalized] = CinematicRenderMaterial.Empty;
            logger.LogDebug("Cinematic material '{MaterialPath}' was not found.", normalized);
            return CinematicRenderMaterial.Empty;
        }

        try
        {
            using Stream stream = fileSystem.GetFileStream(materialFile);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            material = CinematicMaterialReader.Read(memory.ToArray());
            material = CinematicMaterialReader.ApplyMaterialPathConventions(normalized, material);
            cache[normalized] = material;
            return material;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (CinematicMaterialReader.TryCreateShaderFallback(normalized, out material))
            {
                material = CinematicMaterialReader.ApplyMaterialPathConventions(normalized, material);
                cache[normalized] = material;
                logger.LogDebug(ex, "Using cinematic shader fallback material for unreadable '{MaterialPath}'.", normalized);
                return material;
            }

            cache[normalized] = CinematicRenderMaterial.Empty;
            logger.LogDebug(ex, "Cinematic material '{MaterialPath}' could not be decoded.", normalized);
            return CinematicRenderMaterial.Empty;
        }
    }

    private static bool TryGetTextureFile(
        JustDanceUbiArtFileSystem fileSystem,
        string texturePath,
        [NotNullWhen(true)]
        out CookedFile? textureFile)
    {
        return TryGetCookedFile(fileSystem, texturePath, out textureFile);
    }

    private static bool TryGetCookedFile(
        JustDanceUbiArtFileSystem fileSystem,
        string path,
        [NotNullWhen(true)]
        out CookedFile? textureFile)
    {
        if (fileSystem.GetFilePath(path, out textureFile))
            return true;

        if (!path.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) &&
            fileSystem.GetFilePath($"{path}.ckd", out textureFile))
        {
            return true;
        }

        textureFile = null;
        return false;
    }
}