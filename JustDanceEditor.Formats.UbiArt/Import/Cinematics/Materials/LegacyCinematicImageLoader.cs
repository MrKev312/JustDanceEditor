using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Diagnostics.CodeAnalysis;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class LegacyCinematicImageLoader
{
    public static async Task<Dictionary<string, MaterializedCinematicImage>> LoadActorImagesAsync(
        LegacyCinematicScene scene,
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        ILogger logger)
    {
        Dictionary<string, MaterializedCinematicImage> images = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, LegacyCinematicMaterial> materials = new(StringComparer.OrdinalIgnoreCase);
        LegacyCinematicAtlasContainer atlasContainer = LegacyCinematicAtlasContainer.LoadDefault(fileSystem, logger);
        foreach (LegacyCinematicActor actor in scene.Actors)
        {
            if (actor.TexturePath == null || images.ContainsKey(actor.Key))
                continue;

            IReadOnlyList<string> texturePaths = actor.TexturePaths.Count > 0
                ? actor.TexturePaths
                : [actor.TexturePath];
            if (!TryLoadMaterializedImage(
                actor.Key,
                actor.TexturePath,
                texturePaths,
                actor.MaterialPath,
                actor.AtlasIndex,
                fileSystem,
                textureService,
                atlasContainer,
                materials,
                logger,
                out MaterializedCinematicImage? image))
            {
                continue;
            }

            List<MaterializedCinematicFxEmitter> fxEmitters = [];
            if (actor.FxTemplate is { } fxTemplate)
            {
                foreach (LegacyCinematicFxEmitterTemplate emitter in fxTemplate.Emitters)
                {
                    if (!TryLoadMaterializedImage(
                        $"{actor.Key}#fx{emitter.Index}",
                        emitter.TexturePath,
                        [emitter.TexturePath],
                        emitter.MaterialPath,
                        0,
                        fileSystem,
                        textureService,
                        atlasContainer,
                        materials,
                        logger,
                        out MaterializedCinematicImage? emitterImage))
                    {
                        continue;
                    }

                    fxEmitters.Add(new MaterializedCinematicFxEmitter(emitter, emitterImage));
                }
            }

            images[actor.Key] = image with { FxEmitters = fxEmitters };
        }

        return images;
    }

    private static bool TryLoadMaterializedImage(
        string actorKey,
        string texturePath,
        IReadOnlyList<string> texturePaths,
        string? materialPath,
        int atlasIndex,
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        LegacyCinematicAtlasContainer atlasContainer,
        Dictionary<string, LegacyCinematicMaterial> materials,
        ILogger logger,
        [NotNullWhen(true)] out MaterializedCinematicImage? image)
    {
        image = null;
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

        if (!atlasContainer.TryCreateGeometry(texturePath, atlasIndex, out RenderGeometry geometry, out string atlasPath))
        {
            logger.LogDebug(
                "Legacy cinematic atlas '{AtlasPath}' was not found for texture '{TexturePath}', using no-atlas quad geometry.",
                atlasPath,
                texturePath);
        }

        _ = atlasContainer.TryGetAtlasForTexture(texturePath, out LegacyCinematicAtlas? atlas, out _);
        LegacyCinematicMaterial material = LoadMaterial(fileSystem, materialPath, materials, logger);
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
            logger.LogDebug("Legacy cinematic texture '{TexturePath}' was not found.", texturePath);
            return false;
        }

        using Stream stream = fileSystem.GetFileStream(textureFile);
        image = textureService.ConvertToImage(stream);
        if (image != null)
            return true;

        logger.LogDebug("Legacy cinematic texture '{TexturePath}' could not be decoded.", texturePath);
        return false;
    }

    private static LegacyCinematicMaterial LoadMaterial(
        JustDanceUbiArtFileSystem fileSystem,
        string? materialPath,
        Dictionary<string, LegacyCinematicMaterial> cache,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return LegacyCinematicMaterial.Empty;

        string normalized = LegacyCinematicNames.NormalizePath(materialPath);
        if (cache.TryGetValue(normalized, out LegacyCinematicMaterial? material))
            return material;

        if (!TryGetCookedFile(fileSystem, normalized, out CookedFile? materialFile))
        {
            if (LegacyCinematicMaterialReader.TryCreateShaderFallback(normalized, out material))
            {
                cache[normalized] = material;
                logger.LogDebug("Using legacy cinematic shader fallback material for '{MaterialPath}'.", normalized);
                return material;
            }

            cache[normalized] = LegacyCinematicMaterial.Empty;
            logger.LogDebug("Legacy cinematic material '{MaterialPath}' was not found.", normalized);
            return LegacyCinematicMaterial.Empty;
        }

        try
        {
            using Stream stream = fileSystem.GetFileStream(materialFile);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            material = LegacyCinematicMaterialReader.Read(memory.ToArray());
            cache[normalized] = material;
            return material;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (LegacyCinematicMaterialReader.TryCreateShaderFallback(normalized, out material))
            {
                cache[normalized] = material;
                logger.LogDebug(ex, "Using legacy cinematic shader fallback material for unreadable '{MaterialPath}'.", normalized);
                return material;
            }

            cache[normalized] = LegacyCinematicMaterial.Empty;
            logger.LogDebug(ex, "Legacy cinematic material '{MaterialPath}' could not be decoded.", normalized);
            return LegacyCinematicMaterial.Empty;
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