using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Audio;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Images;
using JustDanceEditor.Formats.UbiArt.Video;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.UbiArt.Intermediate;

internal static class IntermediateAssetWriter
{
    private static readonly WebpEncoder LosslessWebpEncoder = new()
    {
        FileFormat = WebpFileFormatType.Lossless,
        Quality = 100
    };

    public static async Task PopulateFromUbiArtAsync(ConversionContext context, IntermediateSongPackage package, string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        context.IntermediatePackage ??= package;

        AssetStagingArea staging = AssetStagingArea.Create(Path.Combine(context.FileSystem.TempFolders.MapFolder, "intermediateAssetStage"));
        staging.Reset();

        await EnsurePrerequisitesAsync();
        await PrepareVisualAssetsAsync(context);
        JDUbiArtSong songData = context.SongData ?? throw new InvalidOperationException("Song data not loaded.");

        await AudioConverter.ConvertAudioAsync(songData, context.FileSystem, context.Request, new AudioConversionOptions
        {
            MasterOutputFolder = staging.AudioMaster,
            PreviewOutputFolder = staging.AudioPreview,
        });
        await VideoConverter.ConvertVideoAsync(songData, context.FileSystem, context.Request, new VideoConversionOptions
        {
            VideoOutputFolder = staging.Video,
            PreviewOutputFolder = staging.PreviewVideo,
        });

        CopyAssetsToPackage(context, packageRoot, staging);
    }

    private static async Task EnsurePrerequisitesAsync()
    {
        if (!File.Exists("ffmpeg.exe") && !File.Exists("ffmpeg"))
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
    }

    private static async Task PrepareVisualAssetsAsync(ConversionContext context)
    {
        await PrepareMenuArtFromUbiArtAsync(context);
        await PreparePictogramsAsync(context);
    }

    private static async Task PrepareMenuArtFromUbiArtAsync(ConversionContext context)
    {
        CookedFile[] menuArtFiles = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder);
        UbiArtMenuArtConversionRequest menuRequest = new(menuArtFiles, context.FileSystem.TempFolders.MenuArtFolder);
        await UbiArtMenuArtConverter.ConvertMenuArtAsync(menuRequest);
    }

    private static async Task PreparePictogramsAsync(ConversionContext context)
    {
        CookedFile[] pictoFiles = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.PictosFolder);
        string[] pictoPaths = [.. pictoFiles.Select(file => (string)file)];

        UbiArtPictoConversionRequest request = new(
            context.SongData,
            pictoPaths,
            context.FileSystem.TempFolders.PictoFolder);

        await Task.Run(() => UbiArtPictoConverter.Convert(request));
    }

    private static void CopyAssetsToPackage(ConversionContext context, string packageRoot, AssetStagingArea staging)
    {
        ResetAssetsRoot(packageRoot);

        AttachAudioAssets(packageRoot, staging);
        AttachVideoAssets(packageRoot, staging);
        AttachBrandingAssets(context, packageRoot);
        AttachCoachAssets(context, packageRoot);
        AttachPictograms(context, packageRoot);
        AttachMotionAssets(context, packageRoot);
    }

    private static void AttachAudioAssets(string packageRoot, AssetStagingArea staging)
    {
        CopyFirstFile(staging.AudioMaster, "*.opus", ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.AudioMasterFile));
        CopyFirstFile(staging.AudioPreview, "*.opus", ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.AudioPreviewFile));
    }

    private static void AttachVideoAssets(string packageRoot, AssetStagingArea staging)
    {
        string videoFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string previewFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);

        CopyDirectoryContent(staging.Video, videoFolder);
        CopyDirectoryContent(staging.PreviewVideo, previewFolder);
    }

    private static void AttachBrandingAssets(ConversionContext context, string packageRoot)
    {
        EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder);
        ExportCoverImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoverFile));
        ExportSongTitleLogo(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.SongTitleFile));
    }

    private static string? ExportCoverImage(ConversionContext context, string destination)
    {
        UbiArtCoverRequest coverRequest = new(context.SongData, context.FileSystem.TempFolders.MenuArtFolder);

        // Note: Online cover fetching removed for now to avoid external dependencies in core format logic.
        // Can be re-added if needed via a service.
        using Image<Rgba32>? cover = UbiArtCoverGenerator.ExistingCover(coverRequest)
            ?? UbiArtCoverGenerator.GenerateOwnCover(coverRequest);

        if (cover == null)
        {
            Logger.Log("Cover art could not be prepared for intermediate export.", LogLevel.Warning);
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        cover.Save(destination, LosslessWebpEncoder);
        return destination;
    }

    private static string? ExportSongTitleLogo(ConversionContext context, string destination)
    {
        UbiArtCoverRequest coverRequest = new(context.SongData, context.FileSystem.TempFolders.MenuArtFolder);

        using Image<Rgba32>? title = UbiArtCoverGenerator.ExistingSongTitleLogo(coverRequest);

        if (title == null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        title.Save(destination, LosslessWebpEncoder);
        return destination;
    }

    private static void AttachCoachAssets(ConversionContext context, string packageRoot)
    {
        string menuArtFolder = context.FileSystem.TempFolders.MenuArtFolder;
        if (!Directory.Exists(menuArtFolder))
        {
            TryDeleteDirectory(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder));
            return;
        }

        string coachesFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder);

        string[] coachFiles = [.. Directory.EnumerateFiles(menuArtFolder, $"{context.SongData.Name}_Coach_*.png", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("_phone", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];

        if (coachFiles.Length == 0)
        {
            TryDeleteDirectory(coachesFolder);
            return;
        }

        for (int i = 0; i < coachFiles.Length; i++)
        {
            string destination = Path.Combine(coachesFolder, $"coach_{i + 1:D2}.webp");
            using Image<Rgba32> coach = Image.Load<Rgba32>(coachFiles[i]);
            SaveAsWebp(coach, destination);
        }

        string? background = Directory.EnumerateFiles(menuArtFolder, "*map_bkg*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (background != null)
        {
            using Image<Rgba32> backgroundImage = Image.Load<Rgba32>(background);
            SaveAsWebp(backgroundImage, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesBackgroundFile));
        }
    }

    private static void AttachPictograms(ConversionContext context, string packageRoot)
    {
        string pictoFolder = context.FileSystem.TempFolders.PictoFolder;
        if (!Directory.Exists(pictoFolder))
        {
            TryDeleteDirectory(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder));
            return;
        }

        string outputFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder);
        foreach (string file in Directory.EnumerateFiles(pictoFolder, "*.png", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(file).StartsWith("atlas_", StringComparison.OrdinalIgnoreCase))
                continue;
            string destination = Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file) + ".webp");
            using Image<Rgba32> picto = Image.Load<Rgba32>(file);
            SaveAsWebp(picto, destination);
        }

        if (!Directory.EnumerateFiles(outputFolder).Any())
            TryDeleteDirectory(outputFolder);

    }

    private static void AttachMotionAssets(ConversionContext context, string packageRoot)
    {
        string movesFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.MovesFolder);
        string gesturesFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.GesturesFolder);

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

    private static string? CopyFirstFile(string? sourceFolder, string searchPattern, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            return null;

        string? source = Directory.EnumerateFiles(sourceFolder, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (source == null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(source, destinationPath, true);
        return destinationPath;
    }

    private static string? CopyDirectoryContent(string? sourceFolder, string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
            return null;

        Directory.CreateDirectory(destinationFolder);
        foreach (string file in Directory.EnumerateFiles(sourceFolder))
        {
            string destination = Path.Combine(destinationFolder, Path.GetFileName(file));
            File.Copy(file, destination, true);
        }

        return Directory.EnumerateFiles(destinationFolder).Any() ? destinationFolder : null;
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
                return;

            if (Directory.EnumerateFileSystemEntries(folder).Any())
                return;

            Directory.Delete(folder);
        }
        catch
        {
            // Best-effort cleanup; leftover folders do not block conversion.
        }
    }

    private static void ResetAssetsRoot(string packageRoot)
    {
        string assetsRoot = ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.Root);
        if (Directory.Exists(assetsRoot))
            Directory.Delete(assetsRoot, true);
        Directory.CreateDirectory(assetsRoot);
    }

    private static string EnsureFolder(string packageRoot, string relativeFolder)
    {
        string path = ResolvePackagePath(packageRoot, relativeFolder);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolvePackagePath(string packageRoot, string relative)
    {
        return IntermediatePackageLayout.Resolve(packageRoot, relative);
    }

    private static void SaveAsWebp(Image<Rgba32> image, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        image.Save(destination, LosslessWebpEncoder);
    }

    private sealed class AssetStagingArea
    {
        private AssetStagingArea(string root)
        {
            Root = root;
            AudioMaster = Path.Combine(root, "audioMaster");
            AudioPreview = Path.Combine(root, "audioPreview");
            Video = Path.Combine(root, "video");
            PreviewVideo = Path.Combine(root, "previewVideo");
        }

        public static AssetStagingArea Create(string root) => new(root);

        public string Root { get; }
        public string AudioMaster { get; }
        public string AudioPreview { get; }
        public string Video { get; }
        public string PreviewVideo { get; }

        public void Reset()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);

            Directory.CreateDirectory(AudioMaster);
            Directory.CreateDirectory(AudioPreview);
            Directory.CreateDirectory(Video);
            Directory.CreateDirectory(PreviewVideo);
        }
    }
}