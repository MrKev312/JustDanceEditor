using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.UbiArt.Audio;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Images;
using JustDanceEditor.Formats.UbiArt.Video;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.UbiArt.Intermediate;

internal static class IntermediateAssetWriter
{
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

        CopyAssetsToPackage(context, package.AssetCatalog, packageRoot, staging);
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
        string[] pictoPaths = pictoFiles.Select(file => (string)file).ToArray();
        
        UbiArtPictoConversionRequest request = new(
            context.SongData,
            pictoPaths,
            context.FileSystem.TempFolders.PictoFolder);

        await Task.Run(() => UbiArtPictoConverter.Convert(request));
    }

    private static void CopyAssetsToPackage(ConversionContext context, IntermediateAssetCatalog catalog, string packageRoot, AssetStagingArea staging)
    {
        AssetDirectories dirs = AssetDirectories.Create(packageRoot);
        dirs.Reset();

        AttachAudioAssets(catalog, dirs, staging);
        AttachVideoAssets(catalog, dirs, staging);
        AttachBrandingAssets(context, catalog, dirs);
        AttachCoachAssets(context, catalog, dirs);
        AttachPictograms(context, catalog, dirs);
        AttachMotionAssets(context, catalog, dirs);
    }

    private static void AttachAudioAssets(IntermediateAssetCatalog catalog, AssetDirectories dirs, AssetStagingArea staging)
    {
        string? master = CopyFirstFile(staging.AudioMaster, "*.opus", Path.Combine(dirs.Audio, "master.opus"));
        UpdateAssetReference(catalog, "audio/master", master, dirs, mimeType: "audio/ogg");

        string? preview = CopyFirstFile(staging.AudioPreview, "*.opus", Path.Combine(dirs.Audio, "preview.opus"));
        UpdateAssetReference(catalog, "audio/preview", preview, dirs, mimeType: "audio/ogg");
    }

    private static void AttachVideoAssets(IntermediateAssetCatalog catalog, AssetDirectories dirs, AssetStagingArea staging)
    {
        string? videoFolder = CopyDirectoryContent(staging.Video, dirs.Video);
        UpdateAssetReference(catalog, "video/background", videoFolder, dirs, treatAsDirectory: true);

        string? previewFolder = CopyDirectoryContent(staging.PreviewVideo, dirs.PreviewVideo);
        UpdateAssetReference(catalog, "video/preview", previewFolder, dirs, treatAsDirectory: true);
    }

    private static void AttachBrandingAssets(ConversionContext context, IntermediateAssetCatalog catalog, AssetDirectories dirs)
    {
        string? cover = ExportCoverImage(context, Path.Combine(dirs.Branding, "thumbnail.png"));
        UpdateAssetReference(catalog, "image/cover", cover, dirs, mimeType: "image/png");

        string? title = ExportSongTitleLogo(context, Path.Combine(dirs.Branding, "songTitleLogo.png"));
        UpdateAssetReference(catalog, "image/songTitleLogo", title, dirs, mimeType: "image/png");
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
        cover.Save(destination);
        return destination;
    }

    private static string? ExportSongTitleLogo(ConversionContext context, string destination)
    {
        UbiArtCoverRequest coverRequest = new(context.SongData, context.FileSystem.TempFolders.MenuArtFolder);

        using Image<Rgba32>? title = UbiArtCoverGenerator.ExistingSongTitleLogo(coverRequest);

        if (title == null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        title.Save(destination);
        return destination;
    }

    private static void AttachCoachAssets(ConversionContext context, IntermediateAssetCatalog catalog, AssetDirectories dirs)
    {
        string menuArtFolder = context.FileSystem.TempFolders.MenuArtFolder;
        if (!Directory.Exists(menuArtFolder))
        {
            UpdateAssetReference(catalog, "image/coachLarge", null, dirs);
            return;
        }

        Directory.CreateDirectory(dirs.Coaches);

        string[] coachFiles = Directory.EnumerateFiles(menuArtFolder, $"{context.SongData.Name}_Coach_*.png", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("_phone", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (coachFiles.Length == 0)
        {
            UpdateAssetReference(catalog, "image/coachLarge", null, dirs);
        }
        else
        {
            for (int i = 0; i < coachFiles.Length; i++)
            {
                string destination = Path.Combine(dirs.Coaches, $"coach_{i + 1:D2}.png");
                File.Copy(coachFiles[i], destination, true);
            }

            string? background = Directory.EnumerateFiles(menuArtFolder, "*map_bkg*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (background != null)
                File.Copy(background, Path.Combine(dirs.Coaches, "coachesBackground.png"), true);

            UpdateAssetReference(catalog, "image/coachLarge", dirs.Coaches, dirs, treatAsDirectory: true, mimeType: "image/png");
        }
    }

    private static void AttachPictograms(ConversionContext context, IntermediateAssetCatalog catalog, AssetDirectories dirs)
    {
        string pictoFolder = context.FileSystem.TempFolders.PictoFolder;
        if (!Directory.Exists(pictoFolder))
        {
            UpdateAssetReference(catalog, "atlas/pictograms", null, dirs);
            return;
        }

        Directory.CreateDirectory(dirs.Pictograms);
        foreach (string file in Directory.EnumerateFiles(pictoFolder, "*.png", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(file).StartsWith("atlas_", StringComparison.OrdinalIgnoreCase))
                continue;
            string destination = Path.Combine(dirs.Pictograms, Path.GetFileName(file));
            File.Copy(file, destination, true);
        }

        if (!Directory.EnumerateFiles(dirs.Pictograms).Any())
        {
            UpdateAssetReference(catalog, "atlas/pictograms", null, dirs);
            return;
        }

        UpdateAssetReference(catalog, "atlas/pictograms", dirs.Pictograms, dirs, treatAsDirectory: true, mimeType: "image/png");
    }

    private static void AttachMotionAssets(ConversionContext context, IntermediateAssetCatalog catalog, AssetDirectories dirs)
    {
        string? movesPath = CopyCookedFiles(context, context.FileSystem.InputFolders.MovesFolder, "*.msm", dirs.Moves);
        UpdateAssetReference(catalog, "motion/msm", movesPath, dirs, treatAsDirectory: true);

        string gesturesRelative = Path.Combine(context.FileSystem.InputFolders.TimelineFolder, "gestures");
        string? gesturesPath = CopyCookedFiles(context, gesturesRelative, "*.gesture", dirs.Gestures);
        UpdateAssetReference(catalog, "motion/gestures", gesturesPath, dirs, treatAsDirectory: true);
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

    private static void UpdateAssetReference(IntermediateAssetCatalog catalog, string role, string? absolutePath, AssetDirectories dirs, string? mimeType = null, bool treatAsDirectory = false)
    {
        IntermediateAsset? asset = catalog.Assets.FirstOrDefault(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase));
        if (asset == null)
            return;

        if (absolutePath == null)
        {
            asset.Required = false;
            asset.SourcePath = null;
            asset.SizeBytes = null;
            asset.MimeType = null;
            return;
        }

        asset.SourcePath = dirs.ToRelative(absolutePath);
        asset.MimeType = mimeType;
        asset.Required = true;
        asset.SizeBytes = treatAsDirectory
            ? Directory.EnumerateFiles(absolutePath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : new FileInfo(absolutePath).Length;
    }

    private sealed class AssetDirectories
    {
        private AssetDirectories(string packageRoot)
        {
            PackageRoot = packageRoot;
            Root = Path.Combine(packageRoot, "assets");
            Audio = Path.Combine(Root, "audio");
            Video = Path.Combine(Root, "video");
            PreviewVideo = Path.Combine(Root, "previewVideo");
            Branding = Path.Combine(Root, "branding");
            Coaches = Path.Combine(Root, "coaches");
            Pictograms = Path.Combine(Root, "pictograms");
            Moves = Path.Combine(Root, "moves");
            Gestures = Path.Combine(Root, "gestures");
        }

        public static AssetDirectories Create(string packageRoot) => new(packageRoot);

        public string PackageRoot { get; }
        public string Root { get; }
        public string Audio { get; }
        public string Video { get; }
        public string PreviewVideo { get; }
        public string Branding { get; }
        public string Coaches { get; }
        public string Pictograms { get; }
        public string Moves { get; }
        public string Gestures { get; }

        public void Reset()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
        }

        public string ToRelative(string absolutePath)
        {
            string relative = Path.GetRelativePath(PackageRoot, absolutePath);
            return relative.Replace('\\', '/');
        }
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
