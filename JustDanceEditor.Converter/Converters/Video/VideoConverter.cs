using JustDanceEditor.Converter.Helpers;
using JustDanceEditor.Converter.UbiArt;

using System.Diagnostics;

using Xabe.FFmpeg;

namespace JustDanceEditor.Converter.Converters.Video;
public static class VideoConverter
{
    public static void ConvertVideo(ConvertUbiArtToUnity convert)
    {
        if (convert.SongData.EngineVersion != JDVersion.JDUnlimited)
        {
            try
            {
                string[] videofiles = Directory.GetFiles(Path.Combine(convert.WorldFolder, "videoscoach"), "*.webm");

                if (videofiles.Length == 0)
                {
                    throw new Exception("No video file found");
                }

                string videoFile = videofiles[0];

                IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(videoFile).Result;

                // If codec is vp8 or vp9 AND aspect ratio is 16:9, we don't need to convert
                bool needsConversion = !(mediaInfo.VideoStreams.First().Codec is "vp8" or "vp9"
                    && mediaInfo.VideoStreams.First().Width / (float)mediaInfo.VideoStreams.First().Height == 16f / 9f);

                if (needsConversion)
                    Convert(convert, videoFile);
                else
                {
                    Console.WriteLine("Video file is already in the correct format");
                    File.Copy(videoFile, Path.Combine(convert.TempVideoFolder, "output.webm"), true);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to convert video file: {e.Message}");
            }
        }
    }

    private static void Convert(ConvertUbiArtToUnity convert, string path)
    {
        Console.WriteLine("Converting video file...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            // Convert the file
            ConvertVideoFile(convert, path);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to convert video file: {e.Message}");
            Console.WriteLine("File will be copied as is");

            // Copy the file as is
            File.Copy(path, Path.Combine(convert.TempVideoFolder, "output.webm"), true);
        }

        stopwatch.Stop();
        Console.WriteLine($"Finished converting video file in {stopwatch.ElapsedMilliseconds}ms");
    }

    static void ConvertVideoFile(ConvertUbiArtToUnity convert, string input)
    {
        IConversion conversion = FFmpeg.Conversions.New()
            .UseMultiThread(true);

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
            string cropFilter = aspectRatio > targetAspectRatio
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
            .SetOverwriteOutput(true)
            .SetOutput(Path.Combine(convert.TempVideoFolder, "output.webm"));

        (TimeSpan current, TimeSpan finish) previous = (TimeSpan.Zero, TimeSpan.Zero);
        conversion.OnProgress += (sender, args) =>
        {
            (TimeSpan, TimeSpan) current = (args.Duration, args.TotalLength);
            
            if (previous != current)
                Console.WriteLine($"Video progress: {args.Duration}/{args.TotalLength}");

            previous = current;
        };

        // Did we print the final progress?
        if (previous.current != previous.finish)
            Console.WriteLine($"Video progress: {previous.finish}/{previous.finish}");

        // Start the conversion
        var result = conversion.Start().Result;

        Console.WriteLine($"Ran following: {result.Arguments}");
    }
}
