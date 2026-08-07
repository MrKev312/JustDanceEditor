using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Model;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class BrandingAssetImporter
{
    public static void Import(
        ConversionContext context,
        string packageRoot,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        IntermediateAssetPaths.EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder, io);
        IntermediateAssetPaths.EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.BackgroundsFolder, io);

        Parallel.Invoke(
            () => ExportCover(context, IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.CoverFile), logger, textureService, io),
            () => ExportSquareCover(context, IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.SquareCoverFile), logger, textureService, io),
            () => ExportAlbumCoach(context, IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.AlbumCoachFile), logger, textureService, io),
            () => ExportBanner(context, IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.BannerFile), logger, textureService, io),
            () => ExportMapBackground(context, IntermediateAssetPaths.Resolve(packageRoot, IntermediatePackageLayout.Assets.MapBackgroundFile), logger, textureService, io));
    }

    private static void ExportCover(
        ConversionContext context,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        logger.LogDebug("Attempting to export cover image from menu art folder: {MenuArtFolder}", context.FileSystem.InputFolders.MenuArtFolder);
        CookedFile? cover = context.FileSystem.AssetResolver?.GetCoverArt();
        if (cover != null)
        {
            try
            {
                using Stream stream = context.FileSystem.GetFileStream(cover);
                using Image<Bgra32>? image = textureService.ConvertToImage(stream);
                if (image != null && image.Width >= image.Height * 1.33)
                {
                    image.Mutate(operation => operation.Resize(640, 360));
                    IntermediateAssetPaths.SaveAsWebp(image, destination, io);
                    logger.LogInformation("Saved existing cover image: {FileName}", Path.GetFileName(cover.RelativePath));
                    return;
                }

                logger.LogDebug("Cover art found but has incorrect aspect ratio, will be generated from background");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load existing cover art");
            }
        }

        logger.LogDebug("Cover art not found in MenuArt, will be generated from background and album coach by JDI services");
    }

    private static void ExportSquareCover(
        ConversionContext context,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        string songName = GetAssetSongName(context);
        CookedFile? cover = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{songName}_cover_generic.*").FirstOrDefault();
        cover ??= context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{songName}_cover_online.*").FirstOrDefault();
        if (TryImportImage(context, cover, destination, "square cover", logger, textureService, io))
            return;

        logger.LogDebug("Square cover not found in MenuArt, will be generated from cover if needed");
    }

    private static void ExportAlbumCoach(
        ConversionContext context,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        if (TryImportImage(context, context.FileSystem.AssetResolver?.GetAlbumCoach(), destination, "album coach", logger, textureService, io))
            return;

        logger.LogDebug("Album coach not found in MenuArt, will be generated from individual coaches if available");
    }

    private static void ExportBanner(
        ConversionContext context,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        string songName = GetAssetSongName(context);
        CookedFile? banner = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{songName}_banner_bkg.*").FirstOrDefault();
        if (TryImportImage(context, banner, destination, "banner", logger, textureService, io))
            return;

        logger.LogDebug("Banner not found in MenuArt, will be generated from map background if available");
    }

    private static void ExportMapBackground(
        ConversionContext context,
        string destination,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        string songName = GetAssetSongName(context);
        CookedFile? background = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{songName}_map_bkg.*").FirstOrDefault();
        if (TryImportImage(context, background, destination, "map background", logger, textureService, io))
            return;

        logger.LogDebug("Map background not found in MenuArt");
    }

    private static bool TryImportImage(
        ConversionContext context,
        CookedFile? source,
        string destination,
        string assetName,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io)
    {
        if (source == null)
            return false;

        try
        {
            using Stream stream = context.FileSystem.GetFileStream(source);
            using Image<Bgra32>? image = textureService.ConvertToImage(stream);
            if (image == null)
                return false;

            IntermediateAssetPaths.SaveAsWebp(image, destination, io);
            logger.LogInformation("Imported {AssetName} image from MenuArt", assetName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to import {AssetName} from MenuArt", assetName);
            return false;
        }
    }

    private static string GetAssetSongName(ConversionContext context)
    {
        JDUbiArtSong song = context.SongData ?? throw new ArgumentException("SongData cannot be null.", nameof(context));
        return song.LegacyMashup?.BaseSongName ?? song.Name;
    }
}
