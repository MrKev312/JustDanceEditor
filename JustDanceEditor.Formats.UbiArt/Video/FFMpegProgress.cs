using Xabe.FFmpeg.Events;

namespace JustDanceEditor.Formats.UbiArt.Video;

public sealed class FFMpegProgress(string name)
{
    private (TimeSpan current, TimeSpan finish) previous = (TimeSpan.Zero, TimeSpan.Zero);
    private readonly string progressName = string.IsNullOrWhiteSpace(name) ? "Progress" : $"{name} progress";

    public FFMpegProgress() : this("Progress")
    {
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
        if (previous.current != previous.finish)
            Console.WriteLine($"{progressName}: {previous.finish}/{previous.finish}");
    }
}
