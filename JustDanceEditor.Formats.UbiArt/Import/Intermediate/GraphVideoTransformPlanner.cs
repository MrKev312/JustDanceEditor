using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Video;
using JustDanceEditor.Formats.UbiArt.FileSystem;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Video;

using KevInc.UbiArt.FileSystem;

using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Numerics;

using Xabe.FFmpeg;

namespace JustDanceEditor.Formats.UbiArt.Import.Intermediate;

internal enum GraphVideoImportResult
{
    DirectSource,
    AppliedTransform,
    RequiresFullRender
}

internal static class GraphVideoTransformPlanner
{
    private const float FrameEdgeTolerance = 1.5f;

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

        if (CanUseSourceDirectly(videoInfo.Width, videoInfo.Height, scene))
        {
            logger.LogDebug(
                "Pre-rendered single-video scene '{Video}' maps the complete {Width}x{Height} source to the output framebuffer; preserving the source without transcoding.",
                Path.GetFileName(sourceFile.RelativePath),
                videoInfo.Width,
                videoInfo.Height);
            return GraphVideoImportResult.DirectSource;
        }

        string filter = BuildFilter(videoInfo.Width, videoInfo.Height, scene);
        (int outputWidth, int outputHeight) = CalculateOutputSize(videoInfo.Width, videoInfo.Height, scene);
        string tempOutput = Path.Combine(
            Path.GetDirectoryName(destination) ?? fileSystem.TempFolders.MapFolder,
            $"{Path.GetFileNameWithoutExtension(destination)}_{Guid.NewGuid():N}.webm");
        await RunFfmpegAsync(tempSource, tempOutput, filter, logger);
        if (File.Exists(destination))
            File.Delete(destination);
        File.Move(tempOutput, destination);

        ProjectedQuad outputQuad = NormalizeOutputQuad(scene);
        logger.LogInformation(
            "Mapped the complete {SourceWidth}x{SourceHeight} video texture through graph output quad {OutputQuad} into a {OutputWidth}x{OutputHeight} frame; scene source '{SceneVideoPath}', output actor '{OutputActorKey}'.",
            videoInfo.Width,
            videoInfo.Height,
            outputQuad,
            outputWidth,
            outputHeight,
            scene.SourceVideoPath,
            scene.OutputActorKey);

        return GraphVideoImportResult.AppliedTransform;
    }

    public static bool CanUseSourceDirectly(
        int sourceWidth,
        int sourceHeight,
        CinematicSingleVideoScene scene)
    {
        ValidateDimensions(sourceWidth, sourceHeight, scene);
        return IsFullFramebufferQuad(NormalizeOutputQuad(scene), scene.OutputWidth, scene.OutputHeight) &&
            HasMatchingAspectRatio(sourceWidth, sourceHeight, scene.OutputWidth, scene.OutputHeight);
    }

    public static string BuildFilter(
        int sourceWidth,
        int sourceHeight,
        CinematicSingleVideoScene scene)
    {
        ValidateDimensions(sourceWidth, sourceHeight, scene);

        ProjectedQuad outputQuad = NormalizeOutputQuad(scene);
        (int outputWidth, int outputHeight) = CalculateOutputSize(sourceWidth, sourceHeight, scene);
        if (outputWidth == sourceWidth && outputHeight == sourceHeight &&
            IsFullFramebufferQuad(outputQuad, scene.OutputWidth, scene.OutputHeight))
        {
            return "setsar=1";
        }

        string scale = FormattableString.Invariant(
            $"scale={outputWidth}:{outputHeight}:flags=bilinear");
        if (IsFullFramebufferQuad(outputQuad, scene.OutputWidth, scene.OutputHeight))
            return $"{scale},setsar=1";

        // UbiArt's Pleo texture component builds a 2x2 quad with UVs spanning 0..1.
        // The graph transforms that quad and the framebuffer clips it; it does not
        // perform an implicit aspect-ratio cover crop. Scaling first expresses the
        // source as normalized texture space, and perspective then applies the four
        // authored projected corners while retaining a fixed-size output canvas.
        string perspective = string.Join(
            ':',
            $"perspective=x0={FormatCoordinate(outputQuad.TopLeft.X)}",
            $"y0={FormatCoordinate(outputQuad.TopLeft.Y)}",
            $"x1={FormatCoordinate(outputQuad.TopRight.X)}",
            $"y1={FormatCoordinate(outputQuad.TopRight.Y)}",
            $"x2={FormatCoordinate(outputQuad.BottomLeft.X)}",
            $"y2={FormatCoordinate(outputQuad.BottomLeft.Y)}",
            $"x3={FormatCoordinate(outputQuad.BottomRight.X)}",
            $"y3={FormatCoordinate(outputQuad.BottomRight.Y)}",
            "sense=destination",
            "interpolation=linear");
        return $"{scale},{perspective},setsar=1";
    }

    public static (int Width, int Height) CalculateOutputSize(
        int sourceWidth,
        int sourceHeight,
        CinematicSingleVideoScene scene)
    {
        ValidateDimensions(sourceWidth, sourceHeight, scene);

        ProjectedQuad outputQuad = NormalizeOutputQuad(scene);
        if (!IsFullFramebufferQuad(outputQuad, scene.OutputWidth, scene.OutputHeight))
            return (scene.OutputWidth, scene.OutputHeight);

        if (HasMatchingAspectRatio(sourceWidth, sourceHeight, scene.OutputWidth, scene.OutputHeight))
            return (sourceWidth, sourceHeight);

        double outputAspect = scene.OutputWidth / (double)scene.OutputHeight;
        return (RoundToNearestEven(sourceHeight * outputAspect), sourceHeight);
    }

    private static ProjectedQuad NormalizeOutputQuad(CinematicSingleVideoScene scene) =>
        new(
            SnapToFrameEdge(scene.OutputQuad.TopLeft, 0, 0),
            SnapToFrameEdge(scene.OutputQuad.TopRight, scene.OutputWidth, 0),
            SnapToFrameEdge(scene.OutputQuad.BottomRight, scene.OutputWidth, scene.OutputHeight),
            SnapToFrameEdge(scene.OutputQuad.BottomLeft, 0, scene.OutputHeight),
            scene.OutputQuad.Bounds);

    private static Vector2 SnapToFrameEdge(Vector2 value, float expectedX, float expectedY) =>
        new(
            Math.Abs(value.X - expectedX) <= FrameEdgeTolerance ? expectedX : value.X,
            Math.Abs(value.Y - expectedY) <= FrameEdgeTolerance ? expectedY : value.Y);

    private static bool IsFullFramebufferQuad(ProjectedQuad quad, int outputWidth, int outputHeight) =>
        quad.TopLeft == Vector2.Zero &&
        quad.TopRight == new Vector2(outputWidth, 0) &&
        quad.BottomRight == new Vector2(outputWidth, outputHeight) &&
        quad.BottomLeft == new Vector2(0, outputHeight);

    private static bool HasMatchingAspectRatio(
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight)
    {
        const double aspectTolerance = 0.002;
        double sourceAspect = sourceWidth / (double)sourceHeight;
        double outputAspect = outputWidth / (double)outputHeight;
        return Math.Abs(sourceAspect - outputAspect) <= aspectTolerance;
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

    private static string FormatCoordinate(float value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    private static void ValidateDimensions(
        int sourceWidth,
        int sourceHeight,
        CinematicSingleVideoScene scene)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scene.OutputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scene.OutputHeight);
    }

    private static async Task RunFfmpegAsync(string source, string destination, string filter, ILogger logger)
    {
        await JdiFfmpegResolver.GetFfmpegPathAsync();

        IConversion conversion = FFmpeg.Conversions.New();
        conversion.SetOverwriteOutput(true);
        conversion.AddParameter("-hide_banner");
        conversion.AddParameter($"-i \"{source}\"");
        conversion.AddParameter("-an");
        conversion.AddParameter($"-vf \"{filter}\"");
        conversion.AddParameter("-c:v libvpx");
        conversion.AddParameter("-deadline realtime");
        conversion.AddParameter("-cpu-used 8");
        conversion.AddParameter($"-threads {Math.Max(1, Environment.ProcessorCount).ToString(CultureInfo.InvariantCulture)}");
        conversion.AddParameter("-lag-in-frames 0");
        conversion.AddParameter("-auto-alt-ref 0");
        conversion.AddParameter("-crf 10");
        conversion.AddParameter("-b:v 12M");
        conversion.AddParameter("-maxrate 18M");
        conversion.AddParameter("-bufsize 24M");
        conversion.AddParameter("-pix_fmt yuv420p");
        conversion.SetOutput(destination);
        conversion.OnDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
                logger.LogTrace("ffmpeg: {Output}", args.Data);
        };

        await conversion.Start();
    }
}
