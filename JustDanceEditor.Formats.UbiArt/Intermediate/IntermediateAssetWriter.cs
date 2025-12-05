using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.UbiArt.Audio;
using JustDanceEditor.Formats.UbiArt.Core;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Images;
using JustDanceEditor.Formats.UbiArt.Video;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.UbiArt.Intermediate;

internal static class IntermediateAssetWriter
{
    static ImageEncoder Encoder => JDI.Utilities.WebpSettings.LosslessWebpEncoder;

    public static async Task PopulateFromUbiArtAsync(ConversionContext context, IntermediateSongPackage package, string packageRoot)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        context.IntermediatePackage ??= package;

        ResetAssetsRoot(packageRoot);

        await EnsurePrerequisitesAsync();
        await ConvertPictogramsAsync(context, packageRoot);
        JDUbiArtSong songData = context.SongData ?? throw new InvalidOperationException("Song data not loaded.");

        string audioMasterFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder);
        string audioPreviewFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.AudioFolder);

        ConversionRequest jdiRequest = new()
        {
            InputPath = context.Request.InputPath,
            OutputPath = context.Request.OutputPath,
            TemplatePath = context.Request.TemplatePath,
            OnlineCover = context.Request.OnlineCover,
            SongName = context.Request.SongName,
            SongGUID = context.Request.SongGUID,
            CacheNumber = context.Request.CacheNumber,
            JDVersion = context.Request.JDVersion
        };

        await AudioConverter.ConvertAudioAsync(songData, context.FileSystem, jdiRequest, new AudioConversionOptions
        {
            MasterOutputFolder = audioMasterFolder,
            PreviewOutputFolder = audioPreviewFolder,
        });

        string videoFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string previewVideoFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);

        await VideoConverter.ConvertVideoAsync(songData, context.FileSystem, new VideoConversionOptions
        {
            VideoOutputFolder = videoFolder,
            PreviewOutputFolder = previewVideoFolder,
        });

        CopyAssetsToPackage(context, packageRoot);
    }

    private static async Task EnsurePrerequisitesAsync()
    {
        if (!File.Exists("ffmpeg.exe") && !File.Exists("ffmpeg"))
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
    }

    private static async Task ConvertPictogramsAsync(ConversionContext context, string packageRoot)
    {
        CookedFile[] pictoFiles = context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.PictosFolder);
        string[] pictoPaths = [.. pictoFiles.Select(file => (string)file)];

        string outputFolder = EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.PictogramsFolder);

        UbiArtPictoConversionRequest request = new(
            context.SongData,
            pictoPaths,
            outputFolder);

        await Task.Run(() => UbiArtPictoConverter.Convert(request));
    }

    private static void CopyAssetsToPackage(ConversionContext context, string packageRoot)
    {
        AttachBrandingAssets(context, packageRoot);
        AttachCoachAssets(context, packageRoot);
        AttachMotionAssets(context, packageRoot);
    }

    private static void AttachBrandingAssets(ConversionContext context, string packageRoot)
    {
        EnsureFolder(packageRoot, IntermediatePackageLayout.Assets.CoverAssetsFolder);
        ExportCoverImage(context, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoverFile));
    }

    private static string? ExportCoverImage(ConversionContext context, string destination)
    {
        Logger.Log($"Attempting to export cover image from menu art folder: {context.FileSystem.InputFolders.MenuArtFolder}", LogLevel.Debug);

        Image<Bgra32>? cover = UbiArtCoverGenerator.ExistingCover(context);
        
        if (cover != null)
        {
            Logger.Log("Using existing cover image", LogLevel.Info);
        }
        else
        {
            Logger.Log("No existing cover found, generating own cover", LogLevel.Info);
            cover = UbiArtCoverGenerator.GenerateOwnCover(context);
        }

        if (cover == null)
        {
            Logger.Log("Cover art could not be prepared for intermediate export.", LogLevel.Warning);
            return null;
        }

        using (cover)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            cover.Save(destination, Encoder);
            Logger.Log($"Saved cover image to: {destination}", LogLevel.Info);
        }
        
        return destination;
    }

    private static void AttachCoachAssets(ConversionContext context, string packageRoot)
    {
        CookedFile[] coachFilesCooked = [.. context.FileSystem.GetAllFiles(context.FileSystem.InputFolders.MenuArtFolder, $"{context.SongData.Name}_coach_*")
            .Where(file => !file.Name.EndsWith("_phone", StringComparison.OrdinalIgnoreCase))];

        if (coachFilesCooked.Length == 0)
        {
            Logger.Log($"No coach files found matching pattern '{context.SongData.Name}_coach_*' in {context.FileSystem.InputFolders.MenuArtFolder}", LogLevel.Info);
            return;
        }

        Logger.Log($"Found {coachFilesCooked.Length} coach file(s) to process", LogLevel.Info);
        for (int i = 0; i < coachFilesCooked.Length; i++)
        {
            string destination = Path.Combine(ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesFolder), $"coach_{i + 1:D2}.webp");
            using Image<Bgra32> coach = TextureConverter.TextureConverter.ConvertToImage(coachFilesCooked[i]);
            SaveAsWebp(coach, destination);
            Logger.Log($"Saved coach asset: {Path.GetFileName(destination)}", LogLevel.Debug);
        }

        using Image<Bgra32> backgroundImage = UbiArtCoverGenerator.GetBackground(context);
        SaveAsWebp(backgroundImage, ResolvePackagePath(packageRoot, IntermediatePackageLayout.Assets.CoachesBackgroundFile));
        Logger.Log($"Saved coaches background", LogLevel.Info);
    }

    private static void AttachMotionAssets(ConversionContext context, string packageRoot)
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

    private static void SaveAsWebp(Image<Bgra32> image, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        image.Save(destination, Encoder);
    }
}