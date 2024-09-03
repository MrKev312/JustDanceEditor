using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Logging;

using System.Diagnostics;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Video;
public static class VideoConverter
{
    public async static Task ConvertVideoAsync(ConvertUbiArtToUnity convert) =>
        await Task.Run(() => ConvertVideo(convert));

    public static void ConvertVideo(ConvertUbiArtToUnity convert)
    {
        try
        {
            string videoFile = GetVideoFile(convert);

            IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(videoFile).Result;

            // If codec is vp8 or vp9 AND aspect ratio is 16:9, we don't need to convert
            bool needsConversion = !(mediaInfo.VideoStreams.First().Codec is "vp8" or "vp9"
                && mediaInfo.VideoStreams.First().Width / (float)mediaInfo.VideoStreams.First().Height == 16f / 9f);

            if (needsConversion)
                Convert(convert, videoFile);
            else
            {
                Logger.Log("Video file is already in the correct format");
                Directory.CreateDirectory(convert.FileSystem.TempFolders.VideoFolder);
                File.Copy(videoFile, Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "output.webm"), true);
            }
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert video file: {e.Message}", LogLevel.Error);
            return;
        }

        try
        {
            // Now generate the preview video
            GeneratePreviewVideo(convert, Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "output.webm"));

            // Move the video file to the output folder
            string md5 = Download.GetFileMD5(Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "output.webm"));
            string outputVideoPath = convert.FileSystem.OutputFolders.VideoFolder;
            Directory.CreateDirectory(outputVideoPath);
            File.Move(Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "output.webm"), Path.Combine(outputVideoPath, md5), true);

            // Move the preview video to the output folder
            md5 = Download.GetFileMD5(Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "preview.webm"));
            string previewVideoPath = convert.FileSystem.OutputFolders.PreviewVideoFolder;
            Directory.CreateDirectory(previewVideoPath);
            File.Move(Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "preview.webm"), Path.Combine(previewVideoPath, md5));
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to generate preview video: {e.Message}", LogLevel.Error);
        }
    }

    static string GetVideoFile(ConvertUbiArtToUnity convert)
    {
        string[] videofiles = [];
        if (Directory.Exists(convert.FileSystem.InputFolders.MediaFolder))
            videofiles = Directory.GetFiles(convert.FileSystem.InputFolders.MediaFolder, "*.webm");
        if (videofiles.Length > 0)
            return videofiles[0];

        if (Directory.Exists(Path.Combine(convert.FileSystem.InputFolders.MapWorldFolder, "videoscoach")))
            videofiles = Directory.GetFiles(Path.Combine(convert.FileSystem.InputFolders.MapWorldFolder, "videoscoach"), "*.webm");
        if (videofiles.Length > 0)
            return videofiles[0];

        throw new Exception("No video file found");
    }

    static void Convert(ConvertUbiArtToUnity convert, string path)
    {
        Logger.Log("Converting video file...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            // Convert the file
            ConvertVideoFile(convert, path);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to convert video file, copying as is: {e.Message}", LogLevel.Warning);

            // Copy the file as is
            File.Copy(path, Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "output.webm"), true);
        }

        stopwatch.Stop();
        Logger.Log($"Finished converting video file in {stopwatch.ElapsedMilliseconds}ms");
    }

    static void GeneratePreviewVideo(ConvertUbiArtToUnity convert, string path)
    {
        Logger.Log("Generating preview video...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        float startTime = convert.SongData.GetPreviewStartTime(false);

        // Generate the preview video file
        string previewVideoPath = Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "preview.webm");

        GeneratePreviewVideoFFmpeg(path, previewVideoPath, startTime);

        stopwatch.Stop();
        Logger.Log($"Finished generating preview video in {stopwatch.ElapsedMilliseconds}ms");
    }

    static void GeneratePreviewVideoFFmpeg(string path, string previewVideoPath, float startTime)
    {
        IConversion conversion = FFmpeg.Conversions.New();

        IStream stream = FFmpeg.GetMediaInfo(path).Result.VideoStreams.First()
            .SetCodec(VideoCodec.vp9);

        conversion.AddStream(stream)
            .SetOverwriteOutput(true)
            .SetSeek(TimeSpan.FromSeconds(startTime))
            .AddParameter("-b:v 500k -maxrate 600k -bufsize 1200k")
            // Set fade-in of 1 second
            .AddParameter($"-vf \"scale=768:432,fade=t=in:st={startTime}:d=1,fade=t=out:st={startTime + 30 - 1}:d=1\"")
            .AddParameter("-t 30")
            .SetOutput(previewVideoPath)
            .SetOverwriteOutput(true);

        FFMpegProgress progress = new("Video preview");
        TimeSpan totalLength = TimeSpan.FromSeconds(30);
        conversion.OnProgress += (sender, args) => progress.Update(new(args.Duration, totalLength, (int)args.ProcessId));
        progress.Finish();

        IConversionResult result = conversion.Start().Result;

        Logger.Log($"Generated preview video with \"{result.Arguments}\"", LogLevel.Debug);
    }

    static void ConvertVideoFile(ConvertUbiArtToUnity convert, string input)
    {
        IConversion conversion = FFmpeg.Conversions.New();

        IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(input).Result;

        VideoCodec videoCodec = mediaInfo.VideoStreams.First().Codec == "vp8" 
            ? VideoCodec.vp8 // VP8 is fine, don't need to convert
            : VideoCodec.vp9; // Else we'll convert to VP9, warning: this is slowwww

        // Check the aspect ratio
        float aspectRatio = mediaInfo.VideoStreams.First().Width / (float)mediaInfo.VideoStreams.First().Height;

        // If it's not 16:9, we'll add a crop filter
        float targetAspectRatio = 16f / 9f;
        if (aspectRatio != targetAspectRatio)
        {
            string cropFilter = aspectRatio < targetAspectRatio
                ? "crop=in_w:in_w*9/16"
                : "crop=in_h*16/9:in_h";

            conversion.AddParameter($"-vf {cropFilter}");
        }

        // Set it to vp9 webm
        IStream videoStream = mediaInfo.VideoStreams.First()
            .SetCodec(videoCodec)!;

        conversion.AddStream(videoStream);

        // Set output to webm
        conversion.SetOutputFormat(Format.webm)
            .AddParameter("-crf 4")
            .AddParameter("-b:v 4M")
            .SetOverwriteOutput(true)
            .SetOutput(Path.Combine(convert.FileSystem.TempFolders.VideoFolder, "output.webm"));

        FFMpegProgress progress = new("Video");
        conversion.OnProgress += (sender, args) => progress.Update(args);
        progress.Finish();

        // Start the conversion
        IConversionResult result = conversion.Start().Result;

        Logger.Log($"Converted video with \"{result.Arguments}\"", LogLevel.Debug);
    }
}
