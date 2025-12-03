using JustDanceEditor.Formats.JDI.Utilities;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Events;

namespace JustDanceEditor.Formats.UbiArt.Video;

public sealed record UbiArtVideoConversionRequest(
    JDUbiArtSong SongData,
    string TempVideoFolder,
    string VideoOutputFolder,
    string PreviewOutputFolder,
    string SourceVideoPath,
    bool AppendExtensionToHashedVideo);

public interface IVideoConversionProgress
{
    void Update(ConversionProgressEventArgs args);
    void Finish();
}

public interface IVideoProgressFactory
{
    IVideoConversionProgress Create(string stageName);
}

public sealed class ConsoleVideoProgress(string name) : IVideoConversionProgress
{
    private (TimeSpan current, TimeSpan finish) previous = (TimeSpan.Zero, TimeSpan.Zero);

    public void Update(ConversionProgressEventArgs args)
    {
        (TimeSpan, TimeSpan) current = (args.Duration, args.TotalLength);
        if (previous != current)
            Console.WriteLine($"{name}: {args.Duration}/{args.TotalLength}");
        previous = current;
    }

    public void Finish()
    {
        if (previous.current != previous.finish)
            Console.WriteLine($"{name}: {previous.finish}/{previous.finish}");
    }
}

public sealed class ConsoleVideoProgressFactory : IVideoProgressFactory
{
    public IVideoConversionProgress Create(string stageName) => new ConsoleVideoProgress(stageName);
}

public static class UbiArtVideoConverter
{
    public static Task ConvertVideoAsync(UbiArtVideoConversionRequest request, IVideoProgressFactory? progressFactory = null) =>
        Task.Run(() => ConvertVideo(request, progressFactory));

    public static void ConvertVideo(UbiArtVideoConversionRequest request, IVideoProgressFactory? progressFactory = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SongData);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TempVideoFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VideoOutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PreviewOutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceVideoPath);

        Directory.CreateDirectory(request.TempVideoFolder);

        string outputPath = Path.Combine(request.TempVideoFolder, "output.webm");
        bool needsConversion = NeedsConversion(request.SourceVideoPath);

        IVideoProgressFactory progressFactoryToUse = progressFactory ?? new ConsoleVideoProgressFactory();

        if (needsConversion)
        {
            IVideoConversionProgress progress = progressFactoryToUse.Create("Video");
            ConvertVideoFile(request, outputPath, progress);
        }
        else
        {
            CopySourceVideo(request, outputPath);
        }

        IVideoConversionProgress previewProgress = progressFactoryToUse.Create("Video preview");
        GeneratePreviewVideo(request, outputPath, previewProgress);
        MoveOutputs(request, outputPath);
    }

    private static bool NeedsConversion(string videoPath)
    {
        IMediaInfo info = FFmpeg.GetMediaInfo(videoPath).Result;
        IVideoStream stream = info.VideoStreams.First();

        bool codecOk = stream.Codec is "vp8" or "vp9";
        bool ratioOk = Math.Abs(stream.Width / (float)stream.Height - (16f / 9f)) < 0.01f;
        bool fpsOk = Math.Abs(stream.Framerate - 25) < 0.01f;

        return !(codecOk && ratioOk && fpsOk);
    }

    private static void ConvertVideoFile(UbiArtVideoConversionRequest request, string outputPath, IVideoConversionProgress progress)
    {
        Logger.Log("Converting video file...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            IConversion conversion = BuildConversion(request.SourceVideoPath, outputPath);
            AttachProgress(conversion, progress);
            IConversionResult result = conversion.Start().Result;
            progress.Finish();
            Logger.Log($"Converted video with \"{result.Arguments}\"", LogLevel.Debug);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to convert video, copying as-is: {ex.Message}", LogLevel.Warning);
            File.Copy(request.SourceVideoPath, outputPath, true);
        }

        stopwatch.Stop();
        Logger.Log($"Finished converting video file in {stopwatch.ElapsedMilliseconds}ms");
    }

    private static void CopySourceVideo(UbiArtVideoConversionRequest request, string outputPath)
    {
        Logger.Log("Video already in correct format, copying...");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Copy(request.SourceVideoPath, outputPath, true);
    }

    private static void GeneratePreviewVideo(UbiArtVideoConversionRequest request, string outputPath, IVideoConversionProgress progress)
    {
        Logger.Log("Generating preview video...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        string previewPath = Path.Combine(request.TempVideoFolder, "preview.webm");
        float previewStart = request.SongData.GetPreviewStartTime(false);
        IConversion conversion = BuildPreviewConversion(outputPath, previewPath, previewStart);

        AttachProgress(conversion, progress, TimeSpan.FromSeconds(30));

        IConversionResult result = conversion.Start().Result;
        progress.Finish();
        Logger.Log($"Generated preview video with \"{result.Arguments}\"", LogLevel.Debug);

        stopwatch.Stop();
        Logger.Log($"Finished generating preview video in {stopwatch.ElapsedMilliseconds}ms");
    }

    private static void MoveOutputs(UbiArtVideoConversionRequest request, string outputPath)
    {
        MoveHashed(Path.Combine(request.TempVideoFolder, "output.webm"), request.VideoOutputFolder, request.AppendExtensionToHashedVideo);
        MoveHashed(Path.Combine(request.TempVideoFolder, "preview.webm"), request.PreviewOutputFolder, request.AppendExtensionToHashedVideo);
    }

    private static void MoveHashed(string sourcePath, string destinationFolder, bool appendExtension)
    {
        string hash = FileHashing.GetFileMD5(sourcePath);
        if (appendExtension)
            hash += Path.GetExtension(sourcePath);

        Directory.CreateDirectory(destinationFolder);
        File.Move(sourcePath, Path.Combine(destinationFolder, hash), true);
    }

    private static IConversion BuildConversion(string inputPath, string outputPath)
    {
        IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(inputPath).Result;
        VideoCodec codec = mediaInfo.VideoStreams.First().Codec == "vp8" ? VideoCodec.vp8 : VideoCodec.vp9;
        float aspectRatio = mediaInfo.VideoStreams.First().Width / (float)mediaInfo.VideoStreams.First().Height;
        string? cropFilter = null;
        float targetRatio = 16f / 9f;

        if (Math.Abs(aspectRatio - targetRatio) > 0.001f)
            cropFilter = aspectRatio < targetRatio ? "crop=in_w:in_w*9/16" : "crop=in_h*16/9:in_h";

        IConversion conversion = FFmpeg.Conversions.New();
        IVideoStream stream = mediaInfo.VideoStreams.First().SetCodec(codec)!;
        conversion.AddStream(stream)
            .SetOutputFormat(Format.webm)
            .AddParameter("-crf 4")
            .AddParameter("-b:v 4M")
            .AddParameter("-r 25")
            .SetOverwriteOutput(true)
            .SetOutput(outputPath);

        if (!string.IsNullOrEmpty(cropFilter))
            conversion.AddParameter($"-vf {cropFilter}");

        return conversion;
    }

    private static IConversion BuildPreviewConversion(string inputPath, string previewPath, float startTime)
    {
        IConversion conversion = FFmpeg.Conversions.New();
        IVideoStream stream = FFmpeg.GetMediaInfo(inputPath).Result.VideoStreams.First().SetCodec(VideoCodec.vp9);

        conversion.AddStream(stream)
            .SetOverwriteOutput(true)
            .SetSeek(TimeSpan.FromSeconds(startTime))
            .AddParameter("-b:v 500k -maxrate 600k -bufsize 1200k -r 25")
            .AddParameter($"-vf \"scale=768:432,fade=t=in:st={startTime}:d=1,fade=t=out:st={startTime + 30 - 1}:d=1\"")
            .AddParameter("-t 30")
            .SetOutput(previewPath)
            .SetOverwriteOutput(true);

        return conversion;
    }

    private static void AttachProgress(IConversion conversion, IVideoConversionProgress progress, TimeSpan? fixedLength = null)
    {
        conversion.OnProgress += (_, args) =>
        {
            ConversionProgressEventArgs eventArgs = fixedLength.HasValue
                ? new ConversionProgressEventArgs(args.Duration, fixedLength.Value, (int)args.ProcessId)
                : args;
            progress.Update(eventArgs);
        };
    }
}
