using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;
using JustDanceEditor.Logging;

using System.Text.Json;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.Unity.Converters;

internal sealed class IntermediateToUnityConverter
{
    private readonly IntermediateSongPackage _package;
    private readonly string _packageRoot;
    private readonly UnityConversionRequest _request;
    private readonly TemplateSet _templates;
    private readonly string _songFolderName;
    private readonly string _outputRoot;

    static readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true
    };

    public IntermediateToUnityConverter(
        IntermediateSongPackage package,
        string packageRoot,
        UnityConversionRequest request)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _packageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.TemplatePath))
            throw new ArgumentException("Template path must be provided for Unity exports.");

        _templates = new TemplateSet(request.TemplatePath);
        _songFolderName = BuildSongFolderName(_package.Metadata);
        _outputRoot = Path.Combine(request.OutputPath, _songFolderName);
    }

    public async Task ConvertAsync()
    {
        Logger.Log($"Starting JDI → Unity conversion for '{_songFolderName}'.", LogLevel.Info);

        Directory.CreateDirectory(_outputRoot);
        Logger.Log("Preparing assets and metadata in parallel...", LogLevel.Debug);
        
        // Run audio, video, and SongInfo generation in parallel
        Task audioTask = CopyAudioAssetsAsync();
        Task videoTask = CopyVideoAssetsAsync();
        Task songInfoTask = GenerateSongInfoAsync();
        await Task.WhenAll(audioTask, videoTask, songInfoTask);
        
        Logger.Log("Building Unity bundles...", LogLevel.Debug);
        await BuildUnityBundlesAsync();
        Logger.Log($"Unity conversion for '{_songFolderName}' completed.", LogLevel.Info);
    }

    private async Task CopyAudioAssetsAsync()
    {
        await EnsurePreviewAudioAsync();

        // Master audio always comes from assets (original)
        CopyHashedFile(IntermediatePackageLayout.Assets.AudioMasterFile, Path.Combine(_outputRoot, "Audio_opus"), ".opus");
        
        // Preview audio: check assets first, then scratch
        string assetsPreviewPath = ResolvePackagePath(IntermediatePackageLayout.Assets.AudioPreviewFile);
        string scratchPreviewPath = Path.Combine(GetScratchAudioFolder(), "preview.opus");
        
        string? sourcePreviewPath = null;
        if (File.Exists(assetsPreviewPath))
            sourcePreviewPath = assetsPreviewPath;
        else if (File.Exists(scratchPreviewPath))
            sourcePreviewPath = scratchPreviewPath;

        if (sourcePreviewPath != null)
        {
            string hashName = BuildHashedFileName(sourcePreviewPath, ".opus");
            string destinationPath = Path.Combine(_outputRoot, "AudioPreview_opus", hashName);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePreviewPath, destinationPath, true);
        }
        else
        {
            Logger.Log("Preview audio not found in assets or scratch; Unity export may be incomplete.", LogLevel.Warning);
        }
    }

    private async Task CopyVideoAssetsAsync()
    {
        // Generate background and preview videos in parallel
        Task backgroundTask = JdiVideoConverter.EnsureBackgroundVideosAsync(_packageRoot);
        Task previewTask = JdiVideoConverter.EnsurePreviewVideosAsync(_package, _packageRoot);
        await Task.WhenAll(backgroundTask, previewTask);
        
        CopyBackgroundVideos(Path.Combine(_outputRoot, "video"));
        CopyPreviewVideos(Path.Combine(_outputRoot, "videoPreview"));
    }

    private async Task EnsurePreviewAudioAsync()
    {
        await JdiVideoConverter.EnsureFFmpegInitializedAsync();

        string assetsPreviewPath = ResolvePackagePath(IntermediatePackageLayout.Assets.AudioPreviewFile);
        string scratchPreviewPath = Path.Combine(GetScratchAudioFolder(), "preview.opus");

        // Check if original exists in assets (source-of-truth)
        if (File.Exists(assetsPreviewPath))
        {
            Logger.Log("Source-of-truth preview audio found in assets.", LogLevel.Debug);
            return;
        }

        // Check if already generated in scratch
        if (File.Exists(scratchPreviewPath))
        {
            Logger.Log("Preview audio already exists in scratch.", LogLevel.Debug);
            return;
        }

        // Generate in scratch from master audio
        string masterPath = ResolvePackagePath(IntermediatePackageLayout.Assets.AudioMasterFile);
        if (!File.Exists(masterPath))
        {
            Logger.Log("Cannot generate preview audio because master.opus is missing.", LogLevel.Warning);
            return;
        }

        string scratchFolder = GetScratchAudioFolder();
        Directory.CreateDirectory(scratchFolder);
        (TimeSpan start, TimeSpan duration) = _package.TimelineStructure.GetAudioPreviewTiming();

        string args = FormattableString.Invariant($"-ss {start.TotalSeconds} -i \"{masterPath}\" -c:a libopus -ar 48000 -t {duration.TotalSeconds} -af \"afade=t=in:st=0:d=1,afade=t=out:st={Math.Max(0, duration.TotalSeconds - 1)}:d=1\" -y \"{scratchPreviewPath}\"");

        IConversion conversion = FFmpeg.Conversions.New();
        await conversion.Start(args);
        Logger.Log("Generated preview audio in scratch from master.opus.", LogLevel.Info);
    }

    private async Task GenerateSongInfoAsync()
    {
        ServerSongJSON doc = (ServerSongJSON)_package.Metadata;
        string jsonPath = Path.Combine(_outputRoot, "SongInfo.json");
        await using FileStream stream = File.Create(jsonPath);
        await JsonSerializer.SerializeAsync(stream, doc, serializerOptions);
    }

    private async Task BuildUnityBundlesAsync()
    {
        UnityExportData unityData = UnityExportDataBuilder.Create(_package);
        UnityMenuArtSource menuArt = BuildMenuArtSource();
        string songName = ResolveSongName(unityData);
        int coachCount = Math.Max(1, unityData.Metadata.CoachCount);
        bool forCustomServer = _request.ExportType == ExportType.CustomServer;

        using UnityBundleWorkspace workspace = new(_songFolderName);

        string coverFolder = EnsureOutputFolder("Cover");
        string songTitleFolder = EnsureOutputFolder("songTitleLogo");
        string coachesLargeFolder = EnsureOutputFolder("CoachesLarge");
        string coachesSmallFolder = EnsureOutputFolder("CoachesSmall");
        string mapPackageFolder = EnsureOutputFolder("MapPackage");

        string[] pictoFiles = ResolvePictoFiles();
        string? movesFolder = ResolveMovesFolder();

        UnityCoverRequest coverRequest = new(
            songName,
            unityData,
            menuArt,
            _request.OnlineCover,
            _templates.Cover,
            coverFolder,
            forCustomServer);

        UnitySongTitleRequest songTitleRequest = new(
            songName,
            unityData,
            menuArt,
            _request.OnlineCover,
            _templates.SongTitleLogo,
            songTitleFolder,
            forCustomServer);

        UnityCoachesLargeRequest coachesLargeRequest = new(
            songName,
            coachCount,
            menuArt,
            unityData,
            _templates.CoachesLarge,
            coachesLargeFolder,
            forCustomServer);

        UnityCoachesSmallRequest coachesSmallRequest = new(
            songName,
            coachCount,
            menuArt,
            _templates.CoachesSmall,
            coachesSmallFolder,
            forCustomServer);

        UnityMapPackageRequest mapPackageRequest = new(
            songName,
            unityData,
            pictoFiles,
            workspace.PictoTempFolder,
            workspace.PictoAtlasFolder,
            movesFolder,
            _templates.MapPackage,
            mapPackageFolder,
            forCustomServer);

        Logger.Log("Dispatching Unity bundle builders (cover, title, coaches, map package)...", LogLevel.Debug);
        Task coverTask = CoverBundleBuilder.GenerateAsync(coverRequest);
        Task titleTask = SongTitleBundleBuilder.GenerateAsync(songTitleRequest);
        Task coachesLargeTask = CoachesLargeBundleBuilder.GenerateAsync(coachesLargeRequest);
        Task coachesSmallTask = CoachesSmallBundleBuilder.GenerateAsync(coachesSmallRequest);
        Task mapPackageTask = MapPackageBundleBuilder.GenerateAsync(mapPackageRequest);

        await Task.WhenAll(mapPackageTask, coverTask, titleTask, coachesLargeTask, coachesSmallTask);
        Logger.Log("Unity bundle generation finished.", LogLevel.Debug);
    }

    private void CopyHashedFile(string relativeSourceFile, string destinationFolder, string extension)
    {
        string source = ResolvePackagePath(relativeSourceFile);
        if (!File.Exists(source))
        {
            Logger.Log($"Expected asset '{relativeSourceFile}' does not exist; skipping copy.", LogLevel.Warning);
            return;
        }

        Directory.CreateDirectory(destinationFolder);
        string hashName = BuildHashedFileName(source, extension);
        File.Copy(source, Path.Combine(destinationFolder, hashName), true);
        Logger.Log($"Copied '{relativeSourceFile}' to '{destinationFolder}'.", LogLevel.Debug);
    }

    private void CopyBackgroundVideos(string destinationFolder)
    {
        string assetsDir = ResolvePackagePath(IntermediatePackageLayout.Assets.VideoFolder);
        string[] assetSources = GetVideoFiles(assetsDir);
        
        // If assets has 4 master videos, they're source-of-truth originals
        string[] assetMasters = assetSources
            .Where(f => Path.GetFileName(f).StartsWith("master_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
            
        if (assetMasters.Length == 4)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in assetMasters)
                CopyHashedAsset(source, destinationFolder, ".webm");
            Logger.Log("Detected source-of-truth background videos (4 variants) in assets. Copied all variants.", LogLevel.Info);
            return;
        }

        // Otherwise check scratch for generated master videos
        string scratchDir = GetScratchVideoFolder();
        string[] scratchSources = GetVideoFiles(scratchDir);
        string[] scratchMasters = scratchSources
            .Where(f => Path.GetFileName(f).StartsWith("master_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
            
        if (scratchMasters.Length == 4)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in scratchMasters)
                CopyHashedAsset(source, destinationFolder, ".webm");
            Logger.Log("Using generated background videos (4 variants) from scratch.", LogLevel.Info);
            return;
        }

        // Fallback: copy largest available from assets
        if (assetSources.Length > 0)
        {
            Directory.CreateDirectory(destinationFolder);
            string largest = assetSources.OrderByDescending(f => new FileInfo(f).Length).First();
            File.Copy(largest, Path.Combine(destinationFolder, "master.webm"), true);
            Logger.Log("Background videos incomplete; exported single master.webm from largest available file in assets.", LogLevel.Warning);
            return;
        }

        Logger.Log("No background videos found in assets or scratch; Unity export will lack video assets.", LogLevel.Warning);
    }

    private void CopyPreviewVideos(string destinationFolder)
    {
        int expectedCount = UnityVideoProfiles.Previews.Length;
        
        string assetsDir = ResolvePackagePath(IntermediatePackageLayout.Assets.PreviewVideoFolder);
        string[] assetSources = GetVideoFiles(assetsDir);
        
        // If assets has expected preview videos, they're source-of-truth originals
        if (assetSources.Length == expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in assetSources)
                CopyHashedAsset(source, destinationFolder, ".webm");
            Logger.Log($"Detected source-of-truth preview videos ({expectedCount} variants) in assets. Copied all variants.", LogLevel.Info);
            return;
        }

        // Otherwise use scratch for generated previews (filter by preview_ prefix)
        string scratchDir = GetScratchVideoFolder();
        string[] scratchSources = GetVideoFiles(scratchDir)
            .Where(f => Path.GetFileName(f).StartsWith("preview_", StringComparison.OrdinalIgnoreCase))
            .ToArray();
            
        if (scratchSources.Length == expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in scratchSources)
                CopyHashedAsset(source, destinationFolder, ".webm");
            Logger.Log($"Using generated preview videos ({expectedCount} variants) from scratch.", LogLevel.Info);
            return;
        }

        Logger.Log($"Preview videos not found in assets or scratch (expected {expectedCount}, found {scratchSources.Length} in scratch); Unity export may be incomplete.", LogLevel.Warning);
    }

    private void CopyHashedAsset(string sourceFile, string destinationFolder, string extension)
    {
        string hashName = BuildHashedFileName(sourceFile, extension);
        string destinationPath = Path.Combine(destinationFolder, hashName);

        if (File.Exists(destinationPath))
        {
            string name = Path.GetFileNameWithoutExtension(hashName);
            string ext = Path.GetExtension(hashName);
            hashName = $"{name}_{Guid.NewGuid():N}{ext}";
            destinationPath = Path.Combine(destinationFolder, hashName);
        }

        File.Copy(sourceFile, destinationPath, true);
    }

    private string ResolvePackagePath(string relativePath) => IntermediatePackageLayout.Resolve(_packageRoot, relativePath);

    private string? GetAssetFileIfExists(string relativeFile)
    {
        string path = ResolvePackagePath(relativeFile);
        return File.Exists(path) ? path : null;
    }

    private string? GetAssetFolder(string relativeFolder)
    {
        string path = ResolvePackagePath(relativeFolder);
        return Directory.Exists(path) ? path : null;
    }

    private string? FindFirstFileInFolder(string relativeFolder, params string[] patterns)
    {
        string? folder = GetAssetFolder(relativeFolder);
        if (folder == null)
            return null;

        string[] searchPatterns = patterns.Length == 0 ? ["*"] : patterns;
        foreach (string pattern in searchPatterns)
        {
            string? match = Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (match != null)
                return match;
        }

        return null;
    }

    private string BuildHashedFileName(string sourceFile, string extension)
    {
        string hash = FileHashing.GetFileMD5(sourceFile);
        if (_request.ExportType == ExportType.CustomServer && !string.IsNullOrEmpty(extension))
            hash += extension;
        return hash;
    }

    private string GetScratchAudioFolder() => Path.Combine(_packageRoot, "scratch", "audio");

    private string GetScratchVideoFolder() => Path.Combine(_packageRoot, "scratch", "video");

    private static string[] GetVideoFiles(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Array.Empty<string>();

        string[] allowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
        return [.. Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)];
    }

    private static string BuildSongFolderName(IntermediateMetadata metadata)
    {
        string? codeName = string.IsNullOrWhiteSpace(metadata.MapName) ? metadata.Title : metadata.MapName;
        if (string.IsNullOrWhiteSpace(codeName))
            codeName = "Song";

        foreach (char c in Path.GetInvalidFileNameChars())
            codeName = codeName.Replace(c, '_');
        return codeName.Trim();
    }

    private UnityMenuArtSource BuildMenuArtSource()
    {
        string? coverPath = GetAssetFileIfExists(IntermediatePackageLayout.Assets.CoverFile);
        string? titlePath = GetAssetFileIfExists(IntermediatePackageLayout.Assets.SongTitleFile);
        string? backgroundPath = ResolveCoachBackground();
        IReadOnlyList<string> coachImages = ResolveCoachImages();

        if (coverPath == null)
            Logger.Log("Cover art asset missing from intermediate package.", LogLevel.Warning);
        if (titlePath == null)
            Logger.Log("Song title logo asset missing from intermediate package.", LogLevel.Warning);
        if (coachImages.Count == 0)
            Logger.Log("Coach art assets missing from intermediate package.", LogLevel.Warning);
        if (backgroundPath == null)
            Logger.Log("Coach background asset missing from intermediate package.", LogLevel.Warning);

        return new UnityMenuArtSource(coverPath, titlePath, backgroundPath, coachImages);
    }

    private IReadOnlyList<string> ResolveCoachImages()
    {
        string? coachesDir = GetAssetFolder(IntermediatePackageLayout.Assets.CoachesFolder);
        if (coachesDir == null)
            return Array.Empty<string>();

        string[] candidates = [.. Directory.EnumerateFiles(coachesDir, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !IsCoachBackgroundAsset(file))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];

        return candidates;
    }

    private string? ResolveCoachBackground()
    {
        string[] patterns =
        [
            $"{_songFolderName}_map_bkg*.*",
            "coachesBackground*.*",
            "coachBackground*.*",
            "*map_bkg*.*"
        ];

        return GetAssetFileIfExists(IntermediatePackageLayout.Assets.CoachesBackgroundFile)
            ?? FindFirstFileInFolder(IntermediatePackageLayout.Assets.CoachesFolder, patterns)
            ?? FindFirstFileInFolder(IntermediatePackageLayout.Assets.CoverAssetsFolder, patterns);
    }

    private string ResolveSongName(UnityExportData unityData)
    {
        if (!string.IsNullOrWhiteSpace(unityData?.Name))
            return unityData.Name;
        return _songFolderName;
    }

    private string EnsureOutputFolder(string relativePath)
    {
        string path = Path.Combine(_outputRoot, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private string[] ResolvePictoFiles()
    {
        string? folder = GetAssetFolder(IntermediatePackageLayout.Assets.PictogramsFolder);
        if (folder == null)
        {
            Logger.Log("Intermediate package missing pictogram folder; map package may lack pictos.", LogLevel.Warning);
            return Array.Empty<string>();
        }

        string[] files = [.. Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];

        if (files.Length == 0)
            Logger.Log("Intermediate pictogram folder is empty; map package may lack pictos.", LogLevel.Warning);

        return files;
    }

    private string? ResolveMovesFolder()
    {
        string? folder = GetAssetFolder(IntermediatePackageLayout.Assets.MovesFolder);
        if (folder == null)
            Logger.Log("Intermediate package missing moves folder; MSM scripts will be omitted.", LogLevel.Warning);
        return folder;
    }

    private static bool IsCoachBackgroundAsset(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("map_bkg", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("coachesbackground", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("coachbackground", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectorySafe(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException ex)
        {
            Logger.Log($"Failed to delete temporary folder '{path}': {ex.Message}", LogLevel.Warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log($"Failed to delete temporary folder '{path}': {ex.Message}", LogLevel.Warning);
        }
    }

    private sealed class UnityBundleWorkspace : IDisposable
    {
        public UnityBundleWorkspace(string songFolder)
        {
            Root = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "Unity", songFolder, "Bundles", Guid.NewGuid().ToString("N"));
            PictoTempFolder = Path.Combine(Root, "PictosWork");
            PictoAtlasFolder = Path.Combine(Root, "PictosAtlas");

            Directory.CreateDirectory(PictoTempFolder);
            Directory.CreateDirectory(PictoAtlasFolder);
        }

        public string Root { get; }
        public string PictoTempFolder { get; }
        public string PictoAtlasFolder { get; }

        public void Dispose() => TryDeleteDirectorySafe(Root);
    }

    private sealed class TemplateSet
    {
        public TemplateSet(string root)
        {
            string Resolve(string name)
            {
                string folder = Path.Combine(root, name);
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException($"Missing template folder '{folder}'.");
                string file = Directory.EnumerateFiles(folder).OrderBy(f => f).FirstOrDefault() ?? throw new FileNotFoundException($"Template folder '{folder}' does not contain any files.");
                return file;
            }

            Cover = Resolve("Cover");
            SongTitleLogo = Resolve("SongTitleLogo");
            CoachesLarge = Resolve("CoachesLarge");
            CoachesSmall = Resolve("CoachesSmall");
            MapPackage = Resolve("MapPackage");
        }

        public string Cover { get; }
        public string SongTitleLogo { get; }
        public string CoachesLarge { get; }
        public string CoachesSmall { get; }
        public string MapPackage { get; }
    }
}