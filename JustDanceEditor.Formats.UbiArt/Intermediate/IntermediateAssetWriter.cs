using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.UbiArt.Audio;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Images;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.UbiArt.Intermediate;

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
        ExportCoverImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoverFile), logger, textureService, io);
    }

    private static string? ExportCoverImage(ConversionContext context, string destination, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        logger.LogDebug("Attempting to export cover image from menu art folder: {MenuArtFolder}", context.FileSystem.InputFolders.MenuArtFolder);

        Image<Bgra32>? cover = UbiArtCoverGenerator.ExistingCover(context, textureService, io, logger);

        if (cover != null)
        {
            logger.LogInformation("Using existing cover image");
        }
        else
        {
            logger.LogInformation("No existing cover found, generating own cover");
            cover = UbiArtCoverGenerator.GenerateOwnCover(context, textureService, logger);
        }

        if (cover == null)
        {
            logger.LogWarning("Cover art could not be prepared for intermediate export.");
            return null;
        }

        using (cover)
        {
            io.CreateDirectory(Path.GetDirectoryName(destination)!);
            cover.Save(destination, Encoder);
            logger.LogInformation("Saved cover image to: {Destination}", destination);
        }

        return destination;
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
        for (int i = 0; i < coachFilesCooked.Length; i++)
        {
            string destination = io.Combine(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder), $"coach_{i + 1:D2}.webp");
            try
            {
                using Stream s = context.FileSystem.GetFileStream(coachFilesCooked[i]);
                using Image<Bgra32>? coach = textureService.ConvertToImage(s);
                if (coach is null)
                {
                    logger.LogWarning("Failed to convert coach image: {Path}", coachFilesCooked[i].RelativePath);
                    continue;
                }

                SaveAsWebp(coach, destination, io);
                logger.LogDebug("Saved coach asset: {FileName}", Path.GetFileName(destination));
            }
            catch (FileNotFoundException)
            {
                logger.LogWarning("Failed to convert coach image: {Path} (not found)", coachFilesCooked[i].RelativePath);
                continue;
            }
        }

        using Image<Bgra32>? backgroundImage = UbiArtCoverGenerator.GetBackground(context, textureService);
        if (backgroundImage is not null)
            SaveAsWebp(backgroundImage, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesBackgroundFile), io);
        logger.LogInformation("Saved coaches background");
    }

    private static void AttachMotionAssets(ConversionContext context, string packageRoot, ILogger logger, IFileSystem io)
    {
        string movesFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.MovesFolder);
        string gesturesFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.GesturesFolder);

        CopyCookedFiles(context, context.FileSystem.InputFolders.MovesFolder, "*.msm", movesFolder, io);

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

        io.CreateDirectory(destinationFolder);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (CookedFile file in files)
        {
            string name = $"{file.Name}{file.Extension}";
            if (!seen.Add(name))
                continue;
            string destination = io.Combine(destinationFolder, name);
            try
            {
                using Stream sourceStream = context.FileSystem.GetFileStream(file);
                using FileStream destStream = File.Open(destination, FileMode.Create, FileAccess.Write);
                sourceStream.CopyTo(destStream);
            }
            catch (FileNotFoundException)
            {
                // Skip missing files
                continue;
            }
        }

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
        var coachVideos = fileSystem
            .GetAllFiles(videosCoachFolder, "*.webm")
            .ToArray();

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
        fs.CreateDirectory(Path.GetDirectoryName(destination)!);
        image.Save(destination, Encoder);
    }
}