using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace JustDanceEditor.Editor.Services;

internal static class FfmpegExecutableResolver
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static string? _ffmpegPath;

    public static async Task<string> GetFfmpegPathAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
            return _ffmpegPath;

        await InitLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
                return _ffmpegPath;

            _ffmpegPath = TryFindExecutable();
            if (_ffmpegPath == null)
            {
                string downloadDirectory = GetDownloadDirectory();
                Directory.CreateDirectory(downloadDirectory);
                FFmpeg.SetExecutablesPath(downloadDirectory);

                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, downloadDirectory);
                _ffmpegPath = TryFindExecutable(downloadDirectory, includeNested: true);
            }

            if (_ffmpegPath == null)
                throw new FileNotFoundException("FFmpeg could not be found or downloaded.");

            string? directory = Path.GetDirectoryName(_ffmpegPath);
            if (!string.IsNullOrWhiteSpace(directory))
                FFmpeg.SetExecutablesPath(directory);

            return _ffmpegPath;
        }
        finally
        {
            InitLock.Release();
        }
    }

    private static string GetDownloadDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            localAppData = Path.GetTempPath();

        return Path.Combine(localAppData, "JustDanceEditor", "ffmpeg");
    }

    private static string? TryFindExecutable(bool includeNested = false)
        => TryFindExecutable(null, includeNested);

    private static string? TryFindExecutable(string? preferredDirectory, bool includeNested = false)
    {
        string executableName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        foreach (string directory in GetCandidateDirectories(preferredDirectory))
        {
            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
                return candidate;

            if (includeNested && TryFindNestedExecutable(directory, executableName, out string? nested))
                return nested;
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories(string? preferredDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredDirectory))
            yield return preferredDirectory;

        yield return AppContext.BaseDirectory;
        yield return Environment.CurrentDirectory;
        yield return Directory.GetCurrentDirectory();

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return directory;
    }

    private static bool TryFindNestedExecutable(string directory, string executableName, [NotNullWhen(true)] out string? executablePath)
    {
        executablePath = null;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        try
        {
            executablePath = Directory.EnumerateFiles(directory, executableName, SearchOption.AllDirectories)
                .FirstOrDefault();
            return executablePath != null;
        }
        catch
        {
            return false;
        }
    }
}
