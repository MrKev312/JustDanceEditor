namespace JustDanceEditor.Formats.UbiArt.Model;

/// <summary>
/// Implements UbiArt's MusicTrackStructure beat/sample conversions.
/// </summary>
internal static class MusicTrackTiming
{
    internal const int SampleRate = 48000;

    internal static double GetSecondsAtBeat(IReadOnlyList<int> markers, double beat)
    {
        if (markers.Count < 2)
            throw new NotSupportedException("At least two markers are required for beat to seconds conversion.");

        int previousWholeBeat = (int)Math.Floor(beat);
        int firstMarkerPosition = GetBeatSamplePosition(markers, previousWholeBeat);
        int secondMarkerPosition = GetBeatSamplePosition(markers, previousWholeBeat + 1);
        double beatFractionalPart = beat - previousWholeBeat;
        double sampleOffset = firstMarkerPosition + (beatFractionalPart * (secondMarkerPosition - firstMarkerPosition));
        return sampleOffset / SampleRate;
    }

    internal static int GetBeatSamplePosition(IReadOnlyList<int> markers, int beat)
    {
        if (markers.Count < 2)
            throw new NotSupportedException("At least two markers are required for beat to sample conversion.");

        if (beat < 0)
        {
            int averageBeatLength = ComputeAverageMarkerSpacing(markers, 0, Math.Min(4, markers.Count - 1));
            return beat * averageBeatLength;
        }

        if (beat >= markers.Count)
        {
            int endMarker = markers.Count - 1;
            int startMarker = Math.Max(0, endMarker - 4);
            int averageBeatLength = ComputeAverageMarkerSpacing(markers, startMarker, endMarker);
            return markers[endMarker] + ((beat - markers.Count + 1) * averageBeatLength);
        }

        return markers[beat];
    }

    private static int ComputeAverageMarkerSpacing(IReadOnlyList<int> markers, int startMarker, int endMarker) =>
        (markers[endMarker] - markers[startMarker]) / (endMarker - startMarker);
}
