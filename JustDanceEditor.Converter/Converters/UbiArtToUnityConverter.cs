using JustDanceEditor.Converter.Converters.Audio;
using JustDanceEditor.Converter.Converters.Bundles;
using JustDanceEditor.Converter.Converters.Cache;
using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Converters.Video;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Intermediate;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Converter.Unity;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg.Downloader;
using JustDanceEditor.Formats.Intermediate.Serialization;

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

        _requestValidator.ValidateTemplateFolder(_context.Request.TemplatePath); // Or _context.FileSystem.TemplateFiles.TemplateFolder
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

        Task mapPackageTask = MapPackageBundleGenerator.GenerateMapPackageAsync(_context);
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
            _context.UnityData ??= UnityExportDataBuilder.Create(_context.IntermediatePackage);
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
        await Task.WhenAll(
            AudioConverter.ConvertAudioAsync(_context),
            VideoConverter.ConvertVideoAsync(_context)
        );
    }

    private async Task ConvertMenuArtAndGenerateBundlesAsync()
    {
        await MenuArtConverter.ConvertMenuArtAsync(_context);

        await Task.WhenAll(
            CoachesLargeBundleGenerator.GenerateCoachesLargeAsync(_context),
            CoachesSmallBundleGenerator.GenerateCoachesSmallAsync(_context),
            CoverBundleGenerator.GenerateCoverAsync(_context),
            SongTitleBundleGenerator.GenerateSongTitleLogoAsync(_context)
        );
    }
}