using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Assets;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;
using JustDanceEditor.Logging;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Converters;

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

        Task coverTask = CoverBundleBuilder.GenerateAsync(coverRequest);
        Task titleTask = SongTitleBundleBuilder.GenerateAsync(songTitleRequest);
        Task coachesLargeTask = CoachesLargeBundleBuilder.GenerateAsync(coachesLargeRequest);
        Task coachesSmallTask = CoachesSmallBundleBuilder.GenerateAsync(coachesSmallRequest);
        Task mapPackageTask = MapPackageBundleBuilder.GenerateAsync(mapPackageRequest);

        await Task.WhenAll(mapPackageTask, coverTask, titleTask, coachesLargeTask, coachesSmallTask);
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
        string hash = FileHashing.GetFileMD5(sourceFile);
        if (_request.ExportType == ExportType.CustomServer && !string.IsNullOrEmpty(extension))
            hash += extension;
        return hash;
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
        string? coverPath = TryResolveAssetFile("image/cover", out string? cover) ? cover : null;
        string? titlePath = TryResolveAssetFile("image/songTitleLogo", out string? title) ? title : null;
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
        if (!TryResolveAssetDirectory("image/coachLarge", out string? coachesDir))
            return Array.Empty<string>();

        string[] candidates = Directory.EnumerateFiles(coachesDir, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !IsCoachBackgroundAsset(file))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates;
    }

    private string? ResolveCoachBackground()
    {
        string[] patterns =
        [
            $"{_songFolderName}_map_bkg.*",
            "coachesBackground.*",
            "coachBackground.*",
            "*map_bkg.*"
        ];

        if (TryResolveAssetFile("image/coachBackground", out string? background))
            return background;

        string brandingFolder = GetAssetsSubfolder("branding");
        if (TryFindFileInFolder(brandingFolder, out string? brandingMatch, patterns))
            return brandingMatch;

        string coachesFolder = GetAssetsSubfolder("coaches");
        if (TryFindFileInFolder(coachesFolder, out string? coachesMatch, patterns))
            return coachesMatch;

        return null;
    }

    private string ResolveSongName(UnityExportData unityData)
    {
        if (!string.IsNullOrWhiteSpace(unityData?.Name))
            return unityData.Name;
        if (!string.IsNullOrWhiteSpace(_request.SongName))
            return _request.SongName!;
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
        if (TryResolveAssetDirectory("atlas/pictograms", out string? folder) && Directory.Exists(folder))
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        string fallback = GetAssetsSubfolder("pictograms");
        if (Directory.Exists(fallback))
        {
            return Directory.EnumerateFiles(fallback, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private string? ResolveMovesFolder()
    {
        if (TryResolveAssetDirectory("motion/msm", out string? folder) && Directory.Exists(folder))
            return folder;

        string fallback = GetAssetsSubfolder("moves");
        return Directory.Exists(fallback) ? fallback : null;
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
                Tags = metadata.Tags?.ToArray() ?? [],
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
