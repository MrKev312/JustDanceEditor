using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.AssetExtraction;
using JustDanceEditor.Formats.UbiArt.Import.Assets;
using JustDanceEditor.Formats.UbiArt.Import.Audio;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;
using JustDanceEditor.Formats.UbiArt.Import.Core;
using JustDanceEditor.Formats.UbiArt.Import.Recordings;
using JustDanceEditor.Formats.UbiArt.Model;
using JustDanceEditor.Formats.UbiArt.Model.Clips;

using KevInc.Audio.NAudio;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using System.Diagnostics;
using System.Globalization;

using UbiArtPictogramClip = JustDanceEditor.Formats.UbiArt.Model.Clips.PictogramClip;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal static class IntermediateAssetWriter
{
    private const double CinematicDurationPaddingSeconds = 5.0;
    private const int CinematicOutputHeight = 1080;

    static ImageEncoder Encoder => JDI.Utilities.WebpSettings.LosslessWebpEncoder;
    public static async Task PopulateFromUbiArtAsync(ConversionContext context, IntermediateSongPackage package, string packageRoot, ILogger logger, ITextureService textureService, IAudioConverter audioConverter, IFileSystem? io = null)
    {
        IFileSystem iofs = io ?? new SystemFileSystem();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(audioConverter);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        context.IntermediatePackage ??= package;

        ResetAssetsRoot(packageRoot, iofs);

        JDUbiArtSong songData = context.SongData ?? throw new InvalidOperationException("Song data not loaded.");
        string audioMasterFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder, iofs);
        string audioPreviewFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder, iofs);
        string videoFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder, iofs);
        string previewVideoFolder = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);
        TryDeleteDirectory(previewVideoFolder, logger, iofs);

        Task pictoTask = ConvertPictogramsAsync(context, packageRoot, logger, textureService, iofs);
        Task audioTask = AudioConverter.ConvertAudioAsync(songData, context.FileSystem, new AudioConversionOptions
        {
            MasterOutputFolder = audioMasterFolder,
            PreviewOutputFolder = audioPreviewFolder,
        }, audioConverter, logger);
        Task videoTask = CopyMasterVideoAsync(context.FileSystem, songData, package, videoFolder, logger, textureService, iofs, context.Request.RenderVideoSpeedTest);
        Task assetTask = CopyAssetsToPackageAsync(context, packageRoot, logger, textureService, iofs);
        Task recordingTask = UbiArtRecordingImporter.ImportAsync(context, packageRoot, logger);

        await Task.WhenAll(pictoTask, audioTask, videoTask, assetTask, recordingTask);
    }

    private static async Task ConvertPictogramsAsync(ConversionContext context, string packageRoot, ILogger logger, ITextureService textureService, IFileSystem io)
    {
        CookedFile[] folderPictos = context.FileSystem.AssetResolver?.GetPictograms() ?? [];
        CookedFile[] referencedPictos = [.. EnumerateReferencedPictogramFiles(context)];
        CookedFile[] pictoFiles = [.. folderPictos
            .Concat(referencedPictos)
            .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

        string outputFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder, io);

        UbiArtPictoConversionRequest request = new(
            context.SongData,
            pictoFiles,
            outputFolder);

        await Task.Run(() => UbiArtPictoConverter.Convert(request, logger, textureService, context.FileSystem, io));
    }

    private static IEnumerable<CookedFile> EnumerateReferencedPictogramFiles(ConversionContext context)
    {
        if (context.SongData == null)
            yield break;

        foreach (UbiArtPictogramClip clip in context.SongData.Clips.OfType<UbiArtPictogramClip>())
        {
            if (TryResolveReferencedCookedFile(context.FileSystem, clip.PictoPath, out CookedFile? file) &&
                file != null)
            {
                yield return file;
            }
        }
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

        JDUbiArtSong song = context.SongData ?? throw new ArgumentException("SongData cannot be null.", nameof(context));

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
        JDUbiArtSong song = context.SongData ?? throw new ArgumentException("SongData cannot be null.", nameof(context));
        string assetSongName = GetAssetSongName(song);

        // Try to find square cover in MenuArt (prefer cover_generic as it's the highest resolution)
        CookedFile? squareCover = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{assetSongName}_cover_generic.*")
            .FirstOrDefault();

        // Fall back to cover_online if cover_generic doesn't exist
        squareCover ??= context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{assetSongName}_cover_online.*")
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
        // Try to find album coach in MenuArt
        CookedFile? albumCoach = context.FileSystem.AssetResolver?.GetAlbumCoach();

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
        JDUbiArtSong song = context.SongData ?? throw new ArgumentException("SongData cannot be null.", nameof(context));
        string assetSongName = GetAssetSongName(song);

        // Try to find banner in MenuArt
        CookedFile? banner = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{assetSongName}_banner_bkg.*")
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
        JDUbiArtSong song = context.SongData ?? throw new ArgumentException("SongData cannot be null.", nameof(context));
        string assetSongName = GetAssetSongName(song);

        // Try to find map background in MenuArt
        CookedFile? mapBkg = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{assetSongName}_map_bkg.*")
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
                    logger.LogWarning("Failed to convert coach image: {Path} (texture decoder returned no image)", coachFilesCooked[i].RelativePath);
                    return;
                }

                SaveAsWebp(coach, destination, io);
                logger.LogDebug("Saved coach asset: {FileName}", Path.GetFileName(destination));
            }
            catch (FileNotFoundException)
            {
                logger.LogWarning("Failed to convert coach image: {Path} (not found)", coachFilesCooked[i].RelativePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to convert coach image: {Path}: {Message}", coachFilesCooked[i].RelativePath, ex.Message);
            }
        });
    }

    private static void AttachMotionAssets(ConversionContext context, string packageRoot, ILogger logger, IFileSystem io)
    {
        foreach (string motionFolder in EnumerateMotionSearchFolders(context))
            ImportMotionClassifiers(context, motionFolder, packageRoot);

        foreach (UbiArtGestureFolderSource gestureFolder in EnumerateUbiArtGestureSearchFolders(context))
            CopyCookedFiles(context, gestureFolder.SourceRelativeFolder, "*.gesture", ResolvePackagePath(packageRoot, gestureFolder.PackageRelativeFolder), io);

        if (UbiArtGestureFolders.TryGetPlatformFolder(context.FileSystem.VersionProfile.Platform, out string? sourcePlatformGestureFolder) &&
            sourcePlatformGestureFolder != null)
        {
            string gesturesRelative = context.FileSystem.InputFolders.TimelineFolder + "/gestures";
            CopyCookedFiles(context, gesturesRelative, "*.gesture", ResolvePackagePath(packageRoot, UbiArtGestureFolders.PackageFolder(sourcePlatformGestureFolder)), io);
        }

        foreach (MotionClip clip in context.SongData?.Clips.OfType<MotionClip>() ?? [])
        {
            string extension = Path.GetExtension(clip.ClassifierPath);
            if (extension.Equals(".gesture", StringComparison.OrdinalIgnoreCase))
            {
                string? gesturePackageFolder = GetReferencedUbiArtGesturePackageFolder(context, clip.ClassifierPath);
                if (gesturePackageFolder != null)
                    CopyReferencedCookedFile(context, clip.ClassifierPath, ResolvePackagePath(packageRoot, gesturePackageFolder), io);
                continue;
            }

            ImportReferencedMotionClassifier(context, clip.ClassifierPath, packageRoot);
        }
    }

    private static void ImportMotionClassifiers(ConversionContext context, string relativeFolder, string packageRoot)
    {
        CookedFile[] files;
        try
        {
            files = context.FileSystem.GetAllFiles(relativeFolder, "*.msm");
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        (CookedFile File, string Name)[] uniqueFiles = [.. files
            .Select(file => (File: file, Name: $"{file.Name}{file.Extension}"))
            .Where(item => seen.Add(item.Name))];

        Parallel.ForEach(uniqueFiles, item =>
        {
            try
            {
                using Stream sourceStream = context.FileSystem.GetFileStream(item.File);
                using MemoryStream buffer = new();
                sourceStream.CopyTo(buffer);
                JdiMotionClassifierStorage.ImportClassifier(packageRoot, item.Name, buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
            }
            catch (FileNotFoundException)
            {
            }
        });
    }

    private static void ImportReferencedMotionClassifier(ConversionContext context, string relativePath, string packageRoot)
    {
        if (!TryResolveReferencedCookedFile(context.FileSystem, relativePath, out CookedFile? file) || file == null)
            return;

        try
        {
            using Stream sourceStream = context.FileSystem.GetFileStream(file);
            using MemoryStream buffer = new();
            sourceStream.CopyTo(buffer);
            JdiMotionClassifierStorage.ImportClassifier(
                packageRoot,
                $"{file.Name}{file.Extension}",
                buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static IEnumerable<string> EnumerateMotionSearchFolders(ConversionContext context)
    {
        string timelineMovesFolder = Path.Combine(context.FileSystem.InputFolders.TimelineFolder, "moves");

        yield return context.FileSystem.InputFolders.MovesFolder;
        yield return timelineMovesFolder;

        foreach (UbiArtPlatform platform in Enum.GetValues<UbiArtPlatform>())
        {
            if (platform == UbiArtPlatform.Uncooked)
                continue;

            string platformFolder = platform.GetCookedFolderName();
            if (!string.IsNullOrWhiteSpace(platformFolder))
                yield return Path.Combine(timelineMovesFolder, platformFolder);
        }

        yield return Path.Combine(timelineMovesFolder, "wiiu");
    }

    private static IEnumerable<UbiArtGestureFolderSource> EnumerateUbiArtGestureSearchFolders(ConversionContext context)
    {
        string timelineMovesFolder = Path.Combine(context.FileSystem.InputFolders.TimelineFolder, "moves");

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string[] directories;
        try
        {
            directories = context.FileSystem.GetDirectories(timelineMovesFolder);
        }
        catch (DirectoryNotFoundException)
        {
            directories = [];
        }

        foreach (string directory in directories)
        {
            string platformFolder = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(platformFolder) && seen.Add(platformFolder))
                yield return new(Path.Combine(timelineMovesFolder, platformFolder), UbiArtGestureFolders.PackageFolder(platformFolder));
        }

        foreach (UbiArtGestureFolder gestureFolder in UbiArtGestureFolders.All)
        {
            if (seen.Add(gestureFolder.PlatformFolder))
                yield return new(Path.Combine(timelineMovesFolder, gestureFolder.PlatformFolder), gestureFolder.PackageRelativeFolder);
        }
    }

    private static string? GetReferencedUbiArtGesturePackageFolder(ConversionContext context, string classifierPath)
    {
        if (TryGetUbiArtGesturePackageFolderFromPath(classifierPath, out string? packageFolder))
            return packageFolder;

        return UbiArtGestureFolders.TryGetPlatformFolder(context.FileSystem.VersionProfile.Platform, out string? platformFolder) &&
            platformFolder != null
            ? UbiArtGestureFolders.PackageFolder(platformFolder)
            : null;
    }

    private static bool TryGetUbiArtGesturePackageFolderFromPath(string relativePath, out string? packageFolder)
    {
        packageFolder = null;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string[] segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        int movesIndex = Array.FindIndex(segments, segment => segment.Equals("moves", StringComparison.OrdinalIgnoreCase));
        if (movesIndex >= 0 && movesIndex + 1 < segments.Length)
        {
            packageFolder = UbiArtGestureFolders.PackageFolder(segments[movesIndex + 1]);
            return true;
        }

        foreach (UbiArtGestureFolder gestureFolder in UbiArtGestureFolders.All)
        {
            if (segments.Any(segment => segment.Equals(gestureFolder.PlatformFolder, StringComparison.OrdinalIgnoreCase)))
            {
                packageFolder = gestureFolder.PackageRelativeFolder;
                return true;
            }
        }

        return false;
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

    private static void CopyReferencedCookedFile(ConversionContext context, string relativePath, string destinationFolder, IFileSystem io)
    {
        if (!TryResolveReferencedCookedFile(context.FileSystem, relativePath, out CookedFile? file) ||
            file == null)
        {
            return;
        }

        io.CreateDirectory(destinationFolder);
        string destination = io.Combine(destinationFolder, $"{file.Name}{file.Extension}");
        try
        {
            using Stream sourceStream = context.FileSystem.GetFileStream(file);
            using FileStream destStream = File.Open(destination, FileMode.Create, FileAccess.Write);
            sourceStream.CopyTo(destStream);
        }
        catch (FileNotFoundException)
        {
        }
    }

    private static bool TryResolveReferencedCookedFile(JustDanceUbiArtFileSystem fileSystem, string relativePath, out CookedFile? file)
    {
        file = null;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string normalized = relativePath.Replace('\\', '/');
        string[] candidates =
        [
            normalized,
            normalized.EndsWith(".ckd", StringComparison.OrdinalIgnoreCase) ? normalized : normalized + ".ckd"
        ];

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (fileSystem.GetFilePath(candidate, out file))
                return true;
        }

        return false;
    }

    private static void ResetAssetsRoot(string packageRoot, IFileSystem io)
    {
        string assetsRoot = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.Root);
        if (io.DirectoryExists(assetsRoot))
            io.DeleteDirectory(assetsRoot, true);
        io.CreateDirectory(assetsRoot);
    }

    private static async Task CopyMasterVideoAsync(
        JustDanceUbiArtFileSystem fileSystem,
        JDUbiArtSong songData,
        IntermediateSongPackage package,
        string destinationFolder,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io,
        bool renderVideoSpeedTest)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(songData);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(textureService);

        if (songData.LegacyMashup != null)
        {
            io.CreateDirectory(destinationFolder);
            string mashupDestination = io.Combine(destinationFolder, $"{songData.Name}{GetRenderedVideoExtension(renderVideoSpeedTest)}");
            await MashupVideoRenderer.RenderAsync(
                fileSystem,
                songData,
                package.TimelineStructure,
                fileSystem.TempFolders.MapFolder,
                mashupDestination,
                textureService,
                logger,
                io);
            return;
        }

        CookedFile[] sourceFiles = GetVideoFiles(fileSystem);
        if (sourceFiles.Length == 0)
        {
            logger.LogWarning("No video file found in UbiArt input; skipping video copy.");
            return;
        }

        if (sourceFiles.Length == 1)
            logger.LogInformation("Selected UbiArt master video '{VideoPath}'.", sourceFiles[0].RelativePath);
        else
            logger.LogInformation("Selected {Count} UbiArt master video variants: {VideoPaths}.", sourceFiles.Length, string.Join(", ", sourceFiles.Select(file => file.RelativePath)));

        io.CreateDirectory(destinationFolder);
        foreach (CookedFile sourceFile in sourceFiles)
            await CopyMasterVideoFileAsync(fileSystem, sourceFile, package, destinationFolder, logger, textureService, io, renderVideoSpeedTest);
    }

    private static async Task CopyMasterVideoFileAsync(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        IntermediateSongPackage package,
        string destinationFolder,
        ILogger logger,
        ITextureService textureService,
        IFileSystem io,
        bool renderVideoSpeedTest)
    {
        string destination = io.Combine(destinationFolder, Path.GetFileName(sourceFile.RelativePath));
        string? tempSource = null;
        try
        {
            tempSource = await MaterializeCookedFileAsync(fileSystem, sourceFile, io);
            LegacyCutoutVideoLayout? cutoutLayout = await TryGetLegacyCutoutLayoutAsync(fileSystem, sourceFile, tempSource, logger);

            if (cutoutLayout != null)
            {
                string tempFolder = Path.GetDirectoryName(tempSource) ?? fileSystem.TempFolders.MapFolder;
                double outputDurationSeconds = GetCinematicRenderDurationSeconds(package, cutoutLayout.Value.DurationSeconds);
                string renderDestination = GetRenderedVideoDestination(destination, renderVideoSpeedTest);

                await CinematicVisualRenderer.RenderCutoutVideoAsync(
                    fileSystem,
                    tempFolder,
                    tempSource,
                    renderDestination,
                    outputDurationSeconds,
                    package.TimelineStructure,
                    cutoutLayout.Value.Width,
                    cutoutLayout.Value.VisibleHeight,
                    cutoutLayout.Value.AlphaHeight,
                    cutoutLayout.Value.OutputWidth,
                    cutoutLayout.Value.OutputHeight,
                    textureService,
                    logger,
                    io);

                logger.LogInformation("Imported legacy cutout video into intermediate package with unified cinematic renderer.");
                return;
            }

            if (await TryNormalizeSingleVideoSceneTo16By9Async(fileSystem, sourceFile, package, tempSource, destination, logger, io))
                return;

            await CopyCookedFileAsync(fileSystem, sourceFile, destination);
            logger.LogInformation("Copied master video into intermediate package without conversion.");
        }
        catch (FileNotFoundException)
        {
            logger.LogWarning("Video file not found, skipping.");
        }
        finally
        {
            TryDeleteFile(tempSource, io);
        }
    }

    private static async Task<bool> TryNormalizeSingleVideoSceneTo16By9Async(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        IntermediateSongPackage package,
        string? existingTempSource,
        string destination,
        ILogger logger,
        IFileSystem io)
    {
        string tempSource = existingTempSource ?? await MaterializeCookedFileAsync(fileSystem, sourceFile, io);
        bool ownsTempSource = existingTempSource == null;
        try
        {
            JdiVideoInfo? videoInfo = await JdiVideoConverter.TryInspectVideoAsync(tempSource);
            if (videoInfo == null || videoInfo.Width <= 0 || videoInfo.Height <= 0)
                return false;

            double aspect = videoInfo.Width / (double)videoInfo.Height;
            const double targetAspect = 16.0 / 9.0;
            if (Math.Abs(aspect - targetAspect) < 0.001)
                return false;

            double outputDurationSeconds = GetCinematicRenderDurationSeconds(package, videoInfo.Duration.TotalSeconds);
            if (!CinematicPrerenderedVideoAnalyzer.TryAnalyzeSingleVideoScene(
                fileSystem,
                sourceFile,
                outputDurationSeconds,
                logger,
                out CinematicSingleVideoScene singleVideoScene))
            {
                return false;
            }

            string cropFilter = BuildCenterCrop16By9Filter(videoInfo.Width, videoInfo.Height);
            string filter = $"{cropFilter},setsar=1";
            string ffmpegPath = await JdiFfmpegResolver.GetFfmpegPathAsync();
            string tempOutput = Path.Combine(Path.GetDirectoryName(destination) ?? fileSystem.TempFolders.MapFolder, $"{Path.GetFileNameWithoutExtension(destination)}_{Guid.NewGuid():N}.webm");

            string[] args =
            [
                "-hide_banner",
                "-y",
                "-i", tempSource,
                "-an",
                "-vf", filter,
                "-c:v", "libvpx",
                "-deadline", "realtime",
                "-cpu-used", "8",
                "-threads", Math.Max(1, Environment.ProcessorCount).ToString(CultureInfo.InvariantCulture),
                "-lag-in-frames", "0",
                "-auto-alt-ref", "0",
                "-crf", "10",
                "-b:v", "12M",
                "-maxrate", "18M",
                "-bufsize", "24M",
                "-pix_fmt", "yuv420p",
                tempOutput
            ];

            await RunFfmpegAsync(ffmpegPath, args, logger);
            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(tempOutput, destination);
            logger.LogInformation(
                "Normalized pre-rendered single-video scene '{Video}' from {SourceWidth}x{SourceHeight} to native-cropped 16:9 using {Filter}; scene source '{SceneVideoPath}', output actor '{OutputActorKey}'.",
                Path.GetFileName(sourceFile.RelativePath),
                videoInfo.Width,
                videoInfo.Height,
                filter,
                singleVideoScene.SourceVideoPath,
                singleVideoScene.OutputActorKey);
            return true;
        }
        finally
        {
            if (ownsTempSource)
                TryDeleteFile(tempSource, io);
        }
    }

    private static string GetAssetSongName(JDUbiArtSong song) =>
        song.LegacyMashup?.BaseSongName ?? song.Name;

    private static string GetRenderedVideoExtension(bool renderVideoSpeedTest) =>
        renderVideoSpeedTest ? ".speedtest" : ".webm";

    private static string GetRenderedVideoDestination(string destination, bool renderVideoSpeedTest) =>
        !renderVideoSpeedTest
            ? destination
            : Path.ChangeExtension(destination, GetRenderedVideoExtension(renderVideoSpeedTest));

    private static string BuildCenterCrop16By9Filter(int width, int height)
    {
        const double targetAspect = 16.0 / 9.0;
        double aspect = width / (double)height;
        if (aspect < targetAspect)
        {
            int cropHeight = RoundToEven((int)Math.Floor(width * 9.0 / 16.0));
            int y = Math.Max(0, (height - cropHeight) / 2);
            return $"crop={width}:{cropHeight}:0:{y}";
        }

        int cropWidth = RoundToEven((int)Math.Floor(height * 16.0 / 9.0));
        int x = Math.Max(0, (width - cropWidth) / 2);
        return $"crop={cropWidth}:{height}:{x}:0";
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, IReadOnlyList<string> args, ILogger logger)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        foreach (string arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {stderr}");

        if (!string.IsNullOrWhiteSpace(stdout))
            logger.LogTrace("ffmpeg stdout: {Output}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogTrace("ffmpeg stderr: {Output}", stderr);
    }

    internal static double GetMasterVideoDurationSeconds(IntermediateSongPackage package, double fallbackDurationSeconds)
    {
        try
        {
            double durationSeconds = GetMusicTrackVideoTimeSeconds(
                package.TimelineStructure,
                GetEffectiveEndBeat(package.TimelineStructure));
            if (durationSeconds > 0)
                return durationSeconds;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or NotSupportedException)
        {
        }

        return fallbackDurationSeconds;
    }

    internal static double GetCinematicRenderDurationSeconds(IntermediateSongPackage package, double fallbackDurationSeconds) =>
        GetMasterVideoDurationSeconds(package, fallbackDurationSeconds) + CinematicDurationPaddingSeconds;

    private static double GetMusicTrackVideoTimeSeconds(TimelineStructureDocument timelineStructure, double beat) =>
        GetMusicTrackSecondsAtBeat(timelineStructure.Markers, beat) - timelineStructure.VideoStartOffset;

    private static double GetMusicTrackSecondsAtBeat(IReadOnlyList<int> markers, double beat)
        => MusicTrackTiming.GetSecondsAtBeat(markers, beat);

    private static int GetEffectiveEndBeat(TimelineStructureDocument timelineStructure)
    {
        if (timelineStructure.EndBeat != 0)
            return timelineStructure.EndBeat;

        return Math.Max(0, timelineStructure.Markers.Count - 1);
    }

    private static async Task<string> MaterializeCookedFileAsync(JustDanceUbiArtFileSystem fileSystem, CookedFile sourceFile, IFileSystem io)
    {
        string tempFolder = io.Combine(fileSystem.TempFolders.MapFolder, "video");
        io.CreateDirectory(tempFolder);

        string extension = Path.GetExtension(sourceFile.RelativePath);
        if (string.IsNullOrWhiteSpace(extension))
            extension = ".webm";

        string tempPath = io.Combine(tempFolder, $"source_{Guid.NewGuid():N}{extension}");

        await using Stream src = fileSystem.GetFileStream(sourceFile);
        await using FileStream dest = File.Open(tempPath, FileMode.Create, FileAccess.Write);
        await src.CopyToAsync(dest);

        return tempPath;
    }

    private static async Task CopyCookedFileAsync(JustDanceUbiArtFileSystem fileSystem, CookedFile sourceFile, string destination)
    {
        await using Stream src = fileSystem.GetFileStream(sourceFile);
        await using FileStream dest = File.Open(destination, FileMode.Create, FileAccess.Write);
        await src.CopyToAsync(dest);
    }

    private static async Task<LegacyCutoutVideoLayout?> TryGetLegacyCutoutLayoutAsync(JustDanceUbiArtFileSystem fileSystem, CookedFile sourceFile, string sourcePath, ILogger logger)
    {
        try
        {
            JdiVideoInfo? videoInfo = await JdiVideoConverter.TryInspectVideoAsync(sourcePath);
            if (videoInfo == null)
                return null;

            int width = videoInfo.Width;
            int height = videoInfo.Height;
            if (width <= 0 || height <= 0 || height % 3 != 0)
                return null;

            int visibleHeight = height * 2 / 3;
            int alphaHeight = height - visibleHeight;
            if (visibleHeight <= 0 || alphaHeight <= 0)
                return null;

            bool stretchTo16By9 = fileSystem.VersionProfile.Platform is UbiArtPlatform.Revolution or UbiArtPlatform.Cafe;
            if (!HasLegacyStackedAlphaLayout(width, visibleHeight, stretchTo16By9))
                return null;

            int outputHeight = Math.Max(visibleHeight, CinematicOutputHeight);
            int outputWidth = stretchTo16By9
                ? RoundToEven((int)Math.Round(outputHeight * 16.0 / 9.0, MidpointRounding.AwayFromZero))
                : RoundToEven((int)Math.Round(width * (outputHeight / (double)visibleHeight), MidpointRounding.AwayFromZero));
            if (outputWidth <= 0)
                outputWidth = width;

            logger.LogInformation(
                "Detected JD{EngineVersion} stacked alpha video '{Video}' ({Width}x{Height}); importing visible {VisibleWidth}x{VisibleHeight} frame as {OutputWidth}x{OutputHeight}.",
                (int)fileSystem.VersionProfile.EngineVersion,
                Path.GetFileName(sourceFile.RelativePath),
                width,
                height,
                width,
                visibleHeight,
                outputWidth,
                outputHeight);

            return new LegacyCutoutVideoLayout(width, height, visibleHeight, alphaHeight, outputWidth, outputHeight, videoInfo.Duration.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not inspect legacy video '{Video}' for stacked alpha layout.", sourceFile.RelativePath);
            return null;
        }
    }

    private static bool HasLegacyStackedAlphaLayout(int width, int visibleHeight, bool stretchedOnImport)
    {
        if (width <= 0 || visibleHeight <= 0)
            return false;

        return stretchedOnImport
            ? width * 3 == visibleHeight * 4
            : width * 9 == visibleHeight * 16;
    }

    private static int RoundToEven(int value)
    {
        if (value <= 0)
            return value;

        return value % 2 == 0 ? value : value - 1;
    }

    private static void TryDeleteFile(string? path, IFileSystem io)
    {
        if (string.IsNullOrWhiteSpace(path) || !io.FileExists(path))
            return;

        try
        {
            io.DeleteFile(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFiles(IEnumerable<string>? paths, IFileSystem io)
    {
        if (paths == null)
            return;

        foreach (string path in paths)
            TryDeleteFile(path, io);
    }

    private static void TryDeleteDirectories(IEnumerable<string>? paths, ILogger logger, IFileSystem io)
    {
        if (paths == null)
            return;

        foreach (string path in paths)
            TryDeleteDirectory(path, logger, io);
    }

    private readonly record struct LegacyCutoutVideoLayout(int Width, int Height, int VisibleHeight, int AlphaHeight, int OutputWidth, int OutputHeight, double DurationSeconds);
    private readonly record struct UbiArtGestureFolderSource(string SourceRelativeFolder, string PackageRelativeFolder);

    private static CookedFile[] GetVideoFiles(JustDanceUbiArtFileSystem fileSystem) =>
        UbiArtVideoFileSelector.FindPreferredVideoFiles(fileSystem);

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