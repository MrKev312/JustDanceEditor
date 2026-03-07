using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Text;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.JDI.Video;

public static class JdiVideoConverter
{
    private static readonly string[] AllowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
    // Limit total concurrent FFmpeg processes
    private static readonly SemaphoreSlim FFmpegSemaphore = new(Math.Max(1, Environment.ProcessorCount / 2), Math.Max(1, Environment.ProcessorCount / 2));
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

    public static async Task EnsurePreviewVideosAsync(IntermediateSongPackage package, string packageRoot, ILogger logger, CancellationToken ct = default)
    {
        (TimeSpan start, TimeSpan dur) = package.TimelineStructure.GetVideoPreviewTiming();

        await ProcessBatchAsync(
            packageRoot,
            JdiVideoProfiles.Previews,
            "preview",
            IntermediatePackageLayout.Assets.PreviewVideoFolder,
            logger,
            ct,
            start,
            dur
        );
    }

    public static async Task EnsureBackgroundVideosAsync(string packageRoot, ILogger logger, CancellationToken ct = default)
    {
        await ProcessBatchAsync(
            packageRoot,
            JdiVideoProfiles.Masters,
            "master",
            IntermediatePackageLayout.Assets.VideoFolder, // Check assets for source-of-truth
            logger,
            ct
        );
    }

    public static async Task<string?> EnsureWiiVideoAsync(string packageRoot, ILogger logger, CancellationToken ct = default)
    {
        await EnsureFFmpegInitializedAsync();
        string assetsFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string scratchFolder = GetScratchFolder(packageRoot);

        string? sourceVideo = SelectSourceVideo(assetsFolder);
        if (sourceVideo == null)
            return LogNotFound(logger);

        string cachedPath = Path.Combine(scratchFolder, $"{Path.GetFileNameWithoutExtension(sourceVideo)}_wii.webm");
        if (File.Exists(cachedPath))
            return cachedPath;

        Directory.CreateDirectory(scratchFolder);

        // Wii Specifics: VP8, 512x384, Fixed Filters
        string filter = "scale=512:384:flags=bicubic,setsar=1,setdar=4/3";
        string codecArgs = "-c:v vp8 -profile:v 2 -pix_fmt yuv420p -auto-alt-ref 0 -b:v 2000k -quality good -cpu-used 16";

        logger.LogInformation("Transcoding Wii video (2-pass)...");
        await RunTwoPassEncodingAsync(sourceVideo, cachedPath, filter, codecArgs, TimeSpan.Zero, TimeSpan.Zero, logger, ct);

        return cachedPath;
    }

    public static async Task<string?> EnsureVideoFormatAsync(string packageRoot, string targetExt, string codec, ILogger logger, CancellationToken ct = default)
    {
        await EnsureFFmpegInitializedAsync();
        string assetsFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string scratchFolder = GetScratchFolder(packageRoot);

        string? sourceVideo = SelectSourceVideo(assetsFolder);
        if (sourceVideo == null)
            return LogNotFound(logger);

        // Check if source already matches requirements
        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(sourceVideo, ct);
        IVideoStream? vidStream = mediaInfo.VideoStreams.FirstOrDefault();
        if (vidStream?.Codec.Equals(codec, StringComparison.OrdinalIgnoreCase) == true &&
            Path.GetExtension(sourceVideo).Equals(targetExt, StringComparison.OrdinalIgnoreCase))
        {
            return sourceVideo;
        }

        string cachedPath = Path.Combine(scratchFolder, $"{Path.GetFileNameWithoutExtension(sourceVideo)}_{codec.Replace(":", "")}{targetExt}");
        if (File.Exists(cachedPath))
            return cachedPath;

        Directory.CreateDirectory(scratchFolder);

        // Derive bitrate from source or default to 4M
        long bitrate = vidStream?.Bitrate > 0 ? vidStream.Bitrate : 4_000_000;

        string codecArgs;
        if (codec == "vp8")
            codecArgs = $"-c:v vp8 -b:v {bitrate} -maxrate {bitrate * 1.5} -bufsize {bitrate * 3} -quality good -cpu-used 1 -slices 4";
        else if (codec == "vp9")
            codecArgs = $"-c:v vp9 -b:v {bitrate} -maxrate {bitrate * 1.5} -bufsize {bitrate * 3} -quality good -speed 4 -row-mt 1";
        else
            codecArgs = $"-c:v {codec} -b:v {bitrate}";

        logger.LogInformation("Transcoding legacy video to {Codec} (2-pass)...", codec);
        await RunTwoPassEncodingAsync(sourceVideo, cachedPath, "", codecArgs, TimeSpan.Zero, TimeSpan.Zero, logger, ct);

        return cachedPath;
    }

    // Shared logic for Previews and Backgrounds
    private static async Task ProcessBatchAsync(
        string packageRoot,
        VideoQualityProfile[] profiles,
        string type,
        string truthFolderAbsPath,
        ILogger logger,
        CancellationToken ct,
        TimeSpan start = default,
        TimeSpan duration = default)
    {
        await EnsureFFmpegInitializedAsync();
        string scratchFolder = GetScratchFolder(packageRoot);
        string assetsVideoFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);

        // 1. Check Source-of-Truth
        if (HasCompleteSet(truthFolderAbsPath, profiles, type))
        {
            logger.LogDebug("Source-of-truth {Type} videos found.", type);
            return;
        }

        // 2. Check Scratch
        if (HasCompleteSet(scratchFolder, profiles, type) && ValidateManifest(scratchFolder, profiles, type))
        {
            logger.LogDebug("Scratch {Type} videos found and valid.", type);
            return;
        }

        // 3. Find Source
        string? sourceVideo = SelectSourceVideo(assetsVideoFolder);
        if (sourceVideo == null)
        {
            logger.LogWarning("Cannot generate {Type} videos; no source found.", type);
            return;
        }

        Directory.CreateDirectory(scratchFolder);
        logger.LogInformation("Generating {Count} {Type} videos...", profiles.Length, type);

        // 4. Calculate Base Crop (once)
        IMediaInfo info = await FFmpeg.GetMediaInfo(sourceVideo, ct);
        IVideoStream vStream = info.VideoStreams.First();
        string baseCrop = GetCropFilter(vStream.Width, vStream.Height);

        // 5. Run Parallel
        await Parallel.ForEachAsync(profiles, new ParallelOptions { MaxDegreeOfParallelism = profiles.Length, CancellationToken = ct }, async (profile, token) =>
        {
            string targetPath = Path.Combine(scratchFolder, profile.FileName);
            string filter = BuildFilterChain(baseCrop, profile.Width, profile.Height, duration);
            string codecArgs = BuildVp9ProfileArgs(profile);

            await RunTwoPassEncodingAsync(sourceVideo, targetPath, filter, codecArgs, start, duration, logger, token);
        });

        WriteManifest(scratchFolder, profiles, type, logger);
        logger.LogInformation("{Type} generation complete.", type);
    }

    private static async Task RunTwoPassEncodingAsync(
        string input,
        string output,
        string videoFilter,
        string codecArgs,
        TimeSpan start,
        TimeSpan duration,
        ILogger logger,
        CancellationToken ct)
    {
        string passLogPrefix = Path.Combine(Path.GetDirectoryName(output)!, $"ffmpeg2pass_{Guid.NewGuid()}");
        string nullOutput = Path.DirectorySeparatorChar == '\\' ? "NUL" : "/dev/null";

        // Construct timing args
        string timeArgs = "";
        if (start > TimeSpan.Zero)
            timeArgs += string.Format(CultureInfo.InvariantCulture, "-ss {0} ", start.TotalSeconds);
        if (duration > TimeSpan.Zero)
            timeArgs += string.Format(CultureInfo.InvariantCulture, "-t {0} ", duration.TotalSeconds);

        string vfArg = string.IsNullOrWhiteSpace(videoFilter) ? "" : $"-vf \"{videoFilter}\"";

        // Pass 1
        await FFmpegSemaphore.WaitAsync(ct);
        try
        {
            // Note: -an (no audio) is standard for pass 1
            string p1Args = $"{timeArgs} -i \"{input}\" {codecArgs} {vfArg} -pass 1 -passlogfile \"{passLogPrefix}\" -an -f null {nullOutput}";
            await FFmpeg.Conversions.New().Start(p1Args, ct);
        }
        finally
        {
            FFmpegSemaphore.Release();
        }

        // Pass 2
        await FFmpegSemaphore.WaitAsync(ct);
        try
        {
            // -an is explicitly kept from original code for Wii/Previews
            string p2Args = $"{timeArgs} -i \"{input}\" {codecArgs} {vfArg} -pass 2 -passlogfile \"{passLogPrefix}\" -an -y \"{output}\"";
            IConversion conv = FFmpeg.Conversions.New();
            conv.OnDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    logger.LogTrace("{File}: {Data}", Path.GetFileName(output), e.Data);
            };
            await conv.Start(p2Args, ct);
        }
        finally
        {
            FFmpegSemaphore.Release();
        }

        // Cleanup Logs
        try
        {
            foreach (string f in Directory.GetFiles(Path.GetDirectoryName(output)!, Path.GetFileName(passLogPrefix) + "*"))
                File.Delete(f);
        }
        catch { /* ignore */ }
    }

    private static string BuildVp9ProfileArgs(VideoQualityProfile p)
    {
        int threadCount = Math.Min(Environment.ProcessorCount, 8);
        int tileColumns = (int)Math.Log2(threadCount);

        StringBuilder sb = new("-c:v vp9 ");
        sb.Append(CultureInfo.InvariantCulture, $"-b:v {p.Bitrate} ");
        if (!string.IsNullOrEmpty(p.MaxBitrate))
            sb.Append($"-maxrate {p.MaxBitrate} ");
        if (!string.IsNullOrEmpty(p.BufferSize))
            sb.Append($"-bufsize {p.BufferSize} ");

        sb.Append($"-threads {threadCount} -tile-columns {tileColumns} -tile-rows 0 -row-mt 1 ");
        sb.Append("-g 250 -lag-in-frames 16 -cpu-used 4 -auto-alt-ref 1 -arnr-maxframes 7 -arnr-strength 4 -aq-mode 0 -r 25");
        return sb.ToString();
    }

    private static string GetCropFilter(int width, int height)
    {
        float ratio = width / (float)height;
        // 16:9 is ~1.777
        if (Math.Abs(ratio - (16f / 9f)) < 0.001f)
            return "";
        return ratio < (16f / 9f) ? "crop=in_w:in_w*9/16" : "crop=in_h*16/9:in_h";
    }

    private static string BuildFilterChain(string baseCrop, int? w, int? h, TimeSpan duration)
    {
        List<string> f = [];
        if (!string.IsNullOrEmpty(baseCrop))
            f.Add(baseCrop);

        if (w.HasValue && h.HasValue)
            f.Add($"scale={w}:{h}");

        if (duration > TimeSpan.Zero)
        {
            double fadeOutStart = Math.Max(0, duration.TotalSeconds - 1);
            f.Add($"fade=t=in:st=0:d=1");
            f.Add(string.Format(CultureInfo.InvariantCulture, "fade=t=out:st={0}:d=1", fadeOutStart));
        }

        return string.Join(",", f);
    }

    private static string GetScratchFolder(string root) => Path.Combine(root, "scratch", "video");

    private static string? SelectSourceVideo(string folder)
    {
        if (!Directory.Exists(folder))
            return null;
        return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(f => AllowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    private static string? LogNotFound(ILogger log)
    {
        log.LogWarning("Source video not found.");
        return null;
    }

    private static bool HasCompleteSet(string folder, VideoQualityProfile[] profiles, string prefix)
    {
        if (!Directory.Exists(folder))
            return false;
        int count = Directory.EnumerateFiles(folder)
            .Count(f => Path.GetFileName(f).StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase)
                     && AllowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        return count >= profiles.Length;
    }

    private static void WriteManifest(string folder, VideoQualityProfile[] profiles, string type, ILogger log)
    {
        IEnumerable<string> lines = profiles.Select(p => $"{p.FileName}|{p.Width}x{p.Height}|{p.Bitrate}|{p.MaxBitrate}|{p.BufferSize}");
        File.WriteAllLines(Path.Combine(folder, $"manifest_{type}.txt"), lines);
    }

    private static bool ValidateManifest(string folder, VideoQualityProfile[] profiles, string type)
    {
        try
        {
            string path = Path.Combine(folder, $"manifest_{type}.txt");
            if (!File.Exists(path))
                return false;

            string[] lines = File.ReadAllLines(path);
            if (lines.Length != profiles.Length)
                return false;

            for (int i = 0; i < profiles.Length; i++)
            {
                if (lines[i] != $"{profiles[i].FileName}|{profiles[i].Width}x{profiles[i].Height}|{profiles[i].Bitrate}|{profiles[i].MaxBitrate}|{profiles[i].BufferSize}")
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}