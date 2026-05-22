using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    private const double DefaultFrameRate = 30;

    public async Task<FfmpegVideoFrameInfo> GetVideoInfoAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        await FfmpegExecutableResolver.GetFfmpegPathAsync(cancellationToken);

        IMediaInfo mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
        IVideoStream videoStream = mediaInfo.VideoStreams.FirstOrDefault()
            ?? throw new InvalidOperationException("The selected file does not contain a video stream.");

        int sourceWidth = Math.Max(1, videoStream.Width);
        int sourceHeight = Math.Max(1, videoStream.Height);
        (int outputWidth, int outputHeight) = FitInside(sourceWidth, sourceHeight, MaxPreviewWidth, MaxPreviewHeight);

        return new FfmpegVideoFrameInfo(sourceWidth, sourceHeight, outputWidth, outputHeight, DefaultFrameRate);
    }

    public async Task<Bitmap> ReadFrameAsync(
        string videoPath,
        double timestampSeconds,
        FfmpegVideoFrameInfo info,
        CancellationToken cancellationToken = default)
    {
        string ffmpegPath = await FfmpegExecutableResolver.GetFfmpegPathAsync(cancellationToken);
        using Process process = StartFfmpeg(ffmpegPath, videoPath, timestampSeconds, info, stream: false);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            byte[] frameBytes = new byte[info.OutputWidth * info.OutputHeight * 4];
            int bytesRead = await ReadExactAsync(process.StandardOutput.BaseStream, frameBytes, cancellationToken);

            await WaitForExitOrKillAsync(process, cancellationToken);
            string stderr = await stderrTask;

            if (bytesRead != frameBytes.Length)
                throw new InvalidOperationException($"FFmpeg returned an incomplete video frame. {stderr}".Trim());

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. {stderr}".Trim());

            return CreateBitmap(frameBytes, info.OutputWidth, info.OutputHeight);
        }
        catch
        {
            KillIfRunning(process);
            throw;
        }
    }

    public async Task StreamFramesAsync(
        string videoPath,
        double startSeconds,
        FfmpegVideoFrameInfo info,
        Action<Bitmap> onFrame,
        CancellationToken cancellationToken = default)
    {
        string ffmpegPath = await FfmpegExecutableResolver.GetFfmpegPathAsync(cancellationToken);
        using Process process = StartFfmpeg(ffmpegPath, videoPath, startSeconds, info, stream: true);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        byte[] frameBytes = new byte[info.OutputWidth * info.OutputHeight * 4];
        TimeSpan frameInterval = TimeSpan.FromSeconds(1d / Math.Max(1, info.FrameRate));
        DateTime nextFrameAt = DateTime.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await ReadExactAsync(process.StandardOutput.BaseStream, frameBytes, cancellationToken);
                if (bytesRead == 0)
                    break;

                if (bytesRead != frameBytes.Length)
                    throw new InvalidOperationException("FFmpeg ended before a complete video frame was read.");

                onFrame(CreateBitmap(frameBytes, info.OutputWidth, info.OutputHeight));

                nextFrameAt += frameInterval;
                TimeSpan delay = nextFrameAt - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                else if (delay < -frameInterval)
                    nextFrameAt = DateTime.UtcNow;
            }

            await WaitForExitOrKillAsync(process, cancellationToken);
            string stderr = await stderrTask;
            if (!cancellationToken.IsCancellationRequested && process.ExitCode != 0)
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}. {stderr}".Trim());
        }
        catch
        {
            KillIfRunning(process);
            throw;
        }
    }

    private static Process StartFfmpeg(string ffmpegPath, string videoPath, double startSeconds, FfmpegVideoFrameInfo info, bool stream)
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
        string filter = stream
            ? $"scale={info.OutputWidth}:{info.OutputHeight},fps={info.FrameRate.ToString("0.###", CultureInfo.InvariantCulture)}"
            : $"scale={info.OutputWidth}:{info.OutputHeight}";

        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(timestamp);
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        if (!stream)
        {
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
        }

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

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                break;

            offset += read;
        }

        return offset;
    }

    private static Bitmap CreateBitmap(byte[] bgraPixels, int width, int height)
    {
        using SKBitmap skBitmap = new(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(bgraPixels, 0, skBitmap.GetPixels(), bgraPixels.Length);

        WriteableBitmap bitmap = new(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using ILockedFramebuffer framebuffer = bitmap.Lock();
        CopySkiaPixelsToFramebuffer(skBitmap, framebuffer, width, height);
        return bitmap;
    }

    private static void CopySkiaPixelsToFramebuffer(SKBitmap skBitmap, ILockedFramebuffer framebuffer, int width, int height)
    {
        int sourceStride = skBitmap.RowBytes;
        int targetStride = framebuffer.RowBytes;
        int rowBytes = width * 4;
        byte[] row = new byte[rowBytes];
        IntPtr source = skBitmap.GetPixels();

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(IntPtr.Add(source, y * sourceStride), row, 0, rowBytes);
            Marshal.Copy(row, 0, IntPtr.Add(framebuffer.Address, y * targetStride), rowBytes);
        }
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
        catch
        {
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
