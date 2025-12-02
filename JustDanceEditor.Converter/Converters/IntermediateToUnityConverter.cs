using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using JustDanceEditor.Converter.Converters.Bundles;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Formats.Intermediate;
using JustDanceEditor.Formats.Intermediate.Assets;
using JustDanceEditor.Formats.Intermediate.Metadata;
using JustDanceEditor.Logging;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace JustDanceEditor.Converter.Converters;

internal sealed class IntermediateToUnityConverter
{
    private readonly IntermediateSongPackage _package;
    private readonly string _packageRoot;
    private readonly ConversionRequest _request;
    private readonly IRequestValidator _validator;
    private readonly TemplateSet _templates;
    private readonly Dictionary<string, IntermediateAsset> _assetsByRole;
    private readonly string _songFolderName;
    private readonly string _outputRoot;

    public IntermediateToUnityConverter(
        IntermediateSongPackage package,
        string packageRoot,
        ConversionRequest request,
        IRequestValidator validator)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _packageRoot = packageRoot ?? throw new ArgumentNullException(nameof(packageRoot));
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        if (string.IsNullOrWhiteSpace(request.TemplatePath))
            throw new ArgumentException("Template path must be provided for Unity exports.");

        _templates = new TemplateSet(request.TemplatePath);
        _assetsByRole = _package.AssetCatalog.Assets.ToDictionary(a => a.Role, StringComparer.OrdinalIgnoreCase);
        _songFolderName = BuildSongFolderName(_package.Metadata);
        _outputRoot = Path.Combine(request.OutputPath, _songFolderName);
    }

    public async Task ConvertAsync()
    {
        _validator.ValidateTemplateFolder(_request.TemplatePath);
        _validator.ValidateConversionRequest(_request);

        Directory.CreateDirectory(_outputRoot);
        CopyAudioAssets();
        CopyVideoAssets();
        await GenerateSongInfoAsync();
        await BuildUnityBundlesAsync();
    }

    private void CopyAudioAssets()
    {
        CopyHashedAsset("audio/master", Path.Combine(_outputRoot, "Audio_opus"), ".opus");
        CopyHashedAsset("audio/preview", Path.Combine(_outputRoot, "AudioPreview_opus"), ".opus");
    }

    private void CopyVideoAssets()
    {
        CopyHashedDirectory("video/background", Path.Combine(_outputRoot, "video"), ".webm");
        CopyHashedDirectory("video/preview", Path.Combine(_outputRoot, "videoPreview"), ".webm");
    }

    private async Task GenerateSongInfoAsync()
    {
        UnitySongInfoDocument doc = UnitySongInfoDocument.FromMetadata(_package.Metadata);
        string jsonPath = Path.Combine(_outputRoot, "SongInfo.json");
        await using FileStream stream = File.Create(jsonPath);
        await JsonSerializer.SerializeAsync(stream, doc, UnitySongInfoDocument.SerializerOptions);
    }

    private async Task BuildUnityBundlesAsync()
    {
        string workspaceRoot = PrepareWorkspaceRoot();
        string platform = ResolvePlatform();
        string patchRoot = Path.Combine(workspaceRoot, $"patch_{platform}");
        ConversionContext? context = null;

        try
        {
            PrepareSyntheticInputScaffold(patchRoot, platform);
            PopulateSyntheticInputAssets(patchRoot);
            context = BuildSyntheticConversionContext(patchRoot);
            PopulateMenuArtAssets(context);
            await GenerateUnityBundlesAsync(context);
        }
        finally
        {
            if (context != null)
                TryDeleteDirectorySafe(context.FileSystem.TempFolders.MapFolder);

            TryDeleteDirectorySafe(workspaceRoot);
        }
    }

    private void CopyHashedAsset(string role, string destinationFolder, string extension)
    {
        if (!TryResolveAssetFile(role, out string? source))
            return;

        Directory.CreateDirectory(destinationFolder);
        string hashName = BuildHashedFileName(source, extension);
        File.Copy(source, Path.Combine(destinationFolder, hashName), true);
    }

    private void CopyHashedDirectory(string role, string destinationFolder, string extension)
    {
        if (!TryResolveAssetDirectory(role, out string? sourceDir))
            return;

        Directory.CreateDirectory(destinationFolder);
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string hashName = BuildHashedFileName(file, extension);
            File.Copy(file, Path.Combine(destinationFolder, hashName), true);
        }
    }

    private bool TryResolveAssetFile(string role, [NotNullWhen(true)] out string? path)
    {
        path = null;
        if (_assetsByRole.TryGetValue(role, out IntermediateAsset? asset) &&
            !string.IsNullOrWhiteSpace(asset.SourcePath))
        {
            string resolved = ResolveRelativePath(asset.SourcePath);
            if (File.Exists(resolved))
            {
                path = resolved;
                return true;
            }
        }

        return TryResolveFallbackAssetFile(role, out path);
    }

    private bool TryResolveAssetDirectory(string role, [NotNullWhen(true)] out string? folder)
    {
        folder = null;
        if (_assetsByRole.TryGetValue(role, out IntermediateAsset? asset) &&
            !string.IsNullOrWhiteSpace(asset.SourcePath))
        {
            string resolved = ResolveRelativePath(asset.SourcePath);
            if (Directory.Exists(resolved))
            {
                folder = resolved;
                return true;
            }
        }

        return TryResolveFallbackAssetDirectory(role, out folder);
    }

    private bool TryResolveFallbackAssetFile(string role, [NotNullWhen(true)] out string? path)
    {
        path = null;
        string brandingFolder = GetAssetsSubfolder("branding");
        return role switch
        {
            "image/cover" => TryFindFileInFolder(brandingFolder, out path, "thumbnail.*", "cover.*"),
            "image/songTitleLogo" => TryFindFileInFolder(brandingFolder, out path, "songTitleLogo.*", "song_title_logo.*"),
            _ => false
        };
    }

    private bool TryResolveFallbackAssetDirectory(string role, [NotNullWhen(true)] out string? folder)
    {
        folder = role switch
        {
            "image/coachLarge" => GetAssetsSubfolder("coaches"),
            "atlas/pictograms" => GetAssetsSubfolder("pictograms"),
            "motion/msm" => GetAssetsSubfolder("moves"),
            "motion/gestures" => GetAssetsSubfolder("gestures"),
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            return true;

        folder = null;
        return false;
    }

    private static bool TryFindFileInFolder(string folder, [NotNullWhen(true)] out string? path, params string[] patterns)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return false;

        foreach (string pattern in patterns)
        {
            foreach (string match in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
            {
                path = match;
                return true;
            }
        }

        return false;
    }

    private string GetAssetsSubfolder(string segment)
    {
        string assetsRoot = Path.Combine(_packageRoot, "assets");
        string preferred = Path.Combine(assetsRoot, segment);
        if (Directory.Exists(preferred))
            return preferred;

        string fallback = Path.Combine(_packageRoot, segment);
        return fallback;
    }

    private string ResolveRelativePath(string relative)
    {
        string normalized = relative.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_packageRoot, normalized));
    }

    private string BuildHashedFileName(string sourceFile, string extension)
    {
        string hash = Download.GetFileMD5(sourceFile);
        if (_request.ExportType == ExportType.CustomServer && !string.IsNullOrEmpty(extension))
            hash += extension;
        return hash;
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string dir in Directory.GetDirectories(source))
            CopyDirectoryRecursive(dir, Path.Combine(destination, Path.GetFileName(dir)));
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

    private static string PrepareWorkspaceRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "JustDanceEditor", "IntermediateUnity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private void PrepareSyntheticInputScaffold(string patchRoot, string platform)
    {
        TryDeleteDirectorySafe(patchRoot);
        Directory.CreateDirectory(Path.Combine(patchRoot, "world", "maps", _songFolderName));
        Directory.CreateDirectory(Path.Combine(patchRoot, "cache", "itf_cooked", platform));
    }

    private void PopulateSyntheticInputAssets(string patchRoot)
    {
        string mapRoot = Path.Combine(patchRoot, "world", "maps", _songFolderName);
        string timelineRoot = Path.Combine(mapRoot, "timeline");
        string pictosFolder = Path.Combine(timelineRoot, "pictos");
        string movesFolder = Path.Combine(timelineRoot, "moves", "wiiu");
        string gesturesFolder = Path.Combine(timelineRoot, "gestures");

        Directory.CreateDirectory(pictosFolder);
        Directory.CreateDirectory(movesFolder);
        Directory.CreateDirectory(gesturesFolder);

        CopyAssetDirectoryTo("atlas/pictograms", pictosFolder, logWhenMissing: true);
        CopyAssetDirectoryTo("motion/msm", movesFolder, logWhenMissing: true);
        CopyAssetDirectoryTo("motion/gestures", gesturesFolder, logWhenMissing: false);
    }

    private ConversionContext BuildSyntheticConversionContext(string patchRoot)
    {
        ConversionRequest syntheticRequest = new()
        {
            InputPath = patchRoot,
            OutputPath = _request.OutputPath,
            TemplatePath = _request.TemplatePath,
            ExportType = _request.ExportType,
            OnlineCover = _request.OnlineCover,
            SongName = _songFolderName,
            SongGUID = ResolveSongGuid(),
            CacheNumber = _request.CacheNumber,
            JDVersion = _request.JDVersion ?? _package.Metadata.EngineVersion
        };

        FileSystem fileSystem = new(syntheticRequest);
        if (_package.Metadata.SongId == Guid.Empty)
            _package.Metadata.SongId = syntheticRequest.SongGUID;

        ConversionContext context = new(syntheticRequest, fileSystem)
        {
            IntermediatePackage = _package,
            UnityData = UnityExportDataBuilder.Create(_package)
        };

        return context;
    }

    private Guid ResolveSongGuid()
    {
        if (_package.Metadata.SongId != Guid.Empty)
            return _package.Metadata.SongId;
        if (_request.SongGUID != Guid.Empty)
            return _request.SongGUID;
        return Guid.NewGuid();
    }

    private string ResolvePlatform()
    {
        if (_package.Metadata.AdditionalMetadata != null &&
            _package.Metadata.AdditionalMetadata.TryGetValue("platformType", out string? platform) &&
            !string.IsNullOrWhiteSpace(platform))
        {
            return platform.Trim();
        }

        return "nx";
    }

    private static async Task GenerateUnityBundlesAsync(ConversionContext context)
    {
        Task coverTask = CoverBundleGenerator.GenerateCoverAsync(context);
        Task titleTask = SongTitleBundleGenerator.GenerateSongTitleLogoAsync(context);
        Task coachesLargeTask = CoachesLargeBundleGenerator.GenerateCoachesLargeAsync(context);
        Task coachesSmallTask = CoachesSmallBundleGenerator.GenerateCoachesSmallAsync(context);
        Task mapPackageTask = MapPackageBundleGenerator.GenerateMapPackageAsync(context);

        await Task.WhenAll(mapPackageTask, coverTask, titleTask, coachesLargeTask, coachesSmallTask);
    }

    private void PopulateMenuArtAssets(ConversionContext context)
    {
        string menuArtFolder = context.FileSystem.TempFolders.MenuArtFolder;
        Directory.CreateDirectory(menuArtFolder);

        if (TryResolveAssetFile("image/cover", out string? coverPath))
            ExportImageAsset(coverPath, Path.Combine(menuArtFolder, "cover.png"));
        else
            Logger.Log("Cover art asset missing from intermediate package.", LogLevel.Warning);

        if (TryResolveAssetFile("image/songTitleLogo", out string? titlePath))
            ExportImageAsset(titlePath, Path.Combine(menuArtFolder, "songTitleLogo.png"));
        else
            Logger.Log("Song title logo asset missing from intermediate package.", LogLevel.Warning);

        if (TryResolveAssetDirectory("image/coachLarge", out string? coachesDir))
        {
            string coachArtName = context.UnityData?.Name ?? _songFolderName;
            int coachCount = context.UnityData?.Metadata.CoachCount ?? _package.Metadata.CoachCount;
            CopyCoachArt(coachesDir, menuArtFolder, coachArtName, coachCount);
        }
        else
            Logger.Log("Coach art assets missing from intermediate package.", LogLevel.Warning);
    }

    private void CopyAssetDirectoryTo(string role, string destination, bool logWhenMissing)
    {
        if (!TryResolveAssetDirectory(role, out string? source))
        {
            if (logWhenMissing)
                Logger.Log($"Intermediate asset '{role}' could not be resolved; skipping synthetic input copy.", LogLevel.Warning);
            return;
        }

        if (Directory.Exists(destination))
            Directory.Delete(destination, true);

        CopyDirectoryRecursive(source, destination);
    }

    private static void ExportImageAsset(string? sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        try
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(sourcePath);
            image.SaveAsPng(destinationPath);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to materialize image '{sourcePath}': {ex.Message}", LogLevel.Warning);
        }
    }

    private void CopyCoachArt(string sourceDir, string destination, string songName, int coachCount)
    {
        string[] candidates = Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            Logger.Log("Coach art directory is empty.", LogLevel.Warning);
            return;
        }

        for (int i = 0; i < coachCount; i++)
        {
            if (i >= candidates.Length)
            {
                Logger.Log($"Not enough coach images available. Needed {coachCount}, found {candidates.Length}.", LogLevel.Warning);
                break;
            }

            string target = Path.Combine(destination, $"{songName}_Coach_{i + 1}.png");
            ExportImageAsset(candidates[i], target);
        }

        string? background = candidates.FirstOrDefault(IsCoachBackgroundAsset) ?? ResolveCoachBackgroundFromCoaches();
        if (background != null)
        {
            ExportImageAsset(background, Path.Combine(destination, $"{songName}_map_bkg.png"));
        }
        else
        {
            Logger.Log("Coach background asset missing from intermediate package.", LogLevel.Warning);
        }
    }

    private static bool IsCoachBackgroundAsset(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Contains("map_bkg", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("coachesbackground", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("coachbackground", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveCoachBackgroundFromCoaches()
    {
        string[] patterns =
        [
            $"{_songFolderName}_map_bkg.*",
            "coachesBackground.*",
            "coachBackground.*",
            "*map_bkg.*"
        ];

        string coachesFolder = GetAssetsSubfolder("coaches");
        if (Directory.Exists(coachesFolder) &&
            TryFindFileInFolder(coachesFolder, out string? backgroundPath, patterns))
        {
            return backgroundPath;
        }

        return null;
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

    private sealed class TemplateSet
    {
        public TemplateSet(string root)
        {
            string Resolve(string name)
            {
                string folder = Path.Combine(root, name);
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException($"Missing template folder '{folder}'.");
                string? file = Directory.EnumerateFiles(folder).OrderBy(f => f).FirstOrDefault();
                if (file == null)
                    throw new FileNotFoundException($"Template folder '{folder}' does not contain any files.");
                return file;
            }

            Cover = Resolve("Cover");
            SongTitleLogo = Resolve("SongTitleLogo");
            CoachesLarge = Resolve("CoachesLarge");
            MapPackage = Resolve("MapPackage");
        }

        public string Cover { get; }
        public string SongTitleLogo { get; }
        public string CoachesLarge { get; }
        public string MapPackage { get; }
    }

    private sealed class UnitySongInfoDocument
    {
        private UnitySongInfoDocument() { }

        public required Guid SongID { get; init; }
        public required string MapName { get; init; }
        public string? ParentMapName { get; init; }
        public string? Title { get; init; }
        public string? Artist { get; init; }
        public string? Credits { get; init; }
        public uint Difficulty { get; init; }
        public uint SweatDifficulty { get; init; }
        public int CoachCount { get; init; }
        public string[]? CoachNames { get; init; }
        public bool HasSongTitleInCover { get; init; }
        public string? LyricsColor { get; init; }
        public string[]? Tags { get; init; }
        public string[]? TagIds { get; init; }
        public uint OriginalJDVersion { get; init; }
        public double MapLength { get; init; }
        public string? DoubleScoringType { get; init; }
        public string[]? CoachNamesLocIds { get; init; }
        public int? DanceVersionLocId { get; init; }

        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        public static UnitySongInfoDocument FromMetadata(IntermediateMetadata metadata)
        {
            metadata.Validate();
            metadata.AdditionalMetadata ??= new();
            metadata.AdditionalMetadata.TryGetValue("unity.tagIds", out string? tagIdsRaw);
            metadata.AdditionalMetadata.TryGetValue("unity.coachNamesLocIds", out string? namesLocRaw);
            metadata.AdditionalMetadata.TryGetValue("unity.danceVersionLocId", out string? danceLocRaw);
            metadata.AdditionalMetadata.TryGetValue("unity.doubleScoringType", out string? doubleScoreType);

            return new UnitySongInfoDocument
            {
                SongID = metadata.SongId,
                MapName = metadata.MapName,
                ParentMapName = metadata.ParentMapName,
                Title = metadata.Title,
                Artist = metadata.Artist,
                Credits = metadata.Credits,
                Difficulty = metadata.Difficulty,
                SweatDifficulty = metadata.SweatDifficulty,
                CoachCount = metadata.CoachCount,
                CoachNames = metadata.CoachNames,
                HasSongTitleInCover = metadata.HasSongTitleInCover,
                LyricsColor = metadata.LyricsColor,
                Tags = metadata.Tags?.ToArray() ?? Array.Empty<string>(),
                TagIds = SplitCsv(tagIdsRaw),
                OriginalJDVersion = metadata.OriginalJdVersion,
                MapLength = metadata.MapLengthSeconds,
                DoubleScoringType = string.IsNullOrWhiteSpace(doubleScoreType) ? null : doubleScoreType,
                CoachNamesLocIds = SplitCsv(namesLocRaw),
                DanceVersionLocId = int.TryParse(danceLocRaw, out int parsed) ? parsed : null
            };
        }

        private static string[]? SplitCsv(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
