using JustDanceEditor.Formats.JDI;
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

    public static async Task PopulateFromUbiArtAsync(ConversionContext context, IntermediateSongPackage package, string packageRoot, ILogger logger, JDI.Services.ITextureService textureService)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        context.IntermediatePackage ??= package;

        ResetAssetsRoot(packageRoot);

        await EnsurePrerequisitesAsync();

        JDUbiArtSong songData = context.SongData ?? throw new InvalidOperationException("Song data not loaded.");
        string audioMasterFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder);
        string audioPreviewFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder);
        string videoFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);

        // Parallelize pictogram conversion, audio conversion, and video copy
        Task pictoTask = ConvertPictogramsAsync(context, packageRoot, logger, textureService);
        Task audioTask = AudioConverter.ConvertAudioAsync(songData, context.FileSystem, new AudioConversionOptions
        {
            MasterOutputFolder = audioMasterFolder,
            PreviewOutputFolder = audioPreviewFolder,
        }, logger);
        Task videoTask = Task.Run(() => CopyMasterVideo(context.FileSystem, videoFolder, logger));

        await Task.WhenAll(pictoTask, audioTask, videoTask);

        string previewVideoFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);
        TryDeleteDirectory(previewVideoFolder, logger);

        await CopyAssetsToPackageAsync(context, packageRoot, logger, textureService);
    }

    private static async Task EnsurePrerequisitesAsync()
    {
        if (!File.Exists("ffmpeg.exe") && !File.Exists("ffmpeg"))
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
    }

    private static async Task ConvertPictogramsAsync(ConversionContext context, string packageRoot, ILogger logger, JDI.Services.ITextureService textureService)
    {
        CookedFile[] pictoFiles = context.FileSystem.AssetResolver?.GetPictograms() ?? [];
        string[] pictoPaths = [.. pictoFiles.Select(file => (string)file)];

        string outputFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder);

        UbiArtPictoConversionRequest request = new(
            context.SongData,
            pictoPaths,
            outputFolder);

        await Task.Run(() => UbiArtPictoConverter.Convert(request, logger, textureService));
    }

    private static async Task CopyAssetsToPackageAsync(ConversionContext context, string packageRoot, ILogger logger, JDI.Services.ITextureService textureService)
    {
        // Parallelize branding, coach, and motion asset operations
        Task brandingTask = Task.Run(() => AttachBrandingAssets(context, packageRoot, logger, textureService));
        Task coachTask = Task.Run(() => AttachCoachAssets(context, packageRoot, logger, textureService));
        Task motionTask = Task.Run(() => AttachMotionAssets(context, packageRoot, logger));

        await Task.WhenAll(brandingTask, coachTask, motionTask);
    }

    private static void AttachBrandingAssets(ConversionContext context, string packageRoot, ILogger logger, JDI.Services.ITextureService textureService)
    {
        EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder);
        ExportCoverImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoverFile), logger, textureService);
    }

    private static string? ExportCoverImage(ConversionContext context, string destination, ILogger logger, JDI.Services.ITextureService textureService)
    {
        logger.LogDebug("Attempting to export cover image from menu art folder: {MenuArtFolder}", context.FileSystem.InputFolders.MenuArtFolder);

        Image<Bgra32>? cover = UbiArtCoverGenerator.ExistingCover(context, textureService, logger);

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
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            cover.Save(destination, Encoder);
            logger.LogInformation("Saved cover image to: {Destination}", destination);
        }

        return destination;
    }

    private static void AttachCoachAssets(ConversionContext context, string packageRoot, ILogger logger, JDI.Services.ITextureService textureService)
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
            string destination = Path.Combine(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder), $"coach_{i + 1:D2}.webp");
            using Image<Bgra32>? coach = textureService.ConvertToImage(coachFilesCooked[i].FullPath);
            if (coach is null)
            {
                logger.LogWarning("Failed to convert coach image: {Path}", coachFilesCooked[i].FullPath);
                continue;
            }

            SaveAsWebp(coach, destination);
            logger.LogDebug("Saved coach asset: {FileName}", Path.GetFileName(destination));
        }

        using Image<Bgra32>? backgroundImage = UbiArtCoverGenerator.GetBackground(context, textureService);
        if (backgroundImage is not null)
            SaveAsWebp(backgroundImage, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesBackgroundFile));
        logger.LogInformation("Saved coaches background");
    }

    private static void AttachMotionAssets(ConversionContext context, string packageRoot, ILogger logger)
    {
        string movesFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.MovesFolder);
        string gesturesFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.GesturesFolder);

        CopyCookedFiles(context, context.FileSystem.InputFolders.MovesFolder, "*.msm", movesFolder);

        string gesturesRelative = Path.Combine(context.FileSystem.InputFolders.TimelineFolder, "gestures");
        CopyCookedFiles(context, gesturesRelative, "*.gesture", gesturesFolder);
    }

    private static string? CopyCookedFiles(ConversionContext context, string relativeFolder, string pattern, string destinationFolder)
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

        Directory.CreateDirectory(destinationFolder);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (CookedFile file in files)
        {
            string name = $"{file.Name}{file.Extension}";
            if (!seen.Add(name))
                continue;
            string source = file;
            string destination = Path.Combine(destinationFolder, name);
            File.Copy(source, destination, true);
        }

        return destinationFolder;
    }

    private static void ResetAssetsRoot(string packageRoot)
    {
        string assetsRoot = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.Root);
        if (Directory.Exists(assetsRoot))
            Directory.Delete(assetsRoot, true);
        Directory.CreateDirectory(assetsRoot);
    }

    private static void CopyMasterVideo(LayeredFileSystem fileSystem, string destinationFolder, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        string? source = GetVideoFile(fileSystem);
        if (source == null)
        {
            logger.LogWarning("No video file found in UbiArt input; skipping video copy.");
            return;
        }

        Directory.CreateDirectory(destinationFolder);
        string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
        File.Copy(source, destination, true);
        logger.LogInformation("Copied master video into intermediate package without conversion.");
    }

    private static string? GetVideoFile(LayeredFileSystem fileSystem)
    {
        if (fileSystem.GetFolderPath(fileSystem.InputFolders.MediaFolder, out string? mediaFolder))
        {
            CookedFile[] mediaVideos = fileSystem.GetAllFiles(fileSystem.InputFolders.MediaFolder, "*.webm");
            if (mediaVideos.Length > 0)
                return (string)mediaVideos[0];
        }

        string videosCoachFolder = Path.Combine(fileSystem.InputFolders.MapWorldFolder, "videoscoach");
        var coachVideos = fileSystem
            .GetAllFiles(videosCoachFolder, "*.webm")
            .Select(file => (string)file)
            .ToArray();

        if (coachVideos.Length > 0)
            return coachVideos[0];

        return null;
    }

    private static string EnsureFolder(string packageRoot, string relativeFolder)
    {
        string path = ResolvePackagePath(packageRoot, relativeFolder);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, true);
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

    private static void SaveAsWebp(Image<Bgra32> image, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        image.Save(destination, Encoder);
    }
}