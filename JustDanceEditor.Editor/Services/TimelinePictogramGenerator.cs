using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Formats.JDI.Video;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xabe.FFmpeg;

namespace JustDanceEditor.Editor.Services;

public enum PictogramGenerationMode
{
    MoveName,
    VideoFrame
}

public enum PictogramFrameLayoutMode
{
    TransparentBars,
    CropToFill
}

public enum PictogramHorizontalFocus
{
    Left,
    Center,
    Right
}

public sealed record MoveMoment(int StartFrame, int DurationFrames, string MoveId, Color MoveColor)
{
    public double StartBeat => StartFrame / 24d;
}

public sealed record GeneratedPictogramBatch(TrackViewModel? PictogramTrack, IReadOnlyList<PictogramClipViewModel> Clips)
{
    public static GeneratedPictogramBatch Empty { get; } = new(null, Array.Empty<PictogramClipViewModel>());
}

public interface IPictogramImageGenerator
{
    Task GenerateMoveNamePictogramAsync(string outputPath, string moveName, Color backgroundColor, PixelSize targetSize, CancellationToken cancellationToken = default);
    Task GenerateVideoFramePictogramAsync(string videoPath, double timestampSeconds, string outputPath, PixelSize targetSize, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default);
}

public interface ITimelinePictogramGenerator
{
    Task<GeneratedPictogramBatch> GenerateAsync(TimelineEditorViewModel timeline, PictogramGenerationMode mode, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default);
}

public sealed class TimelinePictogramGenerator(IPictogramImageGenerator? imageGenerator = null) : ITimelinePictogramGenerator
{
    private const int PictogramWidth = 512;
    private const int SingleCoachHeight = 512;
    private const int MultiCoachHeight = 354;

    private readonly IPictogramImageGenerator _imageGenerator = imageGenerator ?? new PictogramImageGenerator();

    public static PixelSize GetTargetSize(int coachCount)
    {
        int height = coachCount > 1 ? MultiCoachHeight : SingleCoachHeight;
        return new PixelSize(PictogramWidth, height);
    }

    public static IReadOnlyList<MoveMoment> CollectMoveMoments(IEnumerable<MoveClipViewModel>? moveClips)
    {
        if (moveClips == null)
            return [];

        List<MoveMoment> moments = [.. moveClips
            .Where(c => c != null)
            .Where(c => !string.IsNullOrWhiteSpace(c.MoveId))
            .GroupBy(c => c.RawClip.StartTime)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                MoveClipViewModel chosen = g
                    .OrderByDescending(c => c.DurationBeats)
                    .ThenBy(c => c.MoveId, StringComparer.OrdinalIgnoreCase)
                    .First();

                Color color = chosen.RenderColor;
                int durationFrames = Math.Max(1, (int)Math.Round(chosen.DurationBeats * 24d));
                return new MoveMoment(g.Key, durationFrames, chosen.MoveId, new Color(255, color.R, color.G, color.B));
            })];

        return moments;
    }

    public async Task<GeneratedPictogramBatch> GenerateAsync(TimelineEditorViewModel timeline, PictogramGenerationMode mode, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        TrackViewModel? pictogramTrack = timeline.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Pictogram);
        if (pictogramTrack == null)
            return GeneratedPictogramBatch.Empty;

        List<MoveClipViewModel> moveClips = [.. timeline.Tracks
            .Where(t => t.TrackType is TrackType.CoachHand or TrackType.CoachFullBody)
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()];

        IReadOnlyList<MoveMoment> moments = CollectMoveMoments(moveClips);
        if (moments.Count == 0)
            return new GeneratedPictogramBatch(pictogramTrack, Array.Empty<PictogramClipViewModel>());

        if (mode == PictogramGenerationMode.VideoFrame && (string.IsNullOrWhiteSpace(timeline.VideoPath) || !File.Exists(timeline.VideoPath)))
            return new GeneratedPictogramBatch(pictogramTrack, Array.Empty<PictogramClipViewModel>());

        string pictogramDirectory = Path.Combine(timeline.RootPath, "assets", "pictograms");
        Directory.CreateDirectory(pictogramDirectory);

        PixelSize targetSize = GetTargetSize(timeline.CoachCount);
        HashSet<string> usedIds = new(timeline.AvailablePictograms, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MoveMoment> firstMomentByMove = moments
            .GroupBy(m => m.MoveId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(m => m.StartFrame).First(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> pictoIdByMove = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (MoveMoment seed in firstMomentByMove.Values.OrderBy(m => m.StartFrame))
        {
            string baseId = $"auto_{SanitizeId(seed.MoveId)}";
            string pictoId = EnsureUniqueId(baseId, usedIds);
            usedIds.Add(pictoId);
            pictoIdByMove[seed.MoveId] = pictoId;
        }

        string GetOutputPath(MoveMoment seed)
        {
            if (!pictoIdByMove.TryGetValue(seed.MoveId, out string? pictoId) || string.IsNullOrWhiteSpace(pictoId))
                return string.Empty;

            return Path.Combine(pictogramDirectory, pictoId + ".webp");
        }

        HashSet<string> successfulMoves = [with(StringComparer.OrdinalIgnoreCase)];
        object successfulMovesLock = new();

        void MarkSuccessful(MoveMoment seed)
        {
            lock (successfulMovesLock)
                successfulMoves.Add(seed.MoveId);
        }

        async Task GenerateOneImageAsync(MoveMoment seed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!pictoIdByMove.TryGetValue(seed.MoveId, out string? pictoId) || string.IsNullOrWhiteSpace(pictoId))
                return;

            string outputPath = GetOutputPath(seed);

            if (mode == PictogramGenerationMode.MoveName)
            {
                await _imageGenerator.GenerateMoveNamePictogramAsync(outputPath, seed.MoveId, seed.MoveColor, targetSize, cancellationToken);
            }
            else
            {
                string videoPath = timeline.VideoPath;
                double videoTimeSeconds = GetVideoTimestampSeconds(timeline, seed.StartBeat, seed.DurationFrames);
                await _imageGenerator.GenerateVideoFramePictogramAsync(videoPath, videoTimeSeconds, outputPath, targetSize, frameLayoutMode, horizontalFocus, cancellationToken);
            }

            MarkSuccessful(seed);
        }

        // Generating source images is the expensive part. Run move-level generation in parallel.
        int maxConcurrency = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
        using SemaphoreSlim semaphore = new(maxConcurrency, maxConcurrency);
        Task[] imageTasks = [.. firstMomentByMove.Values.Select(async seed =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await GenerateOneImageAsync(seed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                EditorLog.Fallback(ex, $"Generate pictogram image for move '{seed.MoveId}'");
                string outputPath = GetOutputPath(seed);
                if (!string.IsNullOrWhiteSpace(outputPath) && File.Exists(outputPath))
                    MarkSuccessful(seed);
            }
            finally
            {
                semaphore.Release();
            }
        })];

        await Task.WhenAll(imageTasks);

        List<PictogramClipViewModel> generated = [];
        foreach (MoveMoment moment in moments)
        {
            if (!pictoIdByMove.TryGetValue(moment.MoveId, out string? pictoId) || string.IsNullOrWhiteSpace(pictoId))
                continue;

            if (!successfulMoves.Contains(moment.MoveId))
                continue;

            PictogramClip rawClip = new()
            {
                PictogramId = pictoId,
                StartTime = moment.StartFrame,
                Duration = 24
            };

            generated.Add(new PictogramClipViewModel(rawClip, timeline.RootPath, timeline));
        }

        return new GeneratedPictogramBatch(pictogramTrack, generated);
    }

    internal static string SanitizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "move";

        char[] sanitized = [.. value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_')];

        string result = new string(sanitized).Trim('_');
        while (result.Contains("__", StringComparison.Ordinal))
            result = result.Replace("__", "_", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(result) ? "move" : result;
    }

    private static string EnsureUniqueId(string baseId, ISet<string> usedIds)
    {
        if (!usedIds.Contains(baseId))
            return baseId;

        int suffix = 2;
        while (true)
        {
            string candidate = $"{baseId}_{suffix}";
            if (!usedIds.Contains(candidate))
                return candidate;

            suffix++;
        }
    }

    private static double GetVideoTimestampSeconds(TimelineEditorViewModel timeline, double startBeatLabel, int durationFrames)
    {
        double offsetBeats = Math.Max(1, durationFrames) / 24d / 3d * 2d; // aim for 2/3rds of the way into the move, but never less than 1 beat in
        double beatLabel = startBeatLabel + offsetBeats;
        double playbackSeconds = timeline.GetPlaybackSecondsAtBeatLabel(beatLabel);
        double songStartOffset = timeline.TimelineStructure.GetSongStartOffset();
        double seconds = playbackSeconds + songStartOffset + timeline.VideoOffset;
        return Math.Max(0, seconds);
    }
}

public sealed class PictogramImageGenerator : IPictogramImageGenerator
{
    private const double Padding = 24;
    private static readonly Typeface TextTypeface = new("Inter");

    public async Task GenerateMoveNamePictogramAsync(string outputPath, string moveName, Color backgroundColor, PixelSize targetSize, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        moveName ??= string.Empty;

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempPngPath = Path.Combine(Path.GetTempPath(), $"jdi_move_picto_{Guid.NewGuid():N}.png");
        try
        {
            RenderTargetBitmap bitmap = new(targetSize);
            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(backgroundColor), new Rect(0, 0, targetSize.Width, targetSize.Height));

                double maxTextWidth = Math.Max(1, targetSize.Width - (Padding * 2));
                double maxTextHeight = Math.Max(1, targetSize.Height - (Padding * 2));

                TextLayoutSelection selection = SelectLargestText(moveName, maxTextWidth, maxTextHeight);
                double totalHeight = selection.LineHeight * selection.Lines.Count;
                double y = (targetSize.Height - totalHeight) / 2d;

                foreach (string line in selection.Lines)
                {
                    FormattedText lineText = CreateLineText(line, selection.FontSize);
                    double x = (targetSize.Width - lineText.Width) / 2d;
                    context.DrawText(lineText, new Point(x, y));
                    y += selection.LineHeight;
                }
            }

            await using (FileStream stream = File.Create(tempPngPath))
            {
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                await stream.FlushAsync(cancellationToken);
            }

            await ConvertImageToWebpAsync(tempPngPath, outputPath, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempPngPath);
        }
    }

    public async Task GenerateVideoFramePictogramAsync(string videoPath, double timestampSeconds, string outputPath, PixelSize targetSize, PictogramFrameLayoutMode frameLayoutMode = PictogramFrameLayoutMode.TransparentBars, PictogramHorizontalFocus horizontalFocus = PictogramHorizontalFocus.Center, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string tempFramePath = Path.Combine(Path.GetTempPath(), $"jdi_picto_frame_{Guid.NewGuid():N}.png");
        string tempComposedPath = Path.Combine(Path.GetTempPath(), $"jdi_picto_composed_{Guid.NewGuid():N}.png");
        try
        {
            await ExtractFrameAsync(videoPath, timestampSeconds, tempFramePath, cancellationToken);

            using Bitmap source = new(tempFramePath);
            RenderTargetBitmap target = new(targetSize);

            using (DrawingContext context = target.CreateDrawingContext())
            {
                Size sourceSize = source.Size;
                Rect sourceRect = new(0, 0, sourceSize.Width, sourceSize.Height);
                Rect destinationRect;

                if (frameLayoutMode == PictogramFrameLayoutMode.CropToFill)
                {
                    sourceRect = CalculateCropSourceRect(sourceSize, new Size(targetSize.Width, targetSize.Height), horizontalFocus);
                    destinationRect = new Rect(0, 0, targetSize.Width, targetSize.Height);
                }
                else
                {
                    // Transparent bars mode: keep aspect ratio and leave unused area transparent.
                    destinationRect = CalculateContainRect(sourceSize, new Size(targetSize.Width, targetSize.Height), horizontalFocus);
                }

                context.DrawImage(source, sourceRect, destinationRect);
            }

            await using (FileStream stream = File.Create(tempComposedPath))
            {
                target.Save(stream, PngBitmapEncoderOptions.Default);
                await stream.FlushAsync(cancellationToken);
            }

            await ConvertImageToWebpAsync(tempComposedPath, outputPath, cancellationToken);
        }
        finally
        {
            TryDeleteFile(tempFramePath);
            TryDeleteFile(tempComposedPath);
        }
    }

    private static async Task ExtractFrameAsync(string videoPath, double timestampSeconds, string outputPath, CancellationToken cancellationToken)
    {
        await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);
        IConversion conversion = FFmpeg.Conversions.New();
        string timestamp = Math.Max(0, timestampSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        conversion.AddParameter($"-y -ss {timestamp} -i \"{videoPath}\" -frames:v 1");
        conversion.SetOutput(outputPath);
        conversion.SetOverwriteOutput(true);
        await conversion.Start(cancellationToken);
    }

    private static async Task ConvertImageToWebpAsync(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        await JdiFfmpegResolver.GetFfmpegPathAsync(cancellationToken);
        IConversion conversion = FFmpeg.Conversions.New();
        conversion.AddParameter($"-y -i \"{sourcePath}\"");
        conversion.SetOutput(outputPath);
        conversion.SetOverwriteOutput(true);
        await conversion.Start(cancellationToken);
    }

    private static TextLayoutSelection SelectLargestText(string moveName, double maxWidth, double maxHeight)
    {
        string text = string.IsNullOrWhiteSpace(moveName) ? "MOVE" : moveName;
        double startingSize = Math.Max(24, Math.Min(maxHeight, maxWidth));

        for (double fontSize = startingSize; fontSize >= 12; fontSize -= 1)
        {
            TextLayoutSelection candidate = BuildWrappedLayout(text, fontSize, maxWidth);
            double totalHeight = candidate.LineHeight * candidate.Lines.Count;
            if (totalHeight <= maxHeight + 0.1)
                return candidate;
        }

        return BuildWrappedLayout(text, 12, maxWidth);
    }

    private static TextLayoutSelection BuildWrappedLayout(string text, double fontSize, double maxWidth)
    {
        List<string> wrappedLines = [];

        foreach (string sourceLine in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            foreach (string wrapped in WrapSingleLine(sourceLine, fontSize, maxWidth))
                wrappedLines.Add(wrapped);
        }

        if (wrappedLines.Count == 0)
            wrappedLines.Add("MOVE");

        double lineHeight = CreateLineText("Ag", fontSize).Height;
        return new TextLayoutSelection(fontSize, wrappedLines, lineHeight);
    }

    private static IEnumerable<string> WrapSingleLine(string line, double fontSize, double maxWidth)
    {
        string text = string.IsNullOrWhiteSpace(line) ? " " : line.Trim();
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [" "];

        List<string> result = [];
        string currentLine = string.Empty;

        foreach (string word in words)
        {
            string candidate = string.IsNullOrEmpty(currentLine) ? word : $"{currentLine} {word}";
            double candidateWidth = CreateLineText(candidate, fontSize).Width;

            if (candidateWidth <= maxWidth || string.IsNullOrEmpty(currentLine))
            {
                currentLine = candidate;
            }
            else
            {
                result.Add(currentLine);
                currentLine = word;
            }
        }

        if (!string.IsNullOrEmpty(currentLine))
            result.Add(currentLine);

        List<string> finalized = [];
        foreach (string candidateLine in result)
        {
            if (CreateLineText(candidateLine, fontSize).Width <= maxWidth)
            {
                finalized.Add(candidateLine);
                continue;
            }

            finalized.AddRange(SplitLongToken(candidateLine, fontSize, maxWidth));
        }

        return finalized;
    }

    private static IEnumerable<string> SplitLongToken(string token, double fontSize, double maxWidth)
    {
        List<string> parts = [];
        string current = string.Empty;

        foreach (char ch in token)
        {
            string candidate = current + ch;
            if (CreateLineText(candidate, fontSize).Width <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
            }
            else
            {
                parts.Add(current);
                current = ch.ToString();
            }
        }

        if (!string.IsNullOrEmpty(current))
            parts.Add(current);

        return parts;
    }

    private static FormattedText CreateLineText(string text, double fontSize)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            TextTypeface,
            fontSize,
            Brushes.Black);
    }

    private static Rect CalculateContainRect(Size sourceSize, Size targetSize, PictogramHorizontalFocus horizontalFocus)
    {
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            return new Rect(0, 0, targetSize.Width, targetSize.Height);

        double scale = Math.Min(targetSize.Width / sourceSize.Width, targetSize.Height / sourceSize.Height);
        double width = sourceSize.Width * scale;
        double height = sourceSize.Height * scale;
        double remainingX = Math.Max(0, targetSize.Width - width);
        double x = horizontalFocus switch
        {
            PictogramHorizontalFocus.Left => 0,
            PictogramHorizontalFocus.Right => remainingX,
            _ => remainingX / 2d
        };
        double y = (targetSize.Height - height) / 2d;
        return new Rect(x, y, width, height);
    }

    private static Rect CalculateCropSourceRect(Size sourceSize, Size targetSize, PictogramHorizontalFocus horizontalFocus)
    {
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            return new Rect(0, 0, Math.Max(1, sourceSize.Width), Math.Max(1, sourceSize.Height));

        double sourceAspect = sourceSize.Width / sourceSize.Height;
        double targetAspect = targetSize.Width / targetSize.Height;

        if (sourceAspect > targetAspect)
        {
            double cropWidth = sourceSize.Height * targetAspect;
            double maxX = Math.Max(0, sourceSize.Width - cropWidth);
            double x = horizontalFocus switch
            {
                PictogramHorizontalFocus.Left => 0,
                PictogramHorizontalFocus.Right => maxX,
                _ => maxX / 2d
            };
            return new Rect(x, 0, cropWidth, sourceSize.Height);
        }

        double cropHeight = sourceSize.Width / targetAspect;
        double y = (sourceSize.Height - cropHeight) / 2d;
        return new Rect(0, y, sourceSize.Width, cropHeight);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorLog.Fallback(ex, $"Delete temporary pictogram file '{path}'");
        }
    }

    private sealed record TextLayoutSelection(double FontSize, IReadOnlyList<string> Lines, double LineHeight);
}
