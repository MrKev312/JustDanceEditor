using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;

using Microsoft.Win32.SafeHandles;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

internal readonly record struct PleoUvBounds(float Left, float Top, float Right, float Bottom)
{
    public static PleoUvBounds Empty { get; } = new(0, 0, 0, 0);
    public bool IsEmpty => Right <= Left || Bottom <= Top;
}

internal sealed class PleoFrameProvider : IDisposable
{
    private readonly string framesFolder;
    private readonly SafeFileHandle? rawHandle;
    private readonly string? pipeSourcePath;
    private readonly long rawLength;
    private readonly int sourceWidth;
    private readonly int visibleHeight;
    private readonly int alphaHeight;
    private readonly int rawFrameByteLength;
    private readonly int stackedFrameByteLength;
    private Process? pipeProcess;
    private Stream? pipeOutput;
    private StringBuilder? pipeError;
    private int pipeSourceFrame;
    private Bgra32[]? pipePixels;

    private PleoFrameProvider(
        string framesFolder,
        SafeFileHandle? rawHandle,
        string? pipeSourcePath,
        long rawLength,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight)
    {
        this.framesFolder = framesFolder;
        this.rawHandle = rawHandle;
        this.pipeSourcePath = pipeSourcePath;
        this.rawLength = rawLength;
        this.sourceWidth = sourceWidth;
        this.visibleHeight = visibleHeight;
        this.alphaHeight = alphaHeight;
        rawFrameByteLength = checked(sourceWidth * visibleHeight * 4);
        stackedFrameByteLength = checked(sourceWidth * (visibleHeight + alphaHeight) * 4);
    }

    public static PleoFrameProvider Create(
        string framesFolder,
        string? rawPath,
        int sourceWidth,
        int visibleHeight,
        int alphaHeight,
        string? pipeSourcePath = null)
    {
        SafeFileHandle? rawHandle = null;
        long rawLength = 0;
        if (string.IsNullOrWhiteSpace(pipeSourcePath) &&
            !string.IsNullOrWhiteSpace(rawPath) &&
            File.Exists(rawPath))
        {
            rawHandle = File.OpenHandle(
                rawPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileOptions.RandomAccess);
            rawLength = RandomAccess.GetLength(rawHandle);
        }

        return new PleoFrameProvider(framesFolder, rawHandle, pipeSourcePath, rawLength, sourceWidth, visibleHeight, alphaHeight);
    }

    public PleoFrameSource? TryLoad(int outputFrame)
    {
        // FFmpeg's image2/raw exports start at source frame 1. Some legacy exports
        // contain dropped Pleo frames, so the source-frame offset may be scheduled.
        int sourceFrame = outputFrame + 1 + LegacyCinematicCpuDraw.GetPleoFrameOffset(outputFrame);
        if (sourceFrame <= 0)
            return null;

        if (!string.IsNullOrWhiteSpace(pipeSourcePath))
            return TryLoadPipeFrame(sourceFrame);

        return rawHandle != null
            ? TryLoadRawFrame(sourceFrame)
            : TryLoadStackedPngFrame(sourceFrame);
    }

    public void Dispose()
    {
        rawHandle?.Dispose();
        pipeOutput?.Dispose();
        if (pipeProcess != null)
        {
            try
            {
                if (!pipeProcess.HasExited)
                    pipeProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup for the ffmpeg pipe.
            }

            pipeProcess.Dispose();
        }
    }

    private PleoFrameSource? TryLoadPipeFrame(int sourceFrame)
    {
        if (string.IsNullOrWhiteSpace(pipeSourcePath))
            return null;

        if (sourceFrame < pipeSourceFrame)
            return null;

        EnsurePipeStarted(pipeSourcePath);
        if (pipeOutput == null)
            return null;

        pipePixels ??= new Bgra32[checked(sourceWidth * (visibleHeight + alphaHeight))];
        Span<byte> bytes = MemoryMarshal.AsBytes(pipePixels.AsSpan());
        while (pipeSourceFrame < sourceFrame)
        {
            if (!ReadExactly(pipeOutput, bytes))
            {
                ThrowIfPipeFailed();
                return null;
            }

            pipeSourceFrame++;
        }

        return new PleoFrameSource(
            pipePixels,
            pipePixels,
            sourceWidth,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            new PleoUvBounds(0, 0, 1, 1),
            isCutout: false);
    }

    private void EnsurePipeStarted(string sourcePath)
    {
        if (pipeProcess != null)
            return;

        ProcessStartInfo startInfo = new()
        {
            FileName = ResolveFfmpegExecutable(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"fps={LegacyCinematicConstants.OutputFramesPerSecond},format=bgra");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("rawvideo");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("bgra");
        startInfo.ArgumentList.Add("pipe:1");

        pipeProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start ffmpeg for legacy cinematic Pleo pipe.");
        pipeError = new StringBuilder();
        pipeProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                pipeError.AppendLine(e.Data);
        };
        pipeProcess.BeginErrorReadLine();
        pipeOutput = pipeProcess.StandardOutput.BaseStream;
    }

    private void ThrowIfPipeFailed()
    {
        if (pipeProcess is not { HasExited: true } ||
            pipeProcess.ExitCode == 0)
        {
            return;
        }

        string error = pipeError?.ToString() ?? string.Empty;
        throw new InvalidOperationException(
            $"ffmpeg Pleo pipe exited with code {pipeProcess.ExitCode}: {error}");
    }

    internal static string ResolveFfmpegExecutable()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(start);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        return "ffmpeg";
    }

    internal static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        int bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            int read = stream.Read(buffer[bytesRead..]);
            if (read <= 0)
                return false;

            bytesRead += read;
        }

        return true;
    }

    private PleoFrameSource? TryLoadRawFrame(int sourceFrame)
    {
        if (rawHandle == null)
            return null;

        long offset = (sourceFrame - 1L) * rawFrameByteLength;
        if (offset < 0 || offset + rawFrameByteLength > rawLength)
            return null;

        Bgra32[] cutoutPixels = new Bgra32[sourceWidth * visibleHeight];
        Span<byte> bytes = MemoryMarshal.AsBytes(cutoutPixels.AsSpan());
        int bytesRead = 0;
        while (bytesRead < bytes.Length)
        {
            int read = RandomAccess.Read(rawHandle, bytes[bytesRead..], offset + bytesRead);
            if (read <= 0)
                return null;

            bytesRead += read;
        }

        PleoUvBounds alphaBounds = ShouldUseFullRawPleoAlphaBounds()
            ? new PleoUvBounds(0, 0, 1, 1)
            : PleoFrameProvider.ComputePleoCutoutAlphaBounds(cutoutPixels, sourceWidth, visibleHeight);
        return new PleoFrameSource(
            cutoutPixels,
            cutoutPixels,
            sourceWidth,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            alphaBounds,
            isCutout: true);
    }

    internal static bool ShouldUseFullRawPleoAlphaBounds() =>
        LegacyCinematicRenderBackend.DefaultKind == LegacyCinematicRenderBackendKind.Vulkan;

    private PleoFrameSource? TryLoadStackedPngFrame(int sourceFrame)
    {
        string sourcePath = Path.Combine(framesFolder, $"source_{sourceFrame:D05}.png");
        if (!File.Exists(sourcePath))
            return null;

        using Image<Bgra32> stacked = Image.Load<Bgra32>(sourcePath);
        if (stacked.Width < sourceWidth || stacked.Height < visibleHeight + alphaHeight)
            return null;

        Bgra32[] pixels = new Bgra32[stacked.Width * stacked.Height];
        stacked.CopyPixelDataTo(pixels);
        PleoUvBounds alphaBounds = LegacyCinematicCpuDraw.ComputePleoAlphaBounds(pixels, stacked.Width, sourceWidth, visibleHeight, alphaHeight);
        Bgra32[] cutoutPixels = LegacyCinematicCpuDraw.CreatePleoCutoutPixels(pixels, stacked.Width, sourceWidth, visibleHeight, alphaHeight);
        return new PleoFrameSource(
            pixels,
            cutoutPixels,
            stacked.Width,
            sourceWidth,
            visibleHeight,
            alphaHeight,
            alphaBounds,
            isCutout: false);
    }

    internal static PleoUvBounds ComputePleoCutoutAlphaBounds(
        IReadOnlyList<Bgra32> pixels,
        int width,
        int height)
    {
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                if (pixels[rowOffset + x].A == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
            return PleoUvBounds.Empty;

        float maxSourceX = Math.Max(1, width - 1);
        float maxSourceY = Math.Max(1, height - 1);
        return new PleoUvBounds(
            Math.Max(0.0f, (minX - 1) / maxSourceX),
            Math.Max(0.0f, (minY - 1) / maxSourceY),
            Math.Min(1.0f, (maxX + 1) / maxSourceX),
            Math.Min(1.0f, (maxY + 1) / maxSourceY));
    }
}

internal sealed class PleoFrameSource(
    Bgra32[] pixels,
    Bgra32[] cutoutPixels,
    int stackWidth,
    int sourceWidth,
    int visibleHeight,
    int alphaHeight,
    PleoUvBounds alphaBounds,
    bool isCutout) : IDisposable
{
    public Bgra32[] Pixels { get; } = pixels;
    public Bgra32[] CutoutPixels { get; } = cutoutPixels;
    public int StackWidth { get; } = stackWidth;
    public int SourceWidth { get; } = sourceWidth;
    public int VisibleHeight { get; } = visibleHeight;
    public int AlphaHeight { get; } = alphaHeight;
    public PleoUvBounds AlphaBounds { get; } = alphaBounds;
    public bool IsCutout { get; } = isCutout;

    public void Dispose()
    {
    }
}