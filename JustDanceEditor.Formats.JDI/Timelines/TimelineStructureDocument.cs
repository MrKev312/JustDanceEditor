namespace JustDanceEditor.Formats.JDI.Timelines;

public class TimelineStructureDocument
{
    /// <summary>
    /// Source-wave sample positions indexed by musical beat. Markers[i] is beat i;
    /// negative StartBeat values are extrapolated before marker zero.
    /// </summary>
    public List<int> Markers { get; set; } = [];
    public List<SignatureSegment> Signatures { get; set; } = [];
    public List<SectionSegment> Sections { get; set; } = [];
    public int StartBeat { get; set; }
    public int EndBeat { get; set; }
    public double VideoStartOffset { get; set; }
    public int PreviewEntryBeat { get; set; }
    public int PreviewLoopStartBeat { get; set; }
    public int PreviewLoopEndBeat { get; set; }
    public int PreviewDuration { get; set; }

    private const double SampleRate = 48000.0;
    private const double InvSampleRate = 1.0 / SampleRate;

    /// <summary>
    /// Converts a beat to absolute song seconds using markers.
    /// Supports fractional beats via linear interpolation.
    /// </summary>
    public double GetSecondsAtBeat(double beat)
    {
        int count = Markers.Count;
        if (count < 2)
            throw new NotSupportedException("At least two markers are required for beat to seconds conversion.");

        // UbiArt extrapolates out-of-range beats from the average of the first
        // or last four marker intervals, rather than assuming a global BPM.
        if (beat < 0)
        {
            double beatLength = GetAverageMarkerSpacing(0, Math.Min(4, count - 1));
            return (Markers[0] + (beat * beatLength)) * InvSampleRate;
        }

        int i = (int)beat; // Faster than Math.Floor for positive numbers

        if (i >= count - 1)
        {
            // Linear extrapolation for beats after the last marker
            // Formula: M_last + (beat - (count - 1)) * (M_last - M_prev)
            double lastMarker = Markers[count - 1];
            int firstAverageMarker = Math.Max(0, count - 5);
            double beatLength = GetAverageMarkerSpacing(firstAverageMarker, count - 1);
            return (lastMarker + ((beat - (count - 1)) * beatLength)) * InvSampleRate;
        }

        // Interpolate on raw marker values first, then divide once.
        // Formula: M_lower + (M_upper - M_lower) * fraction
        double t = beat - i;
        return (Markers[i] + ((Markers[i + 1] - Markers[i]) * t)) * InvSampleRate;
    }

    /// <summary>
    /// Converts absolute song seconds to a beat index using markers.
    /// </summary>
    public double GetBeatAtSeconds(double seconds)
    {
        int count = Markers.Count;
        if (count == 0)
            return 0;

        // Optimization: Convert seconds to marker scale once to avoid division in loops/comparisons
        double targetSample = seconds * SampleRate;
        double firstSample = Markers[0];

        // Check before start (Extrapolation)
        if (targetSample < firstSample)
        {
            if (count >= 2)
            {
                double beatLength = GetAverageMarkerSpacing(0, Math.Min(4, count - 1));
                return (targetSample - firstSample) / beatLength;
            }
            // Fallback for single marker: scale 0.5s to samples (24000)
            return (targetSample - firstSample) / 24000.0;
        }

        // Check bounds / Extrapolation code here (same as before) ...

        // BUILT-IN BINARY SEARCH
        // If Markers is List<long>, cast targetSample to (long)
        int index = Markers.BinarySearch((int)targetSample);

        // BinarySearch returns a negative number if the exact value isn't found.
        // The bitwise complement (~) gives the index of the next larger item.
        // We want the item *before* that (the floor), so we subtract 1.
        if (index < 0)
        {
            index = ~index - 1;
        }

        // Check after end (Extrapolation)
        if (index >= count - 1)
        {
            double lastSample = Markers[count - 1];
            if (count >= 2)
            {
                int firstAverageMarker = Math.Max(0, count - 5);
                double beatLength = GetAverageMarkerSpacing(firstAverageMarker, count - 1);
                return count - 1 + ((targetSample - lastSample) / beatLength);
            }

            return count - 1 + ((targetSample - lastSample) / 24000.0);
        }

        // Standard Interpolation
        double lowerSample = Markers[index];
        double upperSample = Markers[index + 1];

        // (Target - Lower) / (Upper - Lower)
        // No division by 48000 needed as the ratio is identical
        return index + ((targetSample - lowerSample) / (upperSample - lowerSample));
    }

    /// <summary>
    /// returns (TimeSpan Start, TimeSpan Duration) adjusted for Video timing.
    /// </summary>
    public (TimeSpan start, TimeSpan duration) GetVideoPreviewTiming()
    {
        double audioStartSeconds = GetPreviewAudioStartSeconds();
        double videoOffset = -VideoStartOffset;
        double videoStartSeconds = audioStartSeconds + GetSongStartOffset() + videoOffset;

        return (TimeSpan.FromSeconds(Math.Max(0, videoStartSeconds)), GetPreviewDuration());
    }

    /// <summary>
    /// returns (TimeSpan Start, TimeSpan Duration) adjusted for Audio timing.
    /// </summary>
    public (TimeSpan start, TimeSpan duration) GetAudioPreviewTiming()
    {
        return (TimeSpan.FromSeconds(GetPreviewAudioStartSeconds()), GetPreviewDuration());
    }

    /// <summary>
    /// Converts the preview loop's displayed beat label into processed-audio seconds.
    /// </summary>
    private double GetPreviewAudioStartSeconds()
    {
        if (Markers.Count < 2)
            return 0;

        return Math.Max(0, GetPlaybackSecondsAtBeat(PreviewLoopStartBeat));
    }

    private TimeSpan GetPreviewDuration() => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Converts a timeline beat into seconds in the materialized JDI master audio.
    /// The master begins at StartBeat, while marker zero remains musical beat zero.
    /// </summary>
    public double GetPlaybackSecondsAtBeat(double beat) =>
        GetSecondsAtBeat(beat) - GetSongStartOffset();

    /// <summary>
    /// Converts seconds in the materialized JDI master audio into a timeline beat.
    /// </summary>
    public double GetBeatAtPlaybackSeconds(double seconds) =>
        GetBeatAtSeconds(seconds + GetSongStartOffset());

    /// <summary>
    /// Gets the source-wave time represented by StartBeat. This is negative when
    /// the timeline begins before the wave and positive when its beginning is trimmed.
    /// </summary>
    public double GetSongStartOffset()
    {
        if (Markers.Count < 2)
            return 0;

        return GetSecondsAtBeat(StartBeat);
    }

    private double GetAverageMarkerSpacing(int startMarker, int endMarker) =>
        (Markers[endMarker] - Markers[startMarker]) / (double)(endMarker - startMarker);
}

public class TempoSegment
{
    public double StartBeat { get; set; }
    public double BeatsPerMinute { get; set; }
}

public class SignatureSegment
{
    public int Beats { get; set; }
    public double Marker { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public class SectionSegment
{
    public SongSectionType SectionType { get; set; }
    public double StartBeat { get; set; }
    public string Comment { get; set; } = string.Empty;
}
