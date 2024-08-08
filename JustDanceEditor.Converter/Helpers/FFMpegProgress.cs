using Xabe.FFmpeg.Events;

namespace JustDanceEditor.Converter.Helpers;

public class FFMpegProgress
{
    (TimeSpan current, TimeSpan finish) previous = (TimeSpan.Zero, TimeSpan.Zero);
    readonly string progressName;

    public FFMpegProgress()
    {
        progressName = "Progress";
    }

    public FFMpegProgress(string name)
    {
        progressName = $"{name} progress";
    }

    public void Update(ConversionProgressEventArgs args)
    {
        (TimeSpan, TimeSpan) current = (args.Duration, args.TotalLength);

        if (previous != current)
            Console.WriteLine($"{progressName}: {args.Duration}/{args.TotalLength}");

        previous = current;
    }

    public void Finish()
    {
        // Did we print the final progress?
        if (previous.current != previous.finish)
            Console.WriteLine($"Video progress: {previous.finish}/{previous.finish}");
    }
}
