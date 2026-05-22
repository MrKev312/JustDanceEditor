using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal sealed class LegacyCinematicTimeline(
    IReadOnlyList<int> markerSamples,
    int startBeat,
    double videoStartOffsetSeconds)
{
    public static LegacyCinematicTimeline Create(
        IReadOnlyList<int> markerSamples,
        int startBeat,
        double videoStartOffsetSeconds) =>
        new(markerSamples, startBeat, videoStartOffsetSeconds);

    public double GetTapeFrameForOutputFrame(int outputFrame, int renderStartFrame)
    {
        double outputSeconds = outputFrame / (double)LegacyCinematicConstants.OutputFramesPerSecond;
        double songSeconds = outputSeconds + videoStartOffsetSeconds;
        return GetTapeFrameForSongSeconds(songSeconds, renderStartFrame);
    }

    public double GetTapeFrameForSongSeconds(double songSeconds, int renderStartFrame)
    {
        double beat = ConvertSongSecondsToBeat(songSeconds);
        return renderStartFrame + (beat * LegacyCinematicConstants.TapeFramesPerBeat);
    }

    public double GetSongSecondsForTapeFrame(double tapeFrame, int renderStartFrame)
    {
        double beat = (tapeFrame - renderStartFrame) / LegacyCinematicConstants.TapeFramesPerBeat;
        return ConvertBeatToSongSeconds(beat);
    }

    private double ConvertSongSecondsToBeat(double songSeconds)
    {
        if (markerSamples.Count == 0)
            return songSeconds * LegacyCinematicConstants.TapeTicksPerSecond /
                LegacyCinematicConstants.TapeFramesPerBeat;

        double marker0Seconds = MarkerToSeconds(markerSamples[0]);
        if (startBeat < 0 && videoStartOffsetSeconds < marker0Seconds && songSeconds <= marker0Seconds)
        {
            return Interpolate(
                videoStartOffsetSeconds,
                startBeat,
                marker0Seconds,
                0,
                songSeconds);
        }

        if (songSeconds <= marker0Seconds)
        {
            return markerSamples.Count >= 2
                ? Interpolate(MarkerToSeconds(markerSamples[0]), 0, MarkerToSeconds(markerSamples[1]), 1, songSeconds)
                : 0;
        }

        for (int index = 1; index < markerSamples.Count; index++)
        {
            double previousSeconds = MarkerToSeconds(markerSamples[index - 1]);
            double currentSeconds = MarkerToSeconds(markerSamples[index]);
            if (songSeconds <= currentSeconds)
                return Interpolate(previousSeconds, index - 1, currentSeconds, index, songSeconds);
        }

        if (markerSamples.Count >= 2)
        {
            int last = markerSamples.Count - 1;
            return Interpolate(
                MarkerToSeconds(markerSamples[last - 1]),
                last - 1,
                MarkerToSeconds(markerSamples[last]),
                last,
                songSeconds);
        }

        return markerSamples.Count - 1;
    }

    private double ConvertBeatToSongSeconds(double beat)
    {
        if (markerSamples.Count == 0)
            return beat * LegacyCinematicConstants.TapeFramesPerBeat /
                LegacyCinematicConstants.TapeTicksPerSecond;

        double marker0Seconds = MarkerToSeconds(markerSamples[0]);
        if (startBeat < 0 && videoStartOffsetSeconds < marker0Seconds && beat <= 0)
        {
            return Interpolate(
                startBeat,
                videoStartOffsetSeconds,
                0,
                marker0Seconds,
                beat);
        }

        if (beat <= 0)
        {
            return markerSamples.Count >= 2
                ? Interpolate(0, MarkerToSeconds(markerSamples[0]), 1, MarkerToSeconds(markerSamples[1]), beat)
                : marker0Seconds;
        }

        for (int index = 1; index < markerSamples.Count; index++)
        {
            if (beat <= index)
            {
                return Interpolate(
                    index - 1,
                    MarkerToSeconds(markerSamples[index - 1]),
                    index,
                    MarkerToSeconds(markerSamples[index]),
                    beat);
            }
        }

        if (markerSamples.Count >= 2)
        {
            int last = markerSamples.Count - 1;
            return Interpolate(
                last - 1,
                MarkerToSeconds(markerSamples[last - 1]),
                last,
                MarkerToSeconds(markerSamples[last]),
                beat);
        }

        return marker0Seconds;
    }

    private static double MarkerToSeconds(int markerSamples) =>
        markerSamples / LegacyCinematicConstants.MarkerSamplesPerSecond;

    private static double Interpolate(
        double leftSeconds,
        double leftBeat,
        double rightSeconds,
        double rightBeat,
        double seconds)
    {
        double span = rightSeconds - leftSeconds;
        if (Math.Abs(span) <= 0.000001)
            return rightBeat;

        double t = (seconds - leftSeconds) / span;
        return leftBeat + ((rightBeat - leftBeat) * t);
    }
}