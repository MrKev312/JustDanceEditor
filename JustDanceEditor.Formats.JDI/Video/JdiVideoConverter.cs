using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System.Globalization;
using System.Text;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.JDI.Video;

public static class JdiVideoConverter
{
    private static readonly string[] AllowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
    private static readonly SemaphoreSlim FFmpegSemaphore = new(Environment.ProcessorCount, Environment.ProcessorCount);
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _ffmpegInitialized = false;

    public static async Task EnsureFFmpegInitializedAsync()
    {
        if (_ffmpegInitialized)
            return;

        await InitLock.WaitAsync();
        try
        {
            if (_ffmpegInitialized)
                return;

            if (!File.Exists("ffmpeg.exe") && !File.Exists("ffmpeg"))
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);

            _ffmpegInitialized = true;
        }
        finally
        {
            InitLock.Release();
        }
    }

    public static async Task EnsurePreviewVideosAsync(IntermediateSongPackage package, string packageRoot, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        await EnsureFFmpegInitializedAsync();

        string assetsFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.PreviewVideoFolder);
        string scratchFolder = GetScratchFolder(packageRoot);

        // Check if originals exist in assets (source-of-truth)
        if (HasCompletePreviewSet(assetsFolder))
        {
            logger.LogDebug("Source-of-truth preview videos found in assets.");
            return;
        }

        // Check if already generated in scratch
        if (HasCompletePreviewSet(scratchFolder) && ValidateManifest(scratchFolder, UnityVideoProfiles.Previews, "preview"))
        {
            logger.LogDebug("Preview videos already exist in scratch with valid manifest.");
            return;
        }

        // Generate in scratch from source video
        string videoFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string? sourceVideo = SelectSourceVideo(videoFolder);
        if (sourceVideo == null)
        {
            logger.LogWarning("Cannot generate preview videos; no source video found in assets/video.");
            return;
        }

        Directory.CreateDirectory(scratchFolder);
        (TimeSpan previewStart, TimeSpan previewDuration) = package.TimelineStructure.GetVideoPreviewTiming();

        logger.LogInformation("Generating {Count} preview videos in scratch...", UnityVideoProfiles.Previews.Length);
        await ConvertAllVideosAsync(sourceVideo, scratchFolder, UnityVideoProfiles.Previews, previewStart, previewDuration, logger, cancellationToken);

        WriteManifest(scratchFolder, UnityVideoProfiles.Previews, "preview", logger);
        logger.LogInformation("Preview video generation complete.");
    }

    public static async Task EnsureBackgroundVideosAsync(string packageRoot, Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        await EnsureFFmpegInitializedAsync();

        string assetsFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string scratchFolder = GetScratchFolder(packageRoot);

        // Check if originals exist in assets (source-of-truth: 4 master videos)
        if (HasCompleteMasterSet(assetsFolder))
        {
            logger.LogDebug("Source-of-truth background videos (4 variants) found in assets.");
            return;
        }

        // Check if already generated in scratch (filter by master_ prefix)
        if (HasCompleteMasterSet(scratchFolder) && ValidateManifest(scratchFolder, UnityVideoProfiles.Masters, "master"))
        {
            logger.LogDebug("Background videos already exist in scratch with valid manifest.");
            return;
        }

        // Generate in scratch from largest source video
        string? sourceVideo = SelectSourceVideo(assetsFolder);
        if (sourceVideo == null)
        {
            logger.LogWarning("Cannot generate background videos; no source video found in assets/video.");
            return;
        }

        Directory.CreateDirectory(scratchFolder);

        logger.LogInformation("Generating {Count} background videos in scratch...", UnityVideoProfiles.Masters.Length);
        await ConvertAllVideosAsync(sourceVideo, scratchFolder, UnityVideoProfiles.Masters, TimeSpan.Zero, TimeSpan.Zero, logger, cancellationToken);

        WriteManifest(scratchFolder, UnityVideoProfiles.Masters, "master", logger);
        logger.LogInformation("Background video generation complete.");
    }

    private static bool HasCompleteMasterSet(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        int count = Directory.EnumerateFiles(folder)
            .Where(IsVideoFile)
            .Count(f => Path.GetFileName(f).StartsWith("master_", StringComparison.OrdinalIgnoreCase));
        return count >= UnityVideoProfiles.Masters.Length;
    }

    private static bool HasCompletePreviewSet(string previewFolder)
    {
        if (!Directory.Exists(previewFolder))
            return false;

        int count = Directory.EnumerateFiles(previewFolder)
            .Where(IsVideoFile)
            .Count(f => Path.GetFileName(f).StartsWith("preview_", StringComparison.OrdinalIgnoreCase));
        return count >= UnityVideoProfiles.Previews.Length;
    }

    private static string? SelectSourceVideo(string videoFolder)
    {
        if (!Directory.Exists(videoFolder))
            return null;

        return Directory.EnumerateFiles(videoFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsVideoFile)
            .OrderByDescending(file => new FileInfo(file).Length)
            .FirstOrDefault();
    }

    private static bool IsVideoFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return AllowedExtensions.Contains(extension);
    }

    private static async Task ConvertAllVideosAsync(
        string source,
        string scratchFolder,
        VideoQualityProfile[] profiles,
        TimeSpan start,
        TimeSpan duration,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken)
    {
        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(source, cancellationToken);
        IVideoStream stream = mediaInfo.VideoStreams.First();

        // Build common filter for cropping only
        List<string> baseFilters = [];
        float currentRatio = stream.Width / (float)stream.Height;
        float targetRatio = 16f / 9f;
        if (Math.Abs(currentRatio - targetRatio) > 0.001f)
            baseFilters.Add(currentRatio < targetRatio ? "crop=in_w:in_w*9/16" : "crop=in_h*16/9:in_h");

        string baseCropFilter = baseFilters.Count > 0 ? string.Join(",", baseFilters) : string.Empty;

        // Process all profiles in parallel for better CPU utilization
        logger.LogInformation("Starting parallel 2-pass encoding for {Count} profiles...", profiles.Length);

        Task[] encodingTasks = new Task[profiles.Length];
        for (int i = 0; i < profiles.Length; i++)
        {
            int profileIndex = i; // Capture for closure
            encodingTasks[i] = EncodeProfileAsync(source, scratchFolder, profiles[profileIndex], profileIndex,
                start, duration, baseCropFilter, logger, cancellationToken);
        }

        await Task.WhenAll(encodingTasks);
        logger.LogInformation("All 2-pass video conversions completed successfully.");
    }

    private static async Task EncodeProfileAsync(
        string source,
        string scratchFolder,
        VideoQualityProfile profile,
        int profileIndex,
        TimeSpan start,
        TimeSpan duration,
        string baseCropFilter,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken cancellationToken)
    {
        string targetPath = Path.Combine(scratchFolder, profile.FileName);
        string passLogFile = Path.Combine(scratchFolder, $"ffmpeg2pass-{profileIndex}");

        // Build profile-specific video filter
        List<string> filters = [];
        if (!string.IsNullOrEmpty(baseCropFilter))
            filters.Add(baseCropFilter);

        // Add scale for this specific profile
        Size targetRes = new(profile.Width ?? 768, profile.Height ?? 432);
        filters.Add($"scale={targetRes.Width}:{targetRes.Height}");

        // Add fade effects only for preview videos (when duration is set)
        if (duration > TimeSpan.Zero)
        {
            double fadeOutStart = Math.Max(0, duration.TotalSeconds - 1);
            filters.Add($"fade=t=in:st=0:d=1");
            filters.Add($"fade=t=out:st={fadeOutStart.ToString(CultureInfo.InvariantCulture)}:d=1");
        }

        string videoFilter = string.Join(",", filters);

        // Build common VP9 arguments used for both passes
        string commonVp9Args = BuildVp9Arguments(profile, passLogFile);

        logger.LogInformation("Encoding {FileName} (pass 1/2)...", profile.FileName);

        // Pass 1: Analysis pass
        StringBuilder pass1Args = new();
        if (start > TimeSpan.Zero)
            pass1Args.Append(CultureInfo.InvariantCulture, $"-ss {start.TotalSeconds} ");
        if (duration > TimeSpan.Zero)
            pass1Args.Append(CultureInfo.InvariantCulture, $"-t {duration.TotalSeconds} ");
        pass1Args.Append($"-i \"{source}\" ");
        pass1Args.Append($"-vf \"{videoFilter}\" ");
        pass1Args.Append("-quality good ");
        pass1Args.Append("-pass 1 ");
        pass1Args.Append(commonVp9Args);
        pass1Args.Append("-an ");
        pass1Args.Append("-f null -");

        cancellationToken.ThrowIfCancellationRequested();

        await FFmpegSemaphore.WaitAsync(cancellationToken);
        try
        {
            IConversion pass1 = FFmpeg.Conversions.New();
            await pass1.Start(pass1Args.ToString(), cancellationToken);
        }
        finally
        {
            FFmpegSemaphore.Release();
        }

        logger.LogInformation("Encoding {FileName} (pass 2/2)...", profile.FileName);

        // Pass 2: Final encoding pass
        StringBuilder pass2Args = new();
        if (start > TimeSpan.Zero)
            pass2Args.Append(CultureInfo.InvariantCulture, $"-ss {start.TotalSeconds} ");
        if (duration > TimeSpan.Zero)
            pass2Args.Append(CultureInfo.InvariantCulture, $"-t {duration.TotalSeconds} ");
        pass2Args.Append($"-i \"{source}\" ");
        pass2Args.Append($"-vf \"{videoFilter}\" ");
        pass2Args.Append("-quality good ");
        pass2Args.Append("-pass 2 ");
        pass2Args.Append(commonVp9Args);
        pass2Args.Append("-an ");
        pass2Args.Append("-y ");
        pass2Args.Append($"\"{targetPath}\"");

        cancellationToken.ThrowIfCancellationRequested();

        await FFmpegSemaphore.WaitAsync(cancellationToken);
        try
        {
            IConversion pass2 = FFmpeg.Conversions.New();
            pass2.OnDataReceived += (sender, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    logger.LogDebug("FFmpeg [{FileName}]: {Data}", profile.FileName, eventArgs.Data);
            };
            await pass2.Start(pass2Args.ToString(), cancellationToken);
        }
        finally
        {
            FFmpegSemaphore.Release();
        }

        // Clean up pass log files
        try
        {
            string[] passLogFiles = Directory.GetFiles(scratchFolder, $"ffmpeg2pass-{profileIndex}*");
            foreach (string logFile in passLogFiles)
                File.Delete(logFile);
        }
        catch { /* Ignore cleanup errors */ }

        logger.LogInformation("Completed encoding {FileName}", profile.FileName);
    }

    private static string BuildVp9Arguments(VideoQualityProfile profile, string passLogFile)
    {
        int threadCount = Math.Min(Environment.ProcessorCount, 8);
        int tileColumns = (int)Math.Log2(threadCount);

        StringBuilder args = new();
        args.Append("-c:v libvpx-vp9 ");
        args.Append($"-passlogfile \"{passLogFile}\" ");
        args.Append($"-threads {threadCount} ");
        args.Append("-lag-in-frames 16 ");
        args.Append(CultureInfo.InvariantCulture, $"-b:v {profile.Bitrate} ");
        if (!string.IsNullOrEmpty(profile.MaxBitrate))
            args.Append($"-maxrate {profile.MaxBitrate} ");
        if (!string.IsNullOrEmpty(profile.BufferSize))
            args.Append($"-bufsize {profile.BufferSize} ");
        args.Append("-g 250 ");
        args.Append("-cpu-used 4 ");
        args.Append("-auto-alt-ref 1 ");
        args.Append("-arnr-maxframes 7 ");
        args.Append("-arnr-strength 4 ");
        args.Append("-aq-mode 0 ");
        args.Append("-tile-rows 0 ");
        args.Append($"-tile-columns {tileColumns} ");
        args.Append("-row-mt 1 ");
        args.Append("-r 25 ");
        return args.ToString();
    }

    private static void WriteManifest(string scratchFolder, VideoQualityProfile[] profiles, string videoType, Microsoft.Extensions.Logging.ILogger logger)
    {
        string manifestPath = Path.Combine(scratchFolder, $"manifest_{videoType}.txt");
        List<string> lines = [];

        foreach (VideoQualityProfile profile in profiles)
        {
            lines.Add($"{profile.FileName}|{profile.Width}x{profile.Height}|{profile.Bitrate}|{profile.MaxBitrate}|{profile.BufferSize}");
        }

        File.WriteAllLines(manifestPath, lines);
        logger.LogDebug("Wrote {VideoType} manifest with {Count} entries to scratch folder.", videoType, profiles.Length);
    }

    private static bool ValidateManifest(string scratchFolder, VideoQualityProfile[] profiles, string videoType)
    {
        string manifestPath = Path.Combine(scratchFolder, $"manifest_{videoType}.txt");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            string[] lines = File.ReadAllLines(manifestPath);
            if (lines.Length != profiles.Length)
                return false;

            for (int i = 0; i < profiles.Length; i++)
            {
                VideoQualityProfile profile = profiles[i];
                string expectedLine = $"{profile.FileName}|{profile.Width}x{profile.Height}|{profile.Bitrate}|{profile.MaxBitrate}|{profile.BufferSize}";
                if (lines[i] != expectedLine)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetScratchFolder(string packageRoot)
    {
        return Path.Combine(packageRoot, "scratch", "video");
    }
}