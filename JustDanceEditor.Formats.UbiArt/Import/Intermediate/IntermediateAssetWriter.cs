using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.AssetExtraction;
using JustDanceEditor.Formats.UbiArt.Import.Audio;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Model;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class IntermediateAssetWriter
{
    static ImageEncoder Encoder => JDI.Utilities.WebpSettings.LosslessWebpEncoder;

    public static async Task PopulateFromUbiArtAsync(ConversionContext context, IntermediateSongPackage package, string packageRoot, ILogger logger, ITextureService textureService, IFileSystem? io = null)
    {
        IFileSystem iofs = io ?? new SystemFileSystem();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        context.IntermediatePackage ??= package;

        ResetAssetsRoot(packageRoot, iofs);

        await EnsurePrerequisitesAsync(iofs);

        JDUbiArtSong songData = context.SongData ?? throw new InvalidOperationException("Song data not loaded.");
        string audioMasterFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder, iofs);
        string audioPreviewFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder, iofs);
        string videoFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder, iofs);

        // Parallelize pictogram conversion, audio conversion, and video copy
        Task pictoTask = ConvertPictogramsAsync(context, packageRoot, logger, textureService, iofs);
        Task audioTask = AudioConverter.ConvertAudioAsync(songData, context.FileSystem, new AudioConversionOptions
        {
            MasterOutputFolder = audioMasterFolder,
            PreviewOutputFolder = audioPreviewFolder,
        }, logger);
        Task videoTask = Task.Run(() => CopyMasterVideo(context.FileSystem, videoFolder, logger, iofs));

        await Task.WhenAll(pictoTask, audioTask, videoTask);

        string previewVideoFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);
        TryDeleteDirectory(previewVideoFolder, logger, iofs);

        await CopyAssetsToPackageAsync(context, packageRoot, logger, textureService, iofs);
    }

    private static async Task EnsurePrerequisitesAsync(IFileSystem io)
    {
        if (!io.FileExists("ffmpeg.exe") && !io.FileExists("ffmpeg"))
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
    }

    private static async Task ConvertPictogramsAsync(ConversionContext context, string packageRoot, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        CookedFile[] pictoFiles = context.FileSystem.AssetResolver?.GetPictograms() ?? [];
        string[] pictoPaths = [.. pictoFiles.Select(file => (string)file)];

        string outputFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder, io);

        UbiArtPictoConversionRequest request = new(
            context.SongData,
            pictoFiles,
            outputFolder);

        await Task.Run(() => UbiArtPictoConverter.Convert(request, logger, textureService, context.FileSystem, io));
    }

    private static async Task CopyAssetsToPackageAsync(ConversionContext context, string packageRoot, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        // Parallelize branding, coach, and motion asset operations
        Task brandingTask = Task.Run(() => AttachBrandingAssets(context, packageRoot, logger, textureService, io));
        Task coachTask = Task.Run(() => AttachCoachAssets(context, packageRoot, logger, textureService, io));
        Task motionTask = Task.Run(() => AttachMotionAssets(context, packageRoot, logger, io));

        await Task.WhenAll(brandingTask, coachTask, motionTask);
    }

    private static void AttachBrandingAssets(ConversionContext context, string packageRoot, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder, io);
        EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.BackgroundsFolder, io);

        Parallel.Invoke(
            () => ExportCoverImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoverFile), logger, textureService, io),
            () => ExportSquareCoverImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.SquareCoverFile), logger, textureService, io),
            () => ExportAlbumCoachImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.AlbumCoachFile), logger, textureService, io),
            () => ExportBannerImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.BannerFile), logger, textureService, io),
            () => ExportMapBackgroundImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.MapBackgroundFile), logger, textureService, io)
        );
    }

    private static string? ExportCoverImage(ConversionContext context, string destination, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        logger.LogDebug("Attempting to export cover image from menu art folder: {MenuArtFolder}", context.FileSystem.InputFolders.MenuArtFolder);

        JDUbiArtSong song = context.SongData ?? throw new ArgumentNullException(nameof(context.SongData));

        // Look for existing cover art in MenuArt folder
        CookedFile? cover = context.FileSystem.AssetResolver?.GetCoverArt();
        if (cover != null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(cover);
                Image<Bgra32>? image = textureService.ConvertToImage(s);
                if (image != null)
                {
                    using (image)
                    {
                        // Check if it's a suitable cover format (16:9 aspect ratio or wider)
                        if (image.Width >= image.Height * 1.33)
                        {
                            // Resize to standard cover dimensions
                            image.Mutate(x => x.Resize(640, 360));

                            io.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException($"Could not determine the directory for '{destination}'."));
                            image.Save(destination, Encoder);
                            logger.LogInformation("Saved existing cover image: {FileName}", Path.GetFileName(cover.RelativePath));
                            return destination;
                        }

                        logger.LogDebug("Cover art found but has incorrect aspect ratio, will be generated from background");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load existing cover art");
            }
        }

        logger.LogDebug("Cover art not found in MenuArt, will be generated from background and album coach by JDI services");
        return null;
    }

    private static void ExportSquareCoverImage(ConversionContext context, string destination, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        JDUbiArtSong song = context.SongData ?? throw new ArgumentNullException(nameof(context.SongData));

        // Try to find square cover in MenuArt (prefer cover_generic as it's the highest resolution)
        CookedFile? squareCover = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_cover_generic.*")
            .FirstOrDefault();

        // Fall back to cover_online if cover_generic doesn't exist
        squareCover ??= context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_cover_online.*")
                .FirstOrDefault();

        if (squareCover != null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(squareCover);
                using Image<Bgra32>? img = textureService.ConvertToImage(s);
                if (img != null)
                {
                    SaveAsWebp(img, destination, io);
                    logger.LogInformation("Imported square cover image from MenuArt");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to import square cover from MenuArt");
            }
        }

        logger.LogDebug("Square cover not found in MenuArt, will be generated from cover if needed");
    }

    private static void ExportAlbumCoachImage(ConversionContext context, string destination, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        JDUbiArtSong song = context.SongData ?? throw new ArgumentNullException(nameof(context.SongData));

        // Try to find album coach in MenuArt
        CookedFile? albumCoach = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_cover_albumcoach.*")
            .FirstOrDefault();

        if (albumCoach != null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(albumCoach);
                using Image<Bgra32>? img = textureService.ConvertToImage(s);
                if (img != null)
                {
                    SaveAsWebp(img, destination, io);
                    logger.LogInformation("Imported album coach image from MenuArt");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to import album coach from MenuArt");
            }
        }

        logger.LogDebug("Album coach not found in MenuArt, will be generated from individual coaches if available");
    }

    private static void ExportBannerImage(ConversionContext context, string destination, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        JDUbiArtSong song = context.SongData ?? throw new ArgumentNullException(nameof(context.SongData));

        // Try to find banner in MenuArt
        CookedFile? banner = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_banner_bkg.*")
            .FirstOrDefault();

        if (banner != null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(banner);
                using Image<Bgra32>? img = textureService.ConvertToImage(s);
                if (img != null)
                {
                    SaveAsWebp(img, destination, io);
                    logger.LogInformation("Imported banner image from MenuArt");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to import banner from MenuArt");
            }
        }

        logger.LogDebug("Banner not found in MenuArt, will be generated from map background if available");
    }

    private static void ExportMapBackgroundImage(ConversionContext context, string destination, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        JDUbiArtSong song = context.SongData ?? throw new ArgumentNullException(nameof(context.SongData));

        // Try to find map background in MenuArt
        CookedFile? mapBkg = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{song.Name}_map_bkg.*")
            .FirstOrDefault();

        if (mapBkg != null)
        {
            try
            {
                using Stream s = context.FileSystem.GetFileStream(mapBkg);
                Image<Bgra32>? img = textureService.ConvertToImage(s);
                if (img != null)
                {
                    using (img)
                    {
                        SaveAsWebp(img, destination, io);
                    }

                    logger.LogInformation("Imported map background image from MenuArt");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to import map background from MenuArt");
            }
        }

        logger.LogDebug("Map background not found in MenuArt");
    }

    private static void AttachCoachAssets(ConversionContext context, string packageRoot, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        CookedFile[] coachFilesCooked = context.FileSystem.AssetResolver?.GetCoachTextures() ?? [];

        if (coachFilesCooked.Length == 0)
        {
            logger.LogInformation("No coach files found in {MenuArtFolder}", context.FileSystem.InputFolders.MenuArtFolder);
            return;
        }

        logger.LogInformation("Found {Count} coach file(s) to process", coachFilesCooked.Length);
        Parallel.For(0, coachFilesCooked.Length, i =>
        {
            string destination = io.Combine(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder), $"coach_{i + 1:D2}.webp");
            try
            {
                using Stream s = context.FileSystem.GetFileStream(coachFilesCooked[i]);
                using Image<Bgra32>? coach = textureService.ConvertToImage(s);
                if (coach is null)
                {
                    logger.LogWarning("Failed to convert coach image: {Path}", coachFilesCooked[i].RelativePath);
                    return;
                }

                SaveAsWebp(coach, destination, io);
                logger.LogDebug("Saved coach asset: {FileName}", Path.GetFileName(destination));
            }
            catch (FileNotFoundException)
            {
                logger.LogWarning("Failed to convert coach image: {Path} (not found)", coachFilesCooked[i].RelativePath);
            }
        });
    }

    private static void AttachMotionAssets(ConversionContext context, string packageRoot, ILogger logger, IFileSystem io)
    {
        string movesFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.MovesFolder);
        string gesturesFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.GesturesFolder);

        CopyCookedFiles(context, context.FileSystem.InputFolders.MovesFolder, "*.msm", movesFolder, io);
        CopyCookedFiles(context, context.FileSystem.InputFolders.MovesFolder, "*.gesture", gesturesFolder, io);

        string gesturesRelative = context.FileSystem.InputFolders.TimelineFolder + "/gestures";
        CopyCookedFiles(context, gesturesRelative, "*.gesture", gesturesFolder, io);
    }

    private static string? CopyCookedFiles(ConversionContext context, string relativeFolder, string pattern, string destinationFolder, IFileSystem io)
    {
        CookedFile[] files;
        try
        {
            files = context.FileSystem.GetAllFiles(relativeFolder, pattern);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        if (files.Length == 0)
            return null;

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        (CookedFile File, string Name)[] uniqueFiles = [.. files.Select(file => (File: file, Name: $"{file.Name}{file.Extension}")).Where(item => seen.Add(item.Name))];

        io.CreateDirectory(destinationFolder);
        Parallel.ForEach(uniqueFiles, item =>
        {
            string destination = io.Combine(destinationFolder, item.Name);
            try
            {
                using Stream sourceStream = context.FileSystem.GetFileStream(item.File);
                using FileStream destStream = File.Open(destination, FileMode.Create, FileAccess.Write);
                sourceStream.CopyTo(destStream);
            }
            catch (FileNotFoundException)
            {
                // Skip missing files
            }
        });

        return destinationFolder;
    }

    private static void ResetAssetsRoot(string packageRoot, IFileSystem io)
    {
        string assetsRoot = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.Root);
        if (io.DirectoryExists(assetsRoot))
            io.DeleteDirectory(assetsRoot, true);
        io.CreateDirectory(assetsRoot);
    }

    private static void CopyMasterVideo(LayeredFileSystem fileSystem, string destinationFolder, ILogger logger, IFileSystem io)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        CookedFile? sourceFile = GetVideoFile(fileSystem);
        if (sourceFile == null)
        {
            logger.LogWarning("No video file found in UbiArt input; skipping video copy.");
            return;
        }

        io.CreateDirectory(destinationFolder);
        string destination = io.Combine(destinationFolder, Path.GetFileName(sourceFile.RelativePath));
        try
        {
            using Stream src = fileSystem.GetFileStream(sourceFile);
            using FileStream dest = File.Open(destination, FileMode.Create, FileAccess.Write);
            src.CopyTo(dest);
            logger.LogInformation("Copied master video into intermediate package without conversion.");
        }
        catch (FileNotFoundException)
        {
            logger.LogWarning("Video file not found, skipping.");
        }
    }

    private static CookedFile? GetVideoFile(LayeredFileSystem fileSystem)
    {
        if (fileSystem.GetFolderPath(fileSystem.InputFolders.MediaFolder, out string? mediaFolder))
        {
            CookedFile[] mediaVideos = fileSystem.GetAllFiles(fileSystem.InputFolders.MediaFolder, "*.webm");
            if (mediaVideos.Length > 0)
                return mediaVideos[0];
        }

        string videosCoachFolder = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "videoscoach");
        CookedFile[] coachVideos = [.. fileSystem.GetAllFiles(videosCoachFolder, "*.webm")];

        if (coachVideos.Length > 0)
            return coachVideos[0];

        return null;
    }

    private static string EnsureFolder(string packageRoot, string relativeFolder, IFileSystem io)
    {
        string path = ResolvePackagePath(packageRoot, relativeFolder);
        io.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string? path, ILogger logger, IFileSystem io)
    {
        if (string.IsNullOrWhiteSpace(path) || !io.DirectoryExists(path))
            return;

        try
        {
            io.DeleteDirectory(path, true);
        }
        catch (IOException ex)
        {
            logger.LogWarning("Failed to delete directory '{Path}': {Message}", path, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning("Failed to delete directory '{Path}': {Message}", path, ex.Message);
        }
    }

    private static string ResolvePackagePath(string packageRoot, string relative)
    {
        return IntermediatePackageLayout.Resolve(packageRoot, relative);
    }

    private static void SaveAsWebp(Image<Bgra32> image, string destination, IFileSystem? io = null)
    {
        IFileSystem fs = io ?? new SystemFileSystem();
        fs.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException($"Could not determine the directory for '{destination}'."));
        image.Save(destination, Encoder);
    }
}
