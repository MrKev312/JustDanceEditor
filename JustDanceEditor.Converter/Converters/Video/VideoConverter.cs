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
                Convert(convert);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to convert video file: {e.Message}");
            }
        }
    }

    private static void Convert(ConvertUbiArtToUnity convert)
    {
        Console.WriteLine("Converting video file...");
        Stopwatch stopwatch = Stopwatch.StartNew();

        string[] videofiles = Directory.GetFiles(Path.Combine(convert.WorldFolder, "videoscoach"), "*.webm");

        if (videofiles.Length == 0)
        {
            throw new Exception("No video file found");
        }

        string videoFile = videofiles[0];

        // Convert the file
        ConvertVideoFile(convert, videoFile);
    }

    static void ConvertVideoFile(ConvertUbiArtToUnity convert, string input)
    {
        IConversion conversion = FFmpeg.Conversions.New()
            .UseMultiThread(true);

        string logFile = Path.Combine(convert.TempVideoFolder, "vp9_passlog");

        IMediaInfo mediaInfo = FFmpeg.GetMediaInfo(input).Result;

        // Check the aspect ratio
        float aspectRatio = mediaInfo.VideoStreams.First().Width / (float)mediaInfo.VideoStreams.First().Height;

        // If it's not 16:9, we'll add a crop filter
        if (aspectRatio != 16 / 9)
        {
            string cropFilter = aspectRatio < 16 / 9
                ? "crop=in_w:in_w*9/16"
                : "crop=in_h*16/9:in_h";

            conversion.AddParameter($"-vf {cropFilter}");
        }

        // Set it to vp9 webm
        IStream videoStream = mediaInfo.VideoStreams.FirstOrDefault()
            ?.SetCodec(VideoCodec.vp9)!;

        conversion.AddStream(videoStream);

        // Set output to webm
        conversion.SetOutputFormat(Format.webm)
            .SetOutput(Path.Combine(convert.TempVideoFolder, "output.webm"));

        conversion.OnProgress += (sender, args) => Console.WriteLine($"Video progress: {args.Duration}/{args.TotalLength}");

        // Start the conversion
        conversion.Start();
    }
}
