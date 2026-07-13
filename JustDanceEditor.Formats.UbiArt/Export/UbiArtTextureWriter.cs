using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Export.Generators;
using JustDanceEditor.Formats.UbiArt.Import;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Export;

internal sealed class UbiArtTextureWriter(ILogger logger)
{
    public async Task GenerateColorsAsync(UbiArtExportPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.MaterializedRoot) ||
            plan.Package.Metadata.AdditionalMetadata.ContainsKey("songcolor_1a"))
        {
            return;
        }

        try
        {
            IntermediateImageService imageService = new(plan.MaterializedRoot, plan.Package, plan.Context.IO);
            using Image<Bgra32> image = await imageService.GetMapBackgroundAsync(width: 2048, height: 1024);
            ColorThemeGenerator.SongTheme theme = ColorThemeGenerator.GenerateFromImage(image);

            plan.Package.Metadata.AdditionalMetadata["songcolor_1a"] = theme.Color1A.ToHex();
            plan.Package.Metadata.AdditionalMetadata["songcolor_1b"] = theme.Color1B.ToHex();
            plan.Package.Metadata.AdditionalMetadata["songcolor_2a"] = theme.Color2A.ToHex();
            plan.Package.Metadata.AdditionalMetadata["songcolor_2b"] = theme.Color2B.ToHex();

            logger.LogInformation(
                "Generated song colors: 1A={Color1A}, 1B={Color1B}, 2A={Color2A}, 2B={Color2B}",
                theme.Color1A.ToHex(),
                theme.Color1B.ToHex(),
                theme.Color2A.ToHex(),
                theme.Color2B.ToHex());
        }
        catch (Exception ex)
        {
            logger.LogWarning("Failed to generate song colors from map background: {Message}", ex.Message);
        }
    }

    public async Task WriteAsync(UbiArtExportPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.MaterializedRoot))
            return;

        string pictogramsFolder = Path.Combine(
            plan.PlatformRoot,
            plan.Layout.GetPictosFolder("", plan.MapNameLower, plan.Platform, plan.EngineVersion));
        string menuTexturesFolder = Path.Combine(plan.MapWorldBase, "menuart", "textures");
        string menuActorsFolder = Path.Combine(plan.MapWorldBase, "menuart", "actors");
        bool isLegacyFormat = plan.EngineGenerator is LegacyEngineContentGenerator;
        bool isUncooked = plan.Platform == UbiArtPlatform.Uncooked;
        IntermediateImageService imageService = new(plan.MaterializedRoot, plan.Package, plan.Context.IO);

        string pictogramsSource = plan.Context.IO.Combine(plan.MaterializedRoot, "assets", "pictograms");
        if (plan.Context.IO.DirectoryExists(pictogramsSource))
        {
            string extension = plan.EngineVersion == UbiArtEngineVersion.JD2014 ? ".tga" : ".png";
            await Parallel.ForEachAsync(plan.Context.IO.GetFiles(pictogramsSource), async (file, cancellationToken) =>
            {
                using Image<Bgra32> image = await Image.LoadAsync<Bgra32>(file, cancellationToken);
                string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                await plan.PlatformExporter.WriteTextureAsync(plan.Context, Path.Combine(pictogramsFolder, $"{name}{extension}"), image);
            });
        }

        await Parallel.ForEachAsync(Enumerable.Range(1, plan.Package.Metadata.CoachCount), async (coachIndex, cancellationToken) =>
        {
            using Image<Bgra32> coach = await imageService.GetCoachAsync(coachIndex, width: 1024, height: 1024, useFadeEffect: true, cancellationToken: cancellationToken);
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_coach_{coachIndex}", coach, menuTexturesFolder, menuActorsFolder);

            if (isUncooked)
            {
                using Image<Bgra32> phoneCoach = await imageService.GetCoachAsync(coachIndex, width: 256, height: 256, useFadeEffect: true, cancellationToken: cancellationToken);
                await plan.PlatformExporter.WriteTextureAsync(plan.Context, Path.Combine(menuTexturesFolder, $"{plan.MapNameLower}_coach_{coachIndex}_phone.png"), phoneCoach);
            }
        });

        if (isUncooked || ShouldWriteMenuArtTexture($"{plan.MapNameLower}_cover_generic", plan, isLegacyFormat))
        {
            int coverSize = isUncooked ? 1024 : 512;
            using Image<Bgra32> cover = await imageService.GetSquareCoverAsync(width: coverSize, height: coverSize);
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_cover_generic", cover, menuTexturesFolder, menuActorsFolder);
        }

        if (isUncooked || ShouldWriteMenuArtTexture($"{plan.MapNameLower}_cover_online", plan, isLegacyFormat))
        {
            using Image<Bgra32> cover = await imageService.GetSquareCoverAsync(width: 256, height: 256);
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_cover_online", cover, menuTexturesFolder, menuActorsFolder);
        }

        if (isUncooked || ShouldWriteMenuArtTexture($"{plan.MapNameLower}_cover_online_kids", plan, isLegacyFormat))
        {
            using Image<Bgra32> cover = await imageService.GetSquareCoverAsync(width: 256, height: 256);
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_cover_online_kids", cover, menuTexturesFolder, menuActorsFolder);
        }

        if (isUncooked)
        {
            using Image<Bgra32> phoneCover = await imageService.GetSquareCoverAsync(width: 256, height: 256);
            await plan.PlatformExporter.WriteTextureAsync(plan.Context, Path.Combine(menuTexturesFolder, $"{plan.MapNameLower}_cover_phone.png"), phoneCover);
        }

        if (isUncooked || ShouldWriteMenuArtTexture($"{plan.MapNameLower}_map_bkg", plan, isLegacyFormat))
        {
            using Image<Bgra32> background = await imageService.GetMapBackgroundAsync(width: 2048, height: 1024);
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_map_bkg", background, menuTexturesFolder, menuActorsFolder);

            if (isUncooked)
            {
                string graphTemplatePath = Path.Combine(plan.PlatformRoot, "world", "_common", "graphic_component_templates", "graph", "textures", "background.png");
                await plan.PlatformExporter.WriteTextureAsync(plan.Context, graphTemplatePath, background);
            }
        }

        using (Image<Bgra32> albumBackground = await imageService.GetAlbumBackgroundAsync(width: 256, height: 256))
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_cover_albumbkg", albumBackground, menuTexturesFolder, menuActorsFolder);

        if (isUncooked || ShouldWriteMenuArtTexture($"{plan.MapNameLower}_banner_bkg", plan, isLegacyFormat))
        {
            using Image<Bgra32> banner = await imageService.GetBannerAsync(width: 1024, height: 512);
            await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_banner_bkg", banner, menuTexturesFolder, menuActorsFolder);
        }

        using Image<Bgra32> albumCoach = await imageService.GetAlbumCoachAsync(width: 1024, height: 1024);
        await WriteMenuArtTextureAsync(plan, $"{plan.MapNameLower}_cover_albumcoach", albumCoach, menuTexturesFolder, menuActorsFolder);
    }

    private static async Task WriteMenuArtTextureAsync(
        UbiArtExportPlan plan,
        string textureName,
        Image<Bgra32> image,
        string texturesFolder,
        string actorsFolder)
    {
        await plan.PlatformExporter.WriteTextureAsync(plan.Context, Path.Combine(texturesFolder, $"{textureName}.tga"), image);
        object actor = plan.EngineGenerator.GenerateMenuArtActor(textureName, plan.MapName);
        string actorPath = Path.Combine(actorsFolder, $"{textureName}.act");
        if (plan.Platform == UbiArtPlatform.Uncooked)
            await plan.PlatformExporter.WriteEngineResourceAsync(plan.Context, actorPath, actor);
        else
            await plan.PlatformExporter.WriteBinaryFileAsync(plan.Context, actorPath, actor);
    }

    private static bool ShouldWriteMenuArtTexture(string textureName, UbiArtExportPlan plan, bool isLegacyFormat)
    {
        if (!isLegacyFormat)
            return !textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase) || plan.EngineVersion >= UbiArtEngineVersion.JD2020;

        if (plan.EngineVersion == UbiArtEngineVersion.JD2014 &&
            (textureName.EndsWith("_cover_generic", StringComparison.OrdinalIgnoreCase) ||
             textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (textureName.EndsWith("_banner_bkg", StringComparison.OrdinalIgnoreCase) ||
            textureName.EndsWith("_cover_online", StringComparison.OrdinalIgnoreCase) ||
            textureName.EndsWith("_cover_online_kids", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !textureName.EndsWith("_map_bkg", StringComparison.OrdinalIgnoreCase) || plan.Platform == UbiArtPlatform.Revolution;
    }
}
