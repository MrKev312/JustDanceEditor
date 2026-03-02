namespace JustDanceEditor.Formats.JDI.Timelines;

public class TimelineStructureDocument
{
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

        // Linear extrapolation for negative beats
        if (beat < 0)
        {
            // (M0 + beat * (M1 - M0)) / 48000
            return (Markers[0] + (beat * (Markers[1] - Markers[0]))) * InvSampleRate;
        }

        int i = (int)beat; // Faster than Math.Floor for positive numbers

        if (i >= count - 1)
        {
            // Linear extrapolation for beats after the last marker
            // Formula: M_last + (beat - (count - 1)) * (M_last - M_prev)
            double lastMarker = Markers[count - 1];
            double prevMarker = Markers[count - 2];
            return (lastMarker + ((beat - (count - 1)) * (lastMarker - prevMarker))) * InvSampleRate;
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
                // (Target - First) / (M1 - M0)
                return (targetSample - firstSample) / (double)(Markers[1] - firstSample);
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
                double prevSample = Markers[count - 2];
                return count - 1 + ((targetSample - lastSample) / (lastSample - prevSample));
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
        // Old logic: startTime - songOffsetVideo
        // songOffsetVideo was simply videoStartTime
        return GetPreviewTimingInternal(VideoStartOffset);
    }

    /// <summary>
    /// returns (TimeSpan Start, TimeSpan Duration) adjusted for Audio timing.
    /// </summary>
    public (TimeSpan start, TimeSpan duration) GetAudioPreviewTiming()
    {
        // Old logic: startTime - songOffsetAudio
        // songOffsetAudio was -GetSongStartTime()
        // Therefore: startTime - (-GetSongStartTime()) => startTime + GetSongStartTime()
        // To keep the subtractive pattern in the helper, we pass negative SongStart.
        return GetPreviewTimingInternal(-GetSongStartOffset());
    }

    /// <summary>
    /// Shared private logic to calculate start and duration based on markers.
    /// Replaces the loop logic from the old GetPreviewStartTime.
    /// </summary>
    private (TimeSpan start, TimeSpan duration) GetPreviewTimingInternal(double offsetSeconds)
    {
        // Safety check to ensure markers exist
        if (Markers.Count == 0 || PreviewLoopStartBeat >= Markers.Count || PreviewLoopEndBeat >= Markers.Count)
        {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }

        // 1. Get Raw Start Time (UbiArt Unit Conversion: Ticks / 48 / 1000)
        double rawStartTime = Markers[PreviewLoopStartBeat] / 48.0 / 1000.0;

        // 2. Apply Offset to start time
        double calculatedStartTime = rawStartTime - offsetSeconds;

        // 3. Hardcoded 30 second duration for now
        return (TimeSpan.FromSeconds(calculatedStartTime), TimeSpan.FromSeconds(30));
    }

    public double GetBeatLabelFromIndex(double index) => index + StartBeat;
    public double GetIndexFromBeatLabel(double beatLabel) => beatLabel - StartBeat;

    /// <summary>
    /// Calculates the song offset in seconds based on StartBeat index.
    /// (abs index into the array, convert to ms then set the sign to the input sign)
    /// </summary>
    public double GetSongStartOffset()
    {
        if (Markers.Count == 0)
            return 0;

        int beatIndex = Math.Abs(StartBeat);
        if (beatIndex >= Markers.Count)
            return 0;

        double timeMs = Markers[beatIndex] / 48.0;

        // "set the sign to the input sign"
        double offsetSeconds = timeMs / 1000.0;
        return StartBeat < 0 ? -offsetSeconds : offsetSeconds;
    }
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