using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Text;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.JDI.Video;

public sealed record JdiVideoInfo(int Width, int Height, TimeSpan Duration, string Codec);

public static class JdiVideoConverter
{
    private static readonly string[] AllowedExtensions = [".webm", ".mp4", ".mkv", ".mov"];
    // Limit total concurrent FFmpeg processes
    private static readonly SemaphoreSlim FFmpegSemaphore = new(Math.Max(1, Environment.ProcessorCount / 2), Math.Max(1, Environment.ProcessorCount / 2));

    public static async Task<JdiVideoInfo?> TryInspectVideoAsync(string sourceVideo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceVideo) || !File.Exists(sourceVideo))
            return null;

        await JdiFfmpegResolver.GetFfmpegPathAsync(ct);
        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(sourceVideo, ct);
        IVideoStream? videoStream = mediaInfo.VideoStreams.FirstOrDefault();
        if (videoStream is null)
            return null;

        return new JdiVideoInfo(videoStream.Width, videoStream.Height, mediaInfo.Duration, videoStream.Codec);
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

    public static async Task<string?> GetOrCreateVideoAsync(JdiVideoEncodeRequest request, ILogger logger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await JdiFfmpegResolver.GetFfmpegPathAsync(ct);
        string scratchFolder = GetScratchFolder(request.PackageRoot);
        string cachedPath = Path.Combine(scratchFolder, request.CacheFileName);
        if (File.Exists(cachedPath))
            return cachedPath;

        string? sourceVideo = ResolveSourceVideo(request);
        if (sourceVideo == null)
            return LogNotFound(logger);

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(sourceVideo, ct);
        IVideoStream? vidStream = mediaInfo.VideoStreams.FirstOrDefault();
        Directory.CreateDirectory(Path.GetDirectoryName(cachedPath) ?? scratchFolder);

        long bitrate = vidStream?.Bitrate > 0 ? vidStream.Bitrate : 4_000_000;
        if (!request.ForceTranscode &&
            vidStream?.Codec.Equals(request.Codec, StringComparison.OrdinalIgnoreCase) == true &&
            Path.GetExtension(sourceVideo).Equals(request.ContainerExtension, StringComparison.OrdinalIgnoreCase) &&
            request.Transform.IsEmpty &&
            request.Start <= TimeSpan.Zero &&
            request.Duration <= TimeSpan.Zero)
        {
            File.Copy(sourceVideo, cachedPath, overwrite: true);
            return cachedPath;
        }

        string videoFilter = BuildVideoFilter(request.Transform);
        string codecArgs = BuildVideoCodecArgs(request.Codec, bitrate, request.Encoding);

        logger.LogInformation("Encoding cached JDI video as {Codec}...", request.Codec);
        await RunTwoPassEncodingAsync(sourceVideo, cachedPath, videoFilter, codecArgs, request.Start, request.Duration, logger, ct);

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
        await JdiFfmpegResolver.GetFfmpegPathAsync(ct);
        string scratchFolder = GetScratchFolder(packageRoot);
        string assetsVideoFolder = IntermediatePackageLayout.Resolve(packageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        string truthFolder = IntermediatePackageLayout.Resolve(packageRoot, truthFolderAbsPath);

        // 1. Check Source-of-Truth
        if (HasCompleteSet(truthFolder, profiles, type, requirePrefix: false))
        {
            logger.LogDebug("Source-of-truth {Type} videos found.", type);
            return;
        }

        // 2. Check Scratch
        if (HasCompleteSet(scratchFolder, profiles, type, requirePrefix: true) && ValidateManifest(scratchFolder, profiles, type))
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
        string outputDirectory = Path.GetDirectoryName(output) ?? throw new InvalidOperationException($"Could not determine the directory for '{output}'.");
        string passLogPrefix = Path.Combine(outputDirectory, $"ffmpeg2pass_{Guid.NewGuid()}");
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
            foreach (string f in Directory.GetFiles(outputDirectory, Path.GetFileName(passLogPrefix) + "*"))
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

    private static string? ResolveSourceVideo(JdiVideoEncodeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourcePath))
        {
            if (Path.IsPathFullyQualified(request.SourcePath))
                return File.Exists(request.SourcePath) ? request.SourcePath : null;

            string packageRelative = Path.Combine(request.PackageRoot, request.SourcePath);
            if (File.Exists(packageRelative))
                return packageRelative;
        }

        string assetsFolder = IntermediatePackageLayout.Resolve(request.PackageRoot, IntermediatePackageLayout.Assets.VideoFolder);
        return SelectSourceVideo(assetsFolder);
    }

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

    private static bool HasCompleteSet(string folder, VideoQualityProfile[] profiles, string prefix, bool requirePrefix)
    {
        if (!Directory.Exists(folder))
            return false;
        int count = Directory.EnumerateFiles(folder)
            .Count(f => (!requirePrefix || Path.GetFileName(f).StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
                     && AllowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        return count >= profiles.Length;
    }

    internal static string BuildVideoFilter(JdiVideoTransform transform)
    {
        if (transform.IsEmpty)
            return "";

        List<string> filters = [];
        if (transform.Width.HasValue || transform.Height.HasValue)
        {
            string width = transform.Width?.ToString(CultureInfo.InvariantCulture) ?? "-1";
            string height = transform.Height?.ToString(CultureInfo.InvariantCulture) ?? "-1";
            string scale = $"scale={width}:{height}";
            if (transform.ScaleAlgorithm == JdiScaleAlgorithm.Bicubic)
                scale += ":flags=bicubic";
            filters.Add(scale);
        }

        if (!string.IsNullOrWhiteSpace(transform.SampleAspectRatio))
            filters.Add($"setsar={transform.SampleAspectRatio}");
        if (!string.IsNullOrWhiteSpace(transform.DisplayAspectRatio))
            filters.Add($"setdar={transform.DisplayAspectRatio}");
        filters.AddRange(transform.AdditionalFilters.Where(filter => !string.IsNullOrWhiteSpace(filter)));

        return string.Join(",", filters);
    }

    internal static string BuildVideoCodecArgs(string codec, long sourceBitrate, JdiVideoEncodingSettings settings, bool includeAudioOption = false)
    {
        long bitrate = settings.Bitrate ?? sourceBitrate;
        long? maxBitrate = settings.MaxBitrate;
        long? bufferSize = settings.BufferSize;

        if (settings.UseDefaultCodecTuning)
        {
            maxBitrate ??= (long)(bitrate * 1.5);
            bufferSize ??= bitrate * 3;
        }

        StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"-c:v {ResolveVideoEncoder(codec)} ");
        if (settings.Profile.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-profile:v {settings.Profile.Value} ");
        if (!string.IsNullOrWhiteSpace(settings.PixelFormat))
            builder.Append(CultureInfo.InvariantCulture, $"-pix_fmt {settings.PixelFormat} ");
        if (settings.AutoAltRef.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-auto-alt-ref {(settings.AutoAltRef.Value ? 1 : 0)} ");

        builder.Append(CultureInfo.InvariantCulture, $"-b:v {bitrate} ");
        if (maxBitrate.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-maxrate {maxBitrate.Value} ");
        if (bufferSize.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-bufsize {bufferSize.Value} ");
        if (settings.ConstantRateFactor.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-crf {settings.ConstantRateFactor.Value} ");

        if (!string.IsNullOrWhiteSpace(settings.Quality))
            builder.Append(CultureInfo.InvariantCulture, $"-quality {settings.Quality} ");
        if (settings.CpuUsed.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-cpu-used {settings.CpuUsed.Value} ");
        else if (settings.UseDefaultCodecTuning && codec.Equals("vp8", StringComparison.OrdinalIgnoreCase))
            builder.Append("-cpu-used 1 ");

        if (settings.Speed.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-speed {settings.Speed.Value} ");
        else if (settings.UseDefaultCodecTuning && codec.Equals("vp9", StringComparison.OrdinalIgnoreCase))
            builder.Append("-speed 4 ");

        if (settings.Slices.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-slices {settings.Slices.Value} ");
        else if (settings.UseDefaultCodecTuning && codec.Equals("vp8", StringComparison.OrdinalIgnoreCase))
            builder.Append("-slices 4 ");

        if (settings.RowMultithreading.HasValue)
            builder.Append(CultureInfo.InvariantCulture, $"-row-mt {(settings.RowMultithreading.Value ? 1 : 0)} ");
        else if (settings.UseDefaultCodecTuning && codec.Equals("vp9", StringComparison.OrdinalIgnoreCase))
            builder.Append("-row-mt 1 ");

        if (includeAudioOption && settings.OmitAudio)
            builder.Append("-an ");

        return builder.ToString();
    }

    private static string ResolveVideoEncoder(string codec)
    {
        if (codec.Equals("vp8", StringComparison.OrdinalIgnoreCase))
            return "libvpx";
        if (codec.Equals("vp9", StringComparison.OrdinalIgnoreCase))
            return "libvpx-vp9";

        return codec;
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
