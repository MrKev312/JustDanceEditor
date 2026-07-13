using JustDanceEditor.Formats.JDI.Video;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.Services;

internal sealed record FfmpegVideoFrameInfo(
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    double FrameRate);

internal sealed class FfmpegVideoFrameReader
{
    private const int MaxPreviewWidth = 960;
    private const int MaxPreviewHeight = 540;
    private const double DefaultFrameRate = 25;

    public async Task<FfmpegVideoFrameInfo> GetVideoInfoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
        IVideoStream videoStream = mediaInfo.VideoStreams.FirstOrDefault()
            ?? throw new InvalidOperationException("The selected file does not contain a video stream.");

        int sourceWidth = Math.Max(1, videoStream.Width);
        int sourceHeight = Math.Max(1, videoStream.Height);
        (int outputWidth, int outputHeight) = FitInside(sourceWidth, sourceHeight, MaxPreviewWidth, MaxPreviewHeight);
        double frameRate = videoStream.Framerate;
        if (double.IsNaN(frameRate) || double.IsInfinity(frameRate) || frameRate <= 0)
            frameRate = DefaultFrameRate;

        return new FfmpegVideoFrameInfo(sourceWidth, sourceHeight, outputWidth, outputHeight, Math.Clamp(frameRate, 1, 60));
    }

    public async Task StreamFramesAsync(
        string videoPath,
        double startSeconds,
        FfmpegVideoFrameInfo info,
        Func<byte[], FfmpegVideoFrameInfo, double, CancellationToken, Task<bool>> onFrame,
        bool paceFrames = true,
        CancellationToken cancellationToken = default)
    {
        string ffmpegPath = await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);
        using Process process = StartFfmpeg(ffmpegPath, videoPath, startSeconds, info);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        int frameByteCount = GetFrameByteCount(info);
        byte[] frameBytes = new byte[frameByteCount];
        TimeSpan frameInterval = TimeSpan.FromSeconds(1d / Math.Max(1, info.FrameRate));
        DateTime nextFrameAt = DateTime.UtcNow;
        long frameIndex = 0;
        bool reachedEnd = false;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await ReadExactAsync(process.StandardOutput.BaseStream, frameBytes, frameByteCount, cancellationToken);
                if (bytesRead == 0)
                {
                    reachedEnd = true;
                    break;
                }

                if (bytesRead != frameByteCount)
                    throw new InvalidOperationException("FFmpeg ended before a complete video frame was read.");

                double frameSeconds = startSeconds + (frameIndex / Math.Max(1, info.FrameRate));
                frameIndex++;
                bool shouldContinue = await onFrame(frameBytes, info, frameSeconds, cancellationToken);
                if (!shouldContinue)
                    break;

                if (paceFrames)
                {
                    nextFrameAt += frameInterval;
                    TimeSpan delay = nextFrameAt - DateTime.UtcNow;
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken);
                    else if (delay < -frameInterval)
                        nextFrameAt = DateTime.UtcNow;
                }
            }

            if (!reachedEnd)
                KillIfRunning(process);

            await WaitForExitOrKillAsync(process, cancellationToken);
            string stderr = await stderrTask;
            if (!cancellationToken.IsCancellationRequested && reachedEnd && process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. {stderr}".Trim());
        }
        catch
        {
            KillIfRunning(process);
            throw;
        }
    }

    private static Process StartFfmpeg(string ffmpegPath, string videoPath, double startSeconds, FfmpegVideoFrameInfo info)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        string timestamp = Math.Max(0, startSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        string filter = $"scale={info.OutputWidth}:{info.OutputHeight},fps={info.FrameRate.ToString("0.###", CultureInfo.InvariantCulture)}";

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(timestamp);
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        startInfo.ArgumentList.Add("-sws_flags");
        startInfo.ArgumentList.Add("fast_bilinear");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add(filter);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("bgra");
        startInfo.ArgumentList.Add("pipe:1");

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start FFmpeg.");
        return process;
    }

    private static int GetFrameByteCount(FfmpegVideoFrameInfo info)
        => checked(info.OutputWidth * info.OutputHeight * 4);

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int length, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                break;

            offset += read;
        }

        return offset;
    }

    private static async Task WaitForExitOrKillAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillIfRunning(process);
            throw;
        }
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            EditorLog.Unexpected(ex, "Terminate FFmpeg video frame reader");
        }
    }

    private static (int Width, int Height) FitInside(int sourceWidth, int sourceHeight, int maxWidth, int maxHeight)
    {
        double scale = Math.Min(maxWidth / (double)sourceWidth, maxHeight / (double)sourceHeight);
        scale = Math.Min(1, scale);

        int width = MakeEven(Math.Max(2, (int)Math.Round(sourceWidth * scale)));
        int height = MakeEven(Math.Max(2, (int)Math.Round(sourceHeight * scale)));
        return (width, height);
    }

    private static int MakeEven(int value) => value % 2 == 0 ? value : value - 1;
}