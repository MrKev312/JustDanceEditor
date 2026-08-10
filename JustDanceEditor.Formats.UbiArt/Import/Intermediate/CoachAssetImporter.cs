using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import.Core;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class CoachAssetImporter
{
    public static void Import(
        ConversionContext context,
        string packageRoot,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        CookedFile[] files = context.FileSystem.AssetResolver?.GetCoachTextures() ?? [];
        if (files.Length == 0)
        {
            logger.LogInformation("No coach files found in {MenuArtFolder}", context.FileSystem.InputFolders.MenuArtFolder);
            return;
        }

        logger.LogInformation("Found {Count} coach file(s) to process", files.Length);
        Parallel.For(0, files.Length, index => ImportCoach(context, files[index], index, packageRoot, logger, textureService, io));
    }

    private static void ImportCoach(
        ConversionContext context,
        CookedFile source,
        int index,
        string packageRoot,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        string destination = io.Combine(
            IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder),
            $"coach_{index + 1:D2}.webp");
        try
        {
            using Stream stream = context.FileSystem.GetFileStream(source);
            using Image<Bgra32>? coach = textureService.ConvertToImage(stream);
            if (coach == null)
            {
                logger.LogWarning("Failed to convert coach image: {Path} (texture decoder returned no image)", source.RelativePath);
                return;
            }

            IntermediateAssetPaths.SaveAsWebp(coach, destination, io);
            logger.LogDebug("Saved coach asset: {FileName}", Path.GetFileName(destination));
        }
        catch (FileNotFoundException)
        {
            logger.LogWarning("Failed to convert coach image: {Path} (not found)", source.RelativePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to convert coach image: {Path}: {Message}", source.RelativePath, ex.Message);
        }
    }
}
