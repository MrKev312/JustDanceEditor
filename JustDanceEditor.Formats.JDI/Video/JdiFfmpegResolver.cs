using System.Diagnostics.CodeAnalysis;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Formats.JDI.Video;

public static class JdiFfmpegResolver
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static string? _ffmpegPath;
    private static string? _ffprobePath;

    public static string GetFfmpegPath()
    {
        if (TryUseCachedFfmpeg(out string? cached))
            return cached;

        InitLock.Wait();
        try
        {
            if (TryUseCachedFfmpeg(out cached))
                return cached;

            if (TryResolveBundledExecutables(out string? bundledFfmpegPath, out string? bundledFfprobePath))
                return UseResolvedFfmpeg(bundledFfmpegPath, bundledFfprobePath);

            string downloadDirectory = GetDownloadDirectory();
            Directory.CreateDirectory(downloadDirectory);
            FFmpeg.SetExecutablesPath(downloadDirectory);

            if (!TryResolveDownloadedExecutables(downloadDirectory, out string? ffmpegPath, out string? ffprobePath))
            {
                FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, downloadDirectory).GetAwaiter().GetResult();

                if (!TryResolveDownloadedExecutables(downloadDirectory, out ffmpegPath, out ffprobePath))
                    throw new FileNotFoundException("FFmpeg could not be downloaded by Xabe.");
            }

            return UseResolvedFfmpeg(ffmpegPath, ffprobePath);
        }
        finally
        {
            InitLock.Release();
        }
    }

    public static async Task<string> GetFfmpegPathAsync(CancellationToken cancellationToken = default)
    {
        if (TryUseCachedFfmpeg(out string? cached))
            return cached;

        await InitLock.WaitAsync(cancellationToken);
        try
        {
            if (TryUseCachedFfmpeg(out cached))
                return cached;

            if (TryResolveBundledExecutables(out string? bundledFfmpegPath, out string? bundledFfprobePath))
                return UseResolvedFfmpeg(bundledFfmpegPath, bundledFfprobePath);

            string downloadDirectory = GetDownloadDirectory();
            Directory.CreateDirectory(downloadDirectory);
            FFmpeg.SetExecutablesPath(downloadDirectory);

            if (!TryResolveDownloadedExecutables(downloadDirectory, out string? ffmpegPath, out string? ffprobePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, downloadDirectory);
                cancellationToken.ThrowIfCancellationRequested();

                if (!TryResolveDownloadedExecutables(downloadDirectory, out ffmpegPath, out ffprobePath))
                    throw new FileNotFoundException("FFmpeg could not be downloaded by Xabe.");
            }

            return UseResolvedFfmpeg(ffmpegPath, ffprobePath);
        }
        finally
        {
            InitLock.Release();
        }
    }

    public static string GetFfprobePath()
    {
        if (TryUseCachedFfprobe(out string? cached))
            return cached;

        GetFfmpegPath();

        if (TryUseCachedFfprobe(out cached))
            return cached;

        throw new FileNotFoundException("FFprobe could not be resolved beside FFmpeg.");
    }

    public static async Task<string> GetFfprobePathAsync(CancellationToken cancellationToken = default)
    {
        if (TryUseCachedFfprobe(out string? cached))
            return cached;

        await GetFfmpegPathAsync(cancellationToken);

        if (TryUseCachedFfprobe(out cached))
            return cached;

        throw new FileNotFoundException("FFprobe could not be resolved beside FFmpeg.");
    }

    public static string? TryGetFfplayPath()
    {
        string? ffmpegDirectory = Path.GetDirectoryName(_ffmpegPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ffmpegDirectory))
            return null;

        string ffplayPath = Path.Combine(ffmpegDirectory, GetExecutableName("ffplay"));
        return File.Exists(ffplayPath) ? ffplayPath : null;
    }

    private static bool TryResolveBundledExecutables(
        [NotNullWhen(true)] out string? ffmpegPath,
        [NotNullWhen(true)] out string? ffprobePath)
    {
        foreach (string directory in EnumerateBundledSearchDirectories())
        {
            ffmpegPath = Path.Combine(directory, GetExecutableName("ffmpeg"));
            ffprobePath = Path.Combine(directory, GetExecutableName("ffprobe"));
            if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
                return true;
        }

        ffmpegPath = null;
        ffprobePath = null;
        return false;
    }

    private static IEnumerable<string> EnumerateBundledSearchDirectories()
    {
        yield return AppContext.BaseDirectory;
    }

    private static bool TryResolveDownloadedExecutables(
        string downloadDirectory,
        [NotNullWhen(true)] out string? ffmpegPath,
        [NotNullWhen(true)] out string? ffprobePath)
    {
        ffmpegPath = TryFindDownloadedExecutable("ffmpeg", downloadDirectory);
        ffprobePath = TryFindDownloadedExecutable("ffprobe", downloadDirectory);
        return ffmpegPath is not null && ffprobePath is not null;
    }

    private static string UseResolvedFfmpeg(string ffmpegPath, string ffprobePath)
    {
        string? executableDirectory = Path.GetDirectoryName(ffmpegPath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            throw new FileNotFoundException("Could not determine the downloaded FFmpeg executable directory.", ffmpegPath);

        string? ffprobeDirectory = Path.GetDirectoryName(ffprobePath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(executableDirectory, ffprobeDirectory))
            throw new FileNotFoundException("Downloaded FFmpeg and FFprobe executables are not in the same directory.", ffprobePath);

        FFmpeg.SetExecutablesPath(executableDirectory);
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
        return ffmpegPath;
    }

    private static bool TryUseCachedFfmpeg([NotNullWhen(true)] out string? ffmpegPath)
    {
        ffmpegPath = _ffmpegPath;
        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
            return false;

        string? executableDirectory = Path.GetDirectoryName(ffmpegPath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
            return false;

        FFmpeg.SetExecutablesPath(executableDirectory);
        return true;
    }

    private static bool TryUseCachedFfprobe([NotNullWhen(true)] out string? ffprobePath)
    {
        ffprobePath = _ffprobePath;
        if (string.IsNullOrWhiteSpace(ffprobePath) || !File.Exists(ffprobePath))
            return false;

        return true;
    }

    private static string? TryFindDownloadedExecutable(string baseName, string downloadDirectory)
    {
        string executableName = GetExecutableName(baseName);
        string directPath = Path.Combine(downloadDirectory, executableName);
        if (File.Exists(directPath))
            return directPath;

        try
        {
            return Directory.EnumerateFiles(downloadDirectory, executableName, SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string GetDownloadDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        return Path.Combine(localAppData, "JustDanceEditor", "ffmpeg");
    }

    private static string GetExecutableName(string baseName)
        => OperatingSystem.IsWindows() ? $"{baseName}.exe" : baseName;
}
