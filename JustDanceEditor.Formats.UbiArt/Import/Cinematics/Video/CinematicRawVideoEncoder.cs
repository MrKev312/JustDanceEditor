using JustDanceEditor.Formats.JDI.Video;

using KevInc.UbiArt.Cinematics.Core;

using Microsoft.Extensions.Logging;

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal static class CinematicRawVideoEncoder
{
    public static async Task EncodeRawFramesAsync(
        string destination,
        int outputWidth,
        int outputHeight,
        int frameCount,
        Action<Stream, bool> renderFrames,
        ILogger logger,
        bool preserveAlpha = false)
    {
        if (Path.GetExtension(destination).Equals(".speedtest", StringComparison.OrdinalIgnoreCase))
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            renderFrames(Stream.Null, true);
            stopwatch.Stop();
            logger.LogInformation(
                "Rendered {FrameCount} cinematic frame(s) to null output in {RenderElapsed:0.###}s ({FramesPerSecond:0.###} fps).",
                frameCount,
                stopwatch.Elapsed.TotalSeconds,
                frameCount / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds));
            logger.LogDebug("Discarded cinematic speedtest output for '{Destination}'.", destination);
            return;
        }

        string args =
            "-v error " +
            $"-f rawvideo -pix_fmt bgra -s {outputWidth}x{outputHeight} -r {CinematicConstants.OutputFramesPerSecond} -i pipe:0 " +
            CreateRawPipeCodecArgs(preserveAlpha) +
            $"-y \"{destination}\"";

        ProcessStartInfo startInfo = new(JdiFfmpegResolver.GetFfmpegPath(), args)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        StringBuilder stderr = new();
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stderr.AppendLine(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start ffmpeg raw video writer.");

        process.BeginErrorReadLine();
        Stopwatch renderStopwatch = Stopwatch.StartNew();
        try
        {
            Stream outputStream = process.StandardInput.BaseStream;
            renderFrames(outputStream, false);
            outputStream.Flush();
            process.StandardInput.Close();
        }
        catch
        {
            TryKill(process);
            throw;
        }

        renderStopwatch.Stop();
        Stopwatch ffmpegDrainStopwatch = Stopwatch.StartNew();
        await process.WaitForExitAsync().ConfigureAwait(false);
        ffmpegDrainStopwatch.Stop();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg raw video writer exited with code {process.ExitCode}: {stderr}");
        }

        logger.LogInformation(
            "Piped {FrameCount} raw cinematic frame(s) in {RenderElapsed:0.###}s ({FramesPerSecond:0.###} fps); ffmpeg drain took {FfmpegElapsed:0.###}s.",
            frameCount,
            renderStopwatch.Elapsed.TotalSeconds,
            frameCount / Math.Max(0.001, renderStopwatch.Elapsed.TotalSeconds),
            ffmpegDrainStopwatch.Elapsed.TotalSeconds);
        logger.LogDebug("Encoded raw cinematic frames directly to '{Destination}'.", destination);
    }

    public static void TryDeleteDirectory(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to delete cinematic temp directory '{Path}'.", path);
        }
    }

    public static void TryDeleteFile(string? path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to delete cinematic temp file '{Path}'.", path);
        }
    }

    private static string CreateRawPipeCodecArgs(bool preserveAlpha)
    {
        int threads = Math.Max(1, Environment.ProcessorCount);
        if (preserveAlpha)
            return $"-an -c:v ffv1 -level 3 -g 1 -slices {ChooseFfv1SliceCount(threads)} -slicecrc 0 -threads {threads} -pix_fmt bgra ";

        return "-an -c:v libvpx -deadline realtime -cpu-used 8 " +
            $"-threads {threads} -lag-in-frames 0 -auto-alt-ref 0 " +
            "-crf 10 -b:v 12M -maxrate 18M -bufsize 24M -pix_fmt yuv420p ";
    }

    private static int ChooseFfv1SliceCount(int threads) =>
        threads >= 16 ? 16 : threads >= 12 ? 12 : threads >= 9 ? 9 : threads >= 6 ? 6 : 4;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }
}
