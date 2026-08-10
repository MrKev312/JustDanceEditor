using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;

using KevInc.UbiArt.Cinematics.Core;
using KevInc.UbiArt.Cinematics.Materials;

using Microsoft.Extensions.Logging;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Materials;

internal static class CinematicImageLoader
{
    public static Task<Dictionary<string, MaterializedCinematicImage>> LoadActorImagesAsync(
        CinematicScene scene,
        JustDanceUbiArtFileSystem fileSystem,
        ITextureService textureService,
        ILogger logger)
    {
        CinematicAtlasContainer atlasContainer = CinematicAtlasLoader.LoadDefault(fileSystem, logger);
        Dictionary<string, MaterializedCinematicImage> images = CinematicImageMaterializer.LoadActorImages(
            scene,
            fileSystem,
            textureService.ConvertToImage,
            atlasContainer,
            logger);
        return Task.FromResult(images);
    }
}
