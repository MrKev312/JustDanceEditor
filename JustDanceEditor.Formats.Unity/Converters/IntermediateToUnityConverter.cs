using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Metadata;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Formats.Unity.Builders;
using JustDanceEditor.Formats.Unity.Bundles;
using JustDanceEditor.Formats.Unity.Images;
using JustDanceEditor.Formats.Unity.Models;
using JustDanceEditor.Logging;

using System.Text.Json;

namespace JustDanceEditor.Formats.Unity.Converters;

internal sealed class IntermediateToUnityConverter
{
    private readonly IntermediateSongPackage _package;
    private readonly string _packageRoot;
    private readonly ConversionRequest _request;
    private readonly IRequestValidator _validator;
    private readonly TemplateSet _templates;
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
        _songFolderName = BuildSongFolderName(_package.Metadata);
        _outputRoot = Path.Combine(request.OutputPath, _songFolderName);
    }

    public async Task ConvertAsync()
    {
        Logger.Log($"Starting JDI → Unity conversion for '{_songFolderName}'.", LogLevel.Info);
        _validator.ValidateTemplateFolder(_request.TemplatePath);
        _validator.ValidateConversionRequest(_request);

        Directory.CreateDirectory(_outputRoot);
        Logger.Log("Copying audio assets into Unity workspace...", LogLevel.Debug);
        CopyAudioAssets();
        Logger.Log("Copying video assets into Unity workspace...", LogLevel.Debug);
        CopyVideoAssets();
        Logger.Log("Generating SongInfo.json...", LogLevel.Debug);
        await GenerateSongInfoAsync();
        Logger.Log("Building Unity bundles...", LogLevel.Debug);
        await BuildUnityBundlesAsync();
        Logger.Log($"Unity conversion for '{_songFolderName}' completed.", LogLevel.Info);
    }

    private void CopyAudioAssets()
    {
        CopyHashedFile(IntermediatePackageLayout.Assets.AudioMasterFile, Path.Combine(_outputRoot, "Audio_opus"), ".opus");
        CopyHashedFile(IntermediatePackageLayout.Assets.AudioPreviewFile, Path.Combine(_outputRoot, "AudioPreview_opus"), ".opus");
    }

    private void CopyVideoAssets()
    {
        CopyHashedDirectory(IntermediatePackageLayout.Assets.VideoFolder, Path.Combine(_outputRoot, "video"), ".webm");
        CopyHashedDirectory(IntermediatePackageLayout.Assets.PreviewVideoFolder, Path.Combine(_outputRoot, "videoPreview"), ".webm");
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

    private void CopyHashedDirectory(string relativeSourceFolder, string destinationFolder, string extension)
    {
        string sourceDir = ResolvePackagePath(relativeSourceFolder);
        if (!Directory.Exists(sourceDir))
        {
            Logger.Log($"Expected asset folder '{relativeSourceFolder}' does not exist; skipping copy.", LogLevel.Warning);
            return;
        }

        Directory.CreateDirectory(destinationFolder);
        bool copiedAny = false;
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string hashName = BuildHashedFileName(file, extension);
            File.Copy(file, Path.Combine(destinationFolder, hashName), true);
            copiedAny = true;
        }

        if (!copiedAny)
            Logger.Log($"Asset folder '{relativeSourceFolder}' is empty; nothing copied to '{destinationFolder}'.", LogLevel.Warning);
        else
            Logger.Log($"Copied assets from '{relativeSourceFolder}' to '{destinationFolder}'.", LogLevel.Debug);
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
        string? folder = GetAssetFolder(IntermediatePackageLayout.Assets.PictogramsFolder);
        if (folder == null)
        {
            Logger.Log("Intermediate package missing pictogram folder; map package may lack pictos.", LogLevel.Warning);
            return Array.Empty<string>();
        }

        string[] files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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