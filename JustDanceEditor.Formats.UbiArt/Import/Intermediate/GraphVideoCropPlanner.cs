using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Services;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using KevInc.UbiArt.Cinematics.Timeline;
using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp;

using System.Diagnostics;
using System.Globalization;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal enum GraphVideoImportResult
{
    DirectSource,
    AppliedTransform,
    RequiresFullRender
}

internal static class GraphVideoCropPlanner
{
    public static async Task<GraphVideoImportResult> TryApplyAsync(
        JustDanceUbiArtFileSystem fileSystem,
        CookedFile sourceFile,
        IntermediateSongPackage package,
        string tempSource,
        string destination,
        ILogger logger)
    {
        JdiVideoInfo? videoInfo = await JdiVideoConverter.TryInspectVideoAsync(tempSource);
        if (videoInfo == null || videoInfo.Width <= 0 || videoInfo.Height <= 0)
            return GraphVideoImportResult.RequiresFullRender;

        double duration = IntermediateVideoTiming.GetCinematicRenderDurationSeconds(package, videoInfo.Duration.TotalSeconds);
        if (!CinematicPrerenderedVideoAnalyzer.TryAnalyzeSingleVideoScene(
            fileSystem,
            sourceFile,
            duration,
            logger,
            out CinematicSingleVideoScene scene))
        {
            return GraphVideoImportResult.RequiresFullRender;
        }

        Rectangle crop = CalculateSourceCrop(videoInfo.Width, videoInfo.Height, scene);
        if (crop == new Rectangle(0, 0, videoInfo.Width, videoInfo.Height))
        {
            logger.LogDebug(
                "Pre-rendered single-video scene '{Video}' maps the complete native source into its graph viewport; preserving the source without transcoding.",
                Path.GetFileName(sourceFile.RelativePath));
            return GraphVideoImportResult.DirectSource;
        }

        string ffmpegPath = await JdiFfmpegResolver.GetFfmpegPathAsync();
        string tempOutput = Path.Combine(
            Path.GetDirectoryName(destination) ?? fileSystem.TempFolders.MapFolder,
            $"{Path.GetFileNameWithoutExtension(destination)}_{Guid.NewGuid():N}.webm");
        string[] args = CreateFfmpegArguments(tempSource, tempOutput, BuildFilter(videoInfo.Width, videoInfo.Height, scene));
        await RunFfmpegAsync(ffmpegPath, args, logger);
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(tempOutput, destination);

        bool scaledTo1080p = ShouldScaleSimpleStretchTo1080p(videoInfo.Width, videoInfo.Height, crop);
        if (scaledTo1080p)
        {
            logger.LogInformation(
                "Applied graph-authored 16:9 fill crop to pre-rendered single-video scene '{Video}', from {SourceWidth}x{SourceHeight} to {CropWidth}x{CropHeight} at ({CropX}, {CropY}), then scaled to 1920x1080; scene source '{SceneVideoPath}', output actor '{OutputActorKey}'.",
                Path.GetFileName(sourceFile.RelativePath),
                videoInfo.Width,
                videoInfo.Height,
                crop.Width,
                crop.Height,
                crop.X,
                crop.Y,
                scene.SourceVideoPath,
                scene.OutputActorKey);
        }
        else
        {
            logger.LogInformation(
                "Applied graph-authored native crop to pre-rendered single-video scene '{Video}', from {SourceWidth}x{SourceHeight} to {CropWidth}x{CropHeight} at ({CropX}, {CropY}), without scaling; scene source '{SceneVideoPath}', output actor '{OutputActorKey}'.",
                Path.GetFileName(sourceFile.RelativePath),
                videoInfo.Width,
                videoInfo.Height,
                crop.Width,
                crop.Height,
                crop.X,
                crop.Y,
                scene.SourceVideoPath,
                scene.OutputActorKey);
        }

        return GraphVideoImportResult.AppliedTransform;
    }

    public static string BuildFilter(int sourceWidth, int sourceHeight, CinematicSingleVideoScene scene)
    {
        Rectangle crop = CalculateSourceCrop(sourceWidth, sourceHeight, scene);
        string cropFilter = $"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}";
        return ShouldScaleSimpleStretchTo1080p(sourceWidth, sourceHeight, crop)
            ? $"{cropFilter},scale=1920:1080,setsar=1"
            : $"{cropFilter},setsar=1";
    }

    public static Rectangle CalculateSourceCrop(int sourceWidth, int sourceHeight, CinematicSingleVideoScene scene)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scene.OutputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scene.OutputHeight);

        ProjectedQuad quad = scene.OutputQuad;
        double left = Math.Min(Math.Min(quad.TopLeft.X, quad.TopRight.X), Math.Min(quad.BottomLeft.X, quad.BottomRight.X));
        double top = Math.Min(Math.Min(quad.TopLeft.Y, quad.TopRight.Y), Math.Min(quad.BottomLeft.Y, quad.BottomRight.Y));
        double right = Math.Max(Math.Max(quad.TopLeft.X, quad.TopRight.X), Math.Max(quad.BottomLeft.X, quad.BottomRight.X));
        double bottom = Math.Max(Math.Max(quad.TopLeft.Y, quad.TopRight.Y), Math.Max(quad.BottomLeft.Y, quad.BottomRight.Y));
        double quadWidth = right - left;
        double quadHeight = bottom - top;
        if (quadWidth <= 0 || quadHeight <= 0)
            return new Rectangle(0, 0, RoundToEven(sourceWidth), RoundToEven(sourceHeight));

        (double coverX, double coverY, double coverWidth, double coverHeight) =
            CalculateCover(sourceWidth, sourceHeight, quadWidth / quadHeight);
        double visibleLeft = Math.Clamp(-left / quadWidth, 0, 1);
        double visibleTop = Math.Clamp(-top / quadHeight, 0, 1);
        double visibleRight = Math.Clamp((scene.OutputWidth - left) / quadWidth, 0, 1);
        double visibleBottom = Math.Clamp((scene.OutputHeight - top) / quadHeight, 0, 1);
        return CreateEvenCropRectangle(
            sourceWidth,
            sourceHeight,
            coverX + (coverWidth * visibleLeft),
            coverY + (coverHeight * visibleTop),
            coverWidth * (visibleRight - visibleLeft),
            coverHeight * (visibleBottom - visibleTop));
    }

    private static (double X, double Y, double Width, double Height) CalculateCover(
        int sourceWidth,
        int sourceHeight,
        double targetAspect)
    {
        double sourceAspect = sourceWidth / (double)sourceHeight;
        if (sourceAspect > targetAspect)
        {
            double width = sourceHeight * targetAspect;
            return ((sourceWidth - width) * 0.5, 0, width, sourceHeight);
        }

        if (sourceAspect < targetAspect)
        {
            double height = sourceWidth / targetAspect;
            return (0, (sourceHeight - height) * 0.5, sourceWidth, height);
        }

        return (0, 0, sourceWidth, sourceHeight);
    }

    private static Rectangle CreateEvenCropRectangle(
        int sourceWidth,
        int sourceHeight,
        double desiredX,
        double desiredY,
        double desiredWidth,
        double desiredHeight)
    {
        int width = Math.Clamp(RoundToNearestEven(desiredWidth), 2, RoundToEven(sourceWidth));
        int height = Math.Clamp(RoundToNearestEven(desiredHeight), 2, RoundToEven(sourceHeight));
        int x = RoundToEven(Math.Clamp(RoundToNearestEven(desiredX), 0, sourceWidth - width));
        int y = RoundToEven(Math.Clamp(RoundToNearestEven(desiredY), 0, sourceHeight - height));
        return new Rectangle(x, y, width, height);
    }

    private static int RoundToNearestEven(double value)
    {
        int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        if ((rounded & 1) == 0)
            return rounded;

        int lower = rounded - 1;
        int upper = rounded + 1;
        return Math.Abs(value - lower) <= Math.Abs(upper - value) ? lower : upper;
    }

    private static int RoundToEven(int value) => value > 0 && value % 2 != 0 ? value - 1 : value;

    private static bool ShouldScaleSimpleStretchTo1080p(int sourceWidth, int sourceHeight, Rectangle crop)
    {
        const double aspectTolerance = 0.002;
        double sourceAspect = sourceWidth / (double)sourceHeight;
        double cropAspect = crop.Width / (double)crop.Height;
        return Math.Abs(sourceAspect - (4.0 / 3.0)) <= aspectTolerance &&
            Math.Abs(cropAspect - (16.0 / 9.0)) <= aspectTolerance;
    }

    private static string[] CreateFfmpegArguments(string source, string destination, string filter) =>
    [
        "-hide_banner", "-y", "-i", source, "-an", "-vf", filter,
        "-c:v", "libvpx", "-deadline", "realtime", "-cpu-used", "8",
        "-threads", Math.Max(1, Environment.ProcessorCount).ToString(CultureInfo.InvariantCulture),
        "-lag-in-frames", "0", "-auto-alt-ref", "0", "-crf", "10",
        "-b:v", "12M", "-maxrate", "18M", "-bufsize", "24M", "-pix_fmt", "yuv420p", destination
    ];

    private static async Task RunFfmpegAsync(string ffmpegPath, IReadOnlyList<string> args, ILogger logger)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };
        foreach (string arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {stderr}");
        if (!string.IsNullOrWhiteSpace(stdout))
            logger.LogTrace("ffmpeg stdout: {Output}", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
            logger.LogTrace("ffmpeg stderr: {Output}", stderr);
    }
}
