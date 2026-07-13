using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Bundles.Synthesis;
using JustDanceEditor.Formats.Unity.Cache;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;

using Microsoft.Extensions.Logging;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Converters;

internal sealed class IntermediateToUnityConverter
{
    private readonly IntermediateSongPackage _package;
    private readonly string _packageRoot;
    private readonly UnityConversionRequest _request;
    private readonly string _songFolderName;
    private string _outputRoot;
    private readonly ILogger _logger;
    private readonly IMediaProcessor _mediaProcessor;

    static readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IntermediateToUnityConverter(
        IntermediateSongPackage package,
        string packageRoot,
        UnityConversionRequest request,
        ILogger logger,
        IMediaProcessor? mediaProcessor = null)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _packageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mediaProcessor = mediaProcessor ?? new DefaultMediaProcessor();
        _songFolderName = BuildSongFolderName(_package.Metadata);
        _outputRoot = Path.Combine(request.OutputPath, _songFolderName);
    }

    public async Task ConvertAsync()
    {
        _logger.LogInformation("Starting JDI → Unity conversion for '{SongFolder}'", _songFolderName);

        if (_request.ExportType == ExportType.OfflineCache)
        {
            await ConvertOfflineCacheAsync();
            return;
        }

        Directory.CreateDirectory(_outputRoot);
        await GenerateUnityOutputAsync(includeSongInfo: true);
        _logger.LogInformation("Unity conversion for '{SongFolder}' completed.", _songFolderName);
    }

    private async Task ConvertOfflineCacheAsync()
    {
        uint cacheNumber = _request.CacheNumber ?? throw new InvalidOperationException("Unity offline cache export requires a cache number.");
        string selectedOutputPath = _request.OutputPath;
        string cacheRoot = await UnityCacheLayout.ResolveOrCreateAsync(selectedOutputPath, _request, _logger).ConfigureAwait(false);
        string stagingRoot = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "Unity", "OfflineCache", Guid.NewGuid().ToString("N"));
        _outputRoot = Path.Combine(stagingRoot, _songFolderName);

        try
        {
            Directory.CreateDirectory(_outputRoot);
            await GenerateUnityOutputAsync(includeSongInfo: false);

            UnityOfflineCacheExporter.Publish(_package, _outputRoot, cacheRoot, cacheNumber, _logger);
            _logger.LogInformation("Unity offline cache conversion for '{SongFolder}' completed.", _songFolderName);
        }
        finally
        {
            TryDeleteDirectorySafe(stagingRoot, _logger);
        }
    }

    private async Task GenerateUnityOutputAsync(bool includeSongInfo)
    {
        _logger.LogDebug("Preparing assets and metadata in parallel...");

        // Run audio, video, and optional SongInfo generation in parallel.
        Task audioTask = CopyAudioAssetsAsync();
        Task videoTask = CopyVideoAssetsAsync();
        Task songInfoTask = includeSongInfo ? GenerateSongInfoAsync() : Task.CompletedTask;
        await Task.WhenAll(audioTask, videoTask, songInfoTask);

        await EnsureMissingImageAssetsAsync();

        _logger.LogDebug("Building Unity bundles...");
        await BuildUnityBundlesAsync();
    }

    private async Task EnsureMissingImageAssetsAsync()
    {
        IntermediateImageService imageService = new(
            _packageRoot,
            _package,
            new SystemFileSystem(),
            logger: _logger);

        await imageService.GenerateAllMissingImagesAsync();
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
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException($"Could not determine the directory for '{destinationPath}'."));
            File.Copy(sourcePreviewPath, destinationPath, true);
        }
        else
        {
            _logger.LogWarning("Preview audio not found in assets or scratch; Unity export may be incomplete.");
        }
    }

    private async Task CopyVideoAssetsAsync()
    {
        if (_request.ExportType == ExportType.OfflineCache)
        {
            await CopyOfflineCacheVideoAssetsAsync();
            return;
        }

        // Generate background and preview videos in parallel
        Task backgroundTask = JdiVideoConverter.EnsureBackgroundVideosAsync(_packageRoot, _logger);
        Task previewTask = JdiVideoConverter.EnsurePreviewVideosAsync(_package, _packageRoot, _logger);
        await Task.WhenAll(backgroundTask, previewTask);

        CopyBackgroundVideos(Path.Combine(_outputRoot, "video"));
        CopyPreviewVideos(Path.Combine(_outputRoot, "videoPreview"));
    }

    private async Task CopyOfflineCacheVideoAssetsAsync()
    {
        string backgroundDestination = Path.Combine(_outputRoot, "video");
        string previewDestination = Path.Combine(_outputRoot, "videoPreview");
        string assetsVideoFolder = ResolvePackagePath(IntermediatePackageLayout.Assets.VideoFolder);
        string assetsPreviewFolder = ResolvePackagePath(IntermediatePackageLayout.Assets.PreviewVideoFolder);

        string? backgroundSource = await SelectBestOfflineCacheVideoSourceAsync(GetVideoFiles(assetsVideoFolder));
        if (backgroundSource is not null)
        {
            string? normalizedBackground = await EnsureOfflineCacheVideoAsync(
                "background",
                backgroundSource,
                start: default,
                duration: default);

            if (normalizedBackground is not null)
            {
                Directory.CreateDirectory(backgroundDestination);
                CopyHashedAsset(normalizedBackground, backgroundDestination, ".webm");
            }
        }
        else
        {
            _logger.LogWarning("No background video found for Unity offline cache export.");
        }

        string? previewSource = await SelectBestOfflineCacheVideoSourceAsync(GetVideoFiles(assetsPreviewFolder));
        if (previewSource is not null)
        {
            string? normalizedPreview = await EnsureOfflineCacheVideoAsync(
                "preview",
                previewSource,
                start: default,
                duration: default);

            if (normalizedPreview is not null)
            {
                Directory.CreateDirectory(previewDestination);
                CopyHashedAsset(normalizedPreview, previewDestination, ".webm");
            }

            return;
        }

        if (backgroundSource is null)
        {
            _logger.LogWarning("No preview video source found for Unity offline cache export.");
            return;
        }

        (TimeSpan previewStart, TimeSpan previewDuration) = _package.TimelineStructure.GetVideoPreviewTiming();
        string? generatedPreview = await EnsureOfflineCacheVideoAsync(
            "preview",
            backgroundSource,
            previewStart,
            previewDuration);

        if (generatedPreview is not null)
        {
            Directory.CreateDirectory(previewDestination);
            CopyHashedAsset(generatedPreview, previewDestination, ".webm");
        }
    }

    private async Task<string?> SelectBestOfflineCacheVideoSourceAsync(string[] sources)
    {
        if (sources.Length == 0)
            return null;

        string[] candidates = [.. sources.OrderByDescending(file => new FileInfo(file).Length).ThenBy(file => file, StringComparer.OrdinalIgnoreCase)];
        foreach (string candidate in candidates)
        {
            JdiVideoInfo? info = await JdiVideoConverter.TryInspectVideoAsync(candidate);
            if (IsOfflineCacheCompatibleVideo(candidate, info))
                return candidate;
        }

        return candidates[0];
    }

    private async Task<string?> EnsureOfflineCacheVideoAsync(string role, string sourcePath, TimeSpan start, TimeSpan duration)
    {
        JdiVideoInfo? info = await JdiVideoConverter.TryInspectVideoAsync(sourcePath);
        if (info is not null &&
            start <= TimeSpan.Zero &&
            duration <= TimeSpan.Zero &&
            IsOfflineCacheCompatibleVideo(sourcePath, info))
        {
            _logger.LogDebug("Using offline cache {Role} video as-is from '{VideoFile}'.", role, Path.GetFileName(sourcePath));
            return sourcePath;
        }

        string codec = IsVp8OrVp9(info?.Codec) ? info!.Codec : "vp9";
        JdiVideoTransform transform = BuildSixteenNineTransform(info);
        string cacheFileName = BuildOfflineCacheVideoCacheFileName(role, sourcePath, start, duration, codec, transform);

        _logger.LogInformation("Normalizing Unity offline cache {Role} video from '{VideoFile}' as {Codec} 16:9 WebM.", role, Path.GetFileName(sourcePath), codec);
        return await _mediaProcessor.GetOrCreateVideoAsync(
            new JdiVideoEncodeRequest(_packageRoot, cacheFileName, ".webm", codec)
            {
                SourcePath = sourcePath,
                Transform = transform,
                Start = start,
                Duration = duration,
                Encoding = new JdiVideoEncodingSettings
                {
                    PixelFormat = "yuv420p"
                }
            },
            _logger);
    }

    private async Task EnsurePreviewAudioAsync()
    {
        string assetsPreviewPath = ResolvePackagePath(IntermediatePackageLayout.Assets.AudioPreviewFile);
        string scratchPreviewPath = Path.Combine(GetScratchAudioFolder(), "preview.opus");

        // Check if original exists in assets (source-of-truth)
        if (File.Exists(assetsPreviewPath))
        {
            _logger.LogDebug("Source-of-truth preview audio found in assets.");
            return;
        }

        // Check if already generated in scratch
        if (File.Exists(scratchPreviewPath))
        {
            _logger.LogDebug("Preview audio already exists in scratch.");
            return;
        }

        // Generate in scratch from master audio
        string masterPath = ResolvePackagePath(IntermediatePackageLayout.Assets.AudioMasterFile);
        if (!File.Exists(masterPath))
        {
            _logger.LogWarning("Cannot generate preview audio because master.opus is missing.");
            return;
        }

        string scratchFolder = GetScratchAudioFolder();
        Directory.CreateDirectory(scratchFolder);
        (TimeSpan start, TimeSpan duration) = _package.TimelineStructure.GetAudioPreviewTiming();
        await _mediaProcessor.EncodeAudioAsync(
            new JdiAudioEncodeRequest(masterPath)
            {
                Codec = "opus",
                SampleRate = 48000,
                Start = start,
                Duration = duration,
                FadeInDuration = TimeSpan.FromSeconds(1),
                FadeOutStart = TimeSpan.FromSeconds(Math.Max(0, duration.TotalSeconds - 1)),
                FadeOutDuration = TimeSpan.FromSeconds(1)
            },
            scratchPreviewPath);
        _logger.LogInformation("Generated preview audio in scratch from master.opus.");
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
        UnityExportData unityData = UnityExportDataBuilder.Create(_package, _logger);
        UnityMenuArtSource menuArt = BuildMenuArtSource();
        string songName = ResolveSongName(unityData);
        int coachCount = Math.Max(1, unityData.Metadata.CoachCount);
        bool forCustomServer = _request.ExportType == ExportType.CustomServer;

        using UnityBundleWorkspace workspace = new(_songFolderName);
        string mapPackageSeedPath = CreateSyntheticMapPackageSeed(workspace);

        string coverFolder = GetBundleOutputFolder("Cover", forCustomServer);
        string songTitleFolder = GetBundleOutputFolder("songTitleLogo", forCustomServer);
        string coachesLargeFolder = GetBundleOutputFolder("CoachesLarge", forCustomServer);
        string coachesSmallFolder = GetBundleOutputFolder("CoachesSmall", forCustomServer);
        string mapPackageFolder = GetBundleOutputFolder("MapPackage", forCustomServer);

        string[] pictoFiles = ResolvePictoFiles();
        string? movesFolder = ResolveMovesFolder();

        UnityCoverRequest coverRequest = new(
            songName,
            unityData,
            menuArt,
            coverFolder,
            forCustomServer,
            PublishTarget: CreateBundlePublishTarget("Cover", forCustomServer));

        UnitySongTitleRequest songTitleRequest = new(
            songName,
            unityData,
            menuArt,
            songTitleFolder,
            forCustomServer,
            PublishTarget: CreateBundlePublishTarget("songTitleLogo", forCustomServer));

        UnityCoachesLargeRequest coachesLargeRequest = new(
            songName,
            coachCount,
            menuArt,
            unityData,
            coachesLargeFolder,
            forCustomServer,
            CreateBundlePublishTarget("CoachesLarge", forCustomServer));

        UnityCoachesSmallRequest coachesSmallRequest = new(
            songName,
            coachCount,
            menuArt,
            coachesSmallFolder,
            forCustomServer,
            CreateBundlePublishTarget("CoachesSmall", forCustomServer));

        UnityMapPackageRequest mapPackageRequest = new(
            songName,
            unityData,
            pictoFiles,
            workspace.PictoTempFolder,
            workspace.PictoAtlasFolder,
            movesFolder,
            mapPackageSeedPath,
            mapPackageFolder,
            forCustomServer,
            CreateBundlePublishTarget("MapPackage", forCustomServer));

        _logger.LogDebug("Dispatching Unity bundle builders (cover, title, coaches, map package)...");
        Task coverTask = CoverBundleBuilder.GenerateAsync(coverRequest, _logger);
        Task titleTask = SongTitleBundleBuilder.GenerateAsync(songTitleRequest, _logger);
        Task coachesLargeTask = CoachesLargeBundleBuilder.GenerateAsync(coachesLargeRequest, _logger);
        Task coachesSmallTask = CoachesSmallBundleBuilder.GenerateAsync(coachesSmallRequest, _logger);
        Task mapPackageTask = MapPackageBundleBuilder.GenerateAsync(mapPackageRequest, _logger);

        await Task.WhenAll(mapPackageTask, coverTask, titleTask, coachesLargeTask, coachesSmallTask);
        _logger.LogDebug("Unity bundle generation finished.");
    }

    private static string CreateSyntheticMapPackageSeed(UnityBundleWorkspace workspace)
    {
        string path = Path.Combine(workspace.Root, "synthetic-map-package.bundle");
        using Stream classData = UnityClassDataProvider.OpenClassPackageStream();
        File.WriteAllBytes(path, UnitySyntheticBundleFactory.CreateMapPackageSeed(classData));
        return path;
    }

    private void CopyHashedFile(string relativeSourceFile, string destinationFolder, string extension)
    {
        string source = ResolvePackagePath(relativeSourceFile);
        if (!File.Exists(source))
        {
            _logger.LogWarning("Expected asset '{RelativeSourceFile}' does not exist; skipping copy.", relativeSourceFile);
            return;
        }

        Directory.CreateDirectory(destinationFolder);
        string hashName = BuildHashedFileName(source, extension);
        File.Copy(source, Path.Combine(destinationFolder, hashName), true);
        _logger.LogDebug("Copied '{RelativeSourceFile}' to '{DestinationFolder}'.", relativeSourceFile, destinationFolder);
    }

    private void CopyBackgroundVideos(string destinationFolder)
    {
        string assetsDir = ResolvePackagePath(IntermediatePackageLayout.Assets.VideoFolder);
        string[] assetSources = GetVideoFiles(assetsDir);

        // If assets has the full master set, they're source-of-truth originals.
        int expectedCount = JdiVideoProfiles.Masters.Length;
        if (assetSources.Length >= expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in assetSources)
                CopyHashedAsset(source, destinationFolder, ".webm");
            _logger.LogInformation("Detected source-of-truth background videos ({ExpectedCount} variants) in assets. Copied all variants.", expectedCount);
            return;
        }

        // Legacy generated scratch files use the master_ prefix.
        string[] assetMasters = [.. assetSources.Where(f => Path.GetFileName(f).StartsWith("master_", StringComparison.OrdinalIgnoreCase))];

        if (assetMasters.Length == expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in assetMasters)
                CopyHashedAsset(source, destinationFolder, ".webm");
            _logger.LogInformation("Detected source-of-truth background videos ({ExpectedCount} variants) in assets. Copied all variants.", expectedCount);
            return;
        }

        // Otherwise check scratch for generated master videos
        string scratchDir = GetScratchVideoFolder();
        string[] scratchSources = GetVideoFiles(scratchDir);
        string[] scratchMasters = [.. scratchSources.Where(f => Path.GetFileName(f).StartsWith("master_", StringComparison.OrdinalIgnoreCase))];

        if (scratchMasters.Length == expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in scratchMasters)
                CopyHashedAsset(source, destinationFolder, ".webm");
            _logger.LogInformation("Using generated background videos ({ExpectedCount} variants) from scratch.", expectedCount);
            return;
        }

        // Fallback: copy largest available from assets
        if (assetSources.Length > 0)
        {
            Directory.CreateDirectory(destinationFolder);
            string largest = assetSources.OrderByDescending(f => new FileInfo(f).Length).First();
            File.Copy(largest, Path.Combine(destinationFolder, "master.webm"), true);
            _logger.LogWarning("Background videos incomplete; exported single master.webm from largest available file in assets.");
            return;
        }

        _logger.LogWarning("No background videos found in assets or scratch; Unity export will lack video assets.");
    }

    private void CopyPreviewVideos(string destinationFolder)
    {
        int expectedCount = JdiVideoProfiles.Previews.Length;

        string assetsDir = ResolvePackagePath(IntermediatePackageLayout.Assets.PreviewVideoFolder);
        string[] assetSources = GetVideoFiles(assetsDir);

        // If assets has expected preview videos, they're source-of-truth originals
        if (assetSources.Length == expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in assetSources)
                CopyHashedAsset(source, destinationFolder, ".webm");
            _logger.LogInformation("Detected source-of-truth preview videos ({ExpectedCount} variants) in assets. Copied all variants.", expectedCount);
            return;
        }

        // Otherwise use scratch for generated previews (filter by preview_ prefix)
        string scratchDir = GetScratchVideoFolder();
        string[] scratchSources = [.. GetVideoFiles(scratchDir).Where(f => Path.GetFileName(f).StartsWith("preview_", StringComparison.OrdinalIgnoreCase))];

        if (scratchSources.Length == expectedCount)
        {
            Directory.CreateDirectory(destinationFolder);
            foreach (string source in scratchSources)
                CopyHashedAsset(source, destinationFolder, ".webm");
            _logger.LogInformation("Using generated preview videos ({ExpectedCount} variants) from scratch.", expectedCount);
            return;
        }

        _logger.LogWarning("Preview videos not found in assets or scratch (expected {ExpectedCount}, found {FoundCount} in scratch); Unity export may be incomplete.", expectedCount, scratchSources.Length);
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
            return [];

        string[] allowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
        return [.. Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(file => allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)];
    }

    internal static bool IsOfflineCacheCompatibleVideo(string path, JdiVideoInfo? info)
    {
        return info is not null &&
               Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase) &&
               IsSixteenNine(info) &&
               IsVp8OrVp9(info.Codec);
    }

    internal static bool IsVp8OrVp9(string? codec)
    {
        return codec?.Equals("vp8", StringComparison.OrdinalIgnoreCase) == true ||
               codec?.Equals("vp9", StringComparison.OrdinalIgnoreCase) == true;
    }

    internal static bool IsSixteenNine(JdiVideoInfo info)
    {
        if (info.Width <= 0 || info.Height <= 0)
            return false;

        const double target = 16.0 / 9.0;
        double ratio = info.Width / (double)info.Height;
        return Math.Abs(ratio - target) < 0.01;
    }

    internal static JdiVideoTransform BuildSixteenNineTransform(JdiVideoInfo? info)
    {
        if (info is null || IsSixteenNine(info))
            return JdiVideoTransform.None;

        string crop = info.Width / (double)info.Height < 16.0 / 9.0
            ? "crop=trunc(in_w/2)*2:trunc(in_w*9/16/2)*2"
            : "crop=trunc(in_h*16/9/2)*2:trunc(in_h/2)*2";

        return new JdiVideoTransform
        {
            AdditionalFilters = [crop]
        };
    }

    private static string BuildOfflineCacheVideoCacheFileName(
        string role,
        string sourcePath,
        TimeSpan start,
        TimeSpan duration,
        string codec,
        JdiVideoTransform transform)
    {
        FileInfo info = new(sourcePath);
        string key = string.Join(
            '|',
            role,
            info.FullName,
            info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            start.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            duration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            codec,
            string.Join(',', transform.AdditionalFilters),
            transform.Width?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
            transform.Height?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..16];
        return $"unity_offline_{role}_{hash}.webm";
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
            _logger.LogWarning("Cover art asset missing from intermediate package.");
        if (titlePath == null)
            _logger.LogWarning("Song title logo asset missing from intermediate package.");
        if (coachImages.Count == 0)
            _logger.LogWarning("Coach art assets missing from intermediate package.");
        if (backgroundPath == null)
            _logger.LogWarning("Coach background asset missing from intermediate package.");

        return new UnityMenuArtSource(coverPath, titlePath, backgroundPath, coachImages);
    }

    private IReadOnlyList<string> ResolveCoachImages()
    {
        string? coachesDir = GetAssetFolder(IntermediatePackageLayout.Assets.CoachesFolder);
        if (coachesDir == null)
            return Array.Empty<string>();

        string[] candidates = [.. Directory.EnumerateFiles(coachesDir, "coach_*", SearchOption.TopDirectoryOnly)
            .Where(file => !IsCoachBackgroundAsset(file))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];

        return candidates;
    }

    private string? ResolveCoachBackground()
    {
        return GetAssetFileIfExists(IntermediatePackageLayout.Assets.MapBackgroundFile);
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

    private string GetPrimaryBundleOutputFolder(string bundleFolderName)
    {
        UnityServerPlatform primaryPlatform = UnityServerPlatforms.CustomServerDefaults[0];
        return Path.Combine(_outputRoot, primaryPlatform.FolderName, bundleFolderName);
    }

    private string GetBundleOutputFolder(string bundleFolderName, bool forCustomServer) =>
        forCustomServer ? GetPrimaryBundleOutputFolder(bundleFolderName) : EnsureOutputFolder(bundleFolderName);

    private UnityBundlePublishTarget? CreateBundlePublishTarget(string bundleFolderName, bool forCustomServer) =>
        forCustomServer ? new(_outputRoot, bundleFolderName, UnityServerPlatforms.CustomServerDefaults) : null;

    private string[] ResolvePictoFiles()
    {
        string? folder = GetAssetFolder(IntermediatePackageLayout.Assets.PictogramsFolder);
        if (folder == null)
        {
            _logger.LogWarning("Intermediate package missing pictogram folder; map package may lack pictos.");
            return [];
        }

        string[] files = [.. Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];

        if (files.Length == 0)
            _logger.LogWarning("Intermediate pictogram folder is empty; map package may lack pictos.");

        return files;
    }

    private string? ResolveMovesFolder()
    {
        string? folder = GetAssetFolder(IntermediatePackageLayout.Assets.MovesV7Folder);
        if (folder == null)
            _logger.LogWarning("Intermediate package missing moves folder; MSM scripts will be omitted.");
        return folder;
    }

    private static bool IsCoachBackgroundAsset(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("map_bkg", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("coachesbackground", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("coachbackground", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectorySafe(string path, ILogger? logger = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException ex)
        {
            logger?.LogWarning("Failed to delete temporary folder '{Path}': {Message}", path, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning("Failed to delete temporary folder '{Path}': {Message}", path, ex.Message);
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
}