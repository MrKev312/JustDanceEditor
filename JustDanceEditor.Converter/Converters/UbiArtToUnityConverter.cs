using JustDanceEditor.Converter.Converters.Audio;
using JustDanceEditor.Converter.Converters.Bundles;
using JustDanceEditor.Converter.Converters.Cache;
using JustDanceEditor.Converter.Converters.Images;
using JustDanceEditor.Converter.Converters.Video;
using JustDanceEditor.Converter.Core;
using JustDanceEditor.Converter.Files;
using JustDanceEditor.Converter.Services;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Converter.Converters;

public class UbiArtToUnityConverter
{
    private readonly ConversionContext _context;
    private readonly IRequestValidator _requestValidator;
    private readonly ISongDataLoader _songDataLoader;

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

    public void Convert()
    {
        ConvertAsync().Wait(); // Blocking call for synchronous execution
    }

    public async Task ConvertAsync()
    {
        Logger.Log("Started conversion");
        Stopwatch stopwatch = Stopwatch.StartNew();

        _requestValidator.ValidateTemplateFolder(_context.Request.TemplatePath); // Or _context.FileSystem.TemplateFiles.TemplateFolder
        _requestValidator.ValidateConversionRequest(_context.Request);

        _context.SongData = _songDataLoader.LoadSongData(_context.Request, _context.FileSystem);

        if (_context.SongData == null || string.IsNullOrEmpty(_context.SongData.Name))
        {
            throw new InvalidOperationException("Song data could not be loaded or is invalid.");
        }

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

        stopwatch.Stop();
        Logger.Log($"Conversion finished in {stopwatch.ElapsedMilliseconds}ms");
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