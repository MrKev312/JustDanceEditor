using JustDanceEditor.Converter.Converters.Cache;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Intermediate;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Formats.JDI.Serialization;
using JustDanceEditor.Formats.UbiArt.Audio;
using JustDanceEditor.Formats.UbiArt.Files;
using JustDanceEditor.Formats.UbiArt.Images;
using JustDanceEditor.Formats.UbiArt.Video;
using JustDanceEditor.Formats.Unity;
using JustDanceEditor.Formats.Unity.Bundles.Generation;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Converter.Converters;

public class UbiArtToUnityConverter
{
    private readonly ConversionContext _context;
    private readonly IRequestValidator _requestValidator;
    private readonly ISongDataLoader _songDataLoader;
    private readonly bool _contextProvidedExternally;

    // Consider injecting other converters if they become non-static services
    // For now, they are static and will take _context as a parameter.

    public UbiArtToUnityConverter(
        ConversionRequest conversionRequest,
        IRequestValidator requestValidator,
        ISongDataLoader songDataLoader)
    {
        // FileSystem itself might need ConversionRequest for its own initialization.
        // It derives SongName and PlatformType.
        FileSystem fileSystem = new(conversionRequest);
        _context = new ConversionContext(conversionRequest, fileSystem);
        _requestValidator = requestValidator ?? throw new ArgumentNullException(nameof(requestValidator));
        _songDataLoader = songDataLoader ?? throw new ArgumentNullException(nameof(songDataLoader));
    }

    public UbiArtToUnityConverter(ConversionRequest conversionRequest)
    {
        _context = new ConversionContext(conversionRequest, new FileSystem(conversionRequest));
        _requestValidator = new RequestValidator();
        _songDataLoader = new SongDataLoader();
    }

    public UbiArtToUnityConverter(
        ConversionContext context,
        IRequestValidator? requestValidator = null,
        ISongDataLoader? songDataLoader = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _requestValidator = requestValidator ?? new RequestValidator();
        _songDataLoader = songDataLoader ?? new SongDataLoader();
        _contextProvidedExternally = true;
    }

    public void Convert()
    {
        ConvertAsync().Wait(); // Blocking call for synchronous execution
    }

    public async Task ConvertAsync()
    {
        await ConvertInternalAsync(false);
    }

    public void ConvertWithExistingContext()
    {
        ConvertWithExistingContextAsync().Wait();
    }

    public Task ConvertWithExistingContextAsync()
    {
        return ConvertInternalAsync(true);
    }

    private async Task ConvertInternalAsync(bool reuseContext)
    {
        Logger.Log("Started conversion");
        Stopwatch stopwatch = Stopwatch.StartNew();

        _requestValidator.ValidateTemplateFolder(_context.Request.TemplatePath);
        _requestValidator.ValidateConversionRequest(_context.Request);

        if (_context.SongData == null)
        {
            if (reuseContext || _contextProvidedExternally)
                throw new InvalidOperationException("Song data must be populated before reusing an existing conversion context.");

            _context.SongData = _songDataLoader.LoadSongData(_context.Request, _context.FileSystem);
            _context.FileSystem.UpdateSongName(_context.SongData.Name);
        }

        if (_context.SongData == null || string.IsNullOrEmpty(_context.SongData.Name))
        {
            throw new InvalidOperationException("Song data could not be loaded or is invalid.");
        }

        PersistIntermediatePackage();

        if (_context.Request.ExportType == ExportType.CustomServer)
        {
            Directory.CreateDirectory(Path.Combine(_context.Request.OutputPath, _context.SongData.Name));
        }

        await PerformConversionTasksAsync();
        GenerateCacheFiles();
        bool canClearTemp = MergeCache();

#if RELEASE
        if (canClearTemp)
        {
            _context.FileSystem.TempFolders.Delete();
        }
#endif

        CleanupIntermediateFolder();
        stopwatch.Stop();
        Logger.Log($"Conversion finished in {stopwatch.ElapsedMilliseconds}ms");
    }

    private void CleanupIntermediateFolder()
    {
        try
        {
            string intermediateFolder = _context.FileSystem.OutputFolders.IntermediateFolder;
            if (Directory.Exists(intermediateFolder))
                Directory.Delete(intermediateFolder, true);
        }
        catch (IOException ex)
        {
            Logger.Log($"Failed to remove intermediate folder: {ex.Message}", LogLevel.Warning);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log($"Failed to remove intermediate folder: {ex.Message}", LogLevel.Warning);
        }
    }

    private bool MergeCache()
    {
        if (_context.Request.ExportType == ExportType.CustomServer)
            return true;

        if (File.Exists(_context.FileSystem.OutputFolders.CachePath) &&
            File.Exists(_context.FileSystem.OutputFolders.CachingStatusPath))
            return CacheJsonGenerator.MergeCaches(_context); // Pass context

        return true;
    }

    private void GenerateCacheFiles()
    {
        CacheJsonGenerator.GenerateCacheJson(_context); // Pass context
    }

    private async Task PerformConversionTasksAsync()
    {
        Logger.Log("Starting conversion tasks", LogLevel.Debug);

        // FFmpeg download is a prerequisite for media conversion
        if (!File.Exists("ffmpeg.exe") && !File.Exists("ffmpeg")) // Check for OS variations
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

        Task mapPackageTask = GenerateMapPackageBundleAsync();
        Task mediaConversionTask = ConvertMediaAsync(); // Internal method using _context
        Task menuArtAndAssetsTask = ConvertMenuArtAndGenerateBundlesAsync(); // Internal method using _context

        await Task.WhenAll(mapPackageTask, mediaConversionTask, menuArtAndAssetsTask);

        Logger.Log("Conversion tasks finished", LogLevel.Debug);
    }

    private void PersistIntermediatePackage()
    {
        try
        {
            _context.IntermediatePackage ??= IntermediatePackageBuilder.FromUbiArt(_context);
            EnsureUnityExportData();
            string targetFolder = _context.FileSystem.OutputFolders.IntermediateFolder;
            IntermediatePackageSerializer.WriteToFolder(_context.IntermediatePackage, targetFolder);
            Logger.Log($"Intermediate package exported to '{targetFolder}'.", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to export intermediate package: {ex.Message}", LogLevel.Error);
            throw;
        }
    }

    private async Task ConvertMediaAsync()
    {
        var songData = _context.SongData ?? throw new InvalidOperationException("Song data not loaded.");

        await Task.WhenAll(
            AudioConverter.ConvertAudioAsync(songData, _context.FileSystem, _context.Request),
            VideoConverter.ConvertVideoAsync(songData, _context.FileSystem, _context.Request)
        );
    }

    private async Task ConvertMenuArtAndGenerateBundlesAsync()
    {
        await ConvertMenuArtAsync();

        string songName = _context.ResolveSongName();
        int coachCount = _context.ResolveCoachCount();
        bool allowOnlineLookup = _context.Request.OnlineCover;
        bool forCustomServer = _context.Request.ExportType == ExportType.CustomServer;

        await GenerateUnityMenuAssetsAsync(songName, coachCount, allowOnlineLookup, forCustomServer);
    }

    private async Task ConvertMenuArtAsync()
    {
        CookedFile[] menuArtFiles = _context.FileSystem.GetAllFiles(_context.FileSystem.InputFolders.MenuArtFolder);
        UbiArtMenuArtConversionRequest menuRequest = new(menuArtFiles, _context.FileSystem.TempFolders.MenuArtFolder);
        await UbiArtMenuArtConverter.ConvertMenuArtAsync(menuRequest);
    }

    private static async Task RunGeneratorSafely(Func<Task> generatorCall, string assetName)
    {
        try
        {
            await generatorCall();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to generate {assetName}: {ex.Message}", LogLevel.Error);
        }
    }

    private void EnsureUnityExportData()
    {
        if (_context.IntermediatePackage == null)
            throw new InvalidOperationException("Intermediate package must be created before initializing Unity export data.");

        _context.UnityData ??= UnityExportDataBuilder.Create(_context.IntermediatePackage);
    }

    private Task GenerateUnityMenuAssetsAsync(string songName, int coachCount, bool allowOnlineLookup, bool forCustomServer)
    {
        UnityExportData unityData = _context.RequireUnityData();
        string menuArtFolder = _context.FileSystem.TempFolders.MenuArtFolder;

        UnityCoachesLargeGenerationRequest largeRequest = new(
            songName,
            coachCount,
            menuArtFolder,
            unityData,
            _context.FileSystem.TemplateFiles.CoachesLarge,
            _context.FileSystem.OutputFolders.CoachesLargeFolder,
            forCustomServer);

        UnityCoachesSmallGenerationRequest smallRequest = new(
            songName,
            coachCount,
            menuArtFolder,
            _context.FileSystem.TemplateFiles.CoachesSmall,
            _context.FileSystem.OutputFolders.CoachesSmallFolder,
            forCustomServer);

        UnityCoverGenerationRequest coverRequest = new(
            songName,
            unityData,
            menuArtFolder,
            allowOnlineLookup,
            _context.FileSystem.TemplateFiles.Cover,
            _context.FileSystem.OutputFolders.CoverFolder,
            forCustomServer);

        UnitySongTitleGenerationRequest songTitleRequest = new(
            songName,
            unityData,
            menuArtFolder,
            allowOnlineLookup,
            _context.FileSystem.TemplateFiles.SongTitleLogo,
            _context.FileSystem.OutputFolders.SongTitleLogoFolder,
            forCustomServer);

        return Task.WhenAll(
            RunGeneratorSafely(() => UnityCoachesLargeGenerator.GenerateAsync(largeRequest), "CoachesLarge"),
            RunGeneratorSafely(() => UnityCoachesSmallGenerator.GenerateAsync(smallRequest), "CoachesSmall"),
            RunGeneratorSafely(() => UnityCoverGenerator.GenerateAsync(coverRequest), "cover"),
            RunGeneratorSafely(() => UnitySongTitleGenerator.GenerateAsync(songTitleRequest), "song title logo")
        );
    }

    private Task GenerateMapPackageBundleAsync()
    {
        var rawPictoFiles = _context.FileSystem.GetAllFiles(_context.FileSystem.InputFolders.PictosFolder);
        string[] pictoFiles = rawPictoFiles.Select(file => (string)file).ToArray();

        UnityMapPackageGenerationRequest request = new(
            _context.ResolveSongName(),
            _context.RequireUnityData(),
            pictoFiles,
            _context.FileSystem.TempFolders.PictoFolder,
            _context.FileSystem.TempFolders.PictoAtlasFolder,
            _context.TryGetMovesFolder(),
            _context.FileSystem.TemplateFiles.MapPackage,
            _context.FileSystem.OutputFolders.MapPackageFolder,
            _context.Request.ExportType == ExportType.CustomServer);

        return RunGeneratorSafely(() => UnityMapPackageGenerator.GenerateAsync(request), "map package");
    }
}