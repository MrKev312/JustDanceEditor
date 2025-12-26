namespace JustDanceEditor.Formats.JDI.Timelines;

public class TimelineStructureDocument
{
    public double TimeBaseMsPerBeat { get; set; } = 500;
    public List<int> Markers { get; set; } = [];
    public List<SignatureSegment> Signatures { get; set; } = [];
    public List<SectionSegment> Sections { get; set; } = [];
    public int StartBeat { get; set; }
    public int EndBeat { get; set; }
    public double VideoStartOffset { get; set; }
    public int PreviewEntryBeat { get; set; }
    public int PreviewLoopStartBeat { get; set; }
    public int PreviewLoopEndBeat { get; set; }
    public int PrevewDuration { get; set; }

    /// <summary>
    /// Calculates the average Milliseconds per Beat based on the current Markers list.
    /// This was formerly TimelineMath.EstimateMsPerBeat().
    /// </summary>
    public double EstimateMsPerBeat()
    {
        if (Markers.Count < 2)
            return 500;

        double total = 0;
        // Iterate through markers to calculate the average duration
        for (int i = 1; i < Markers.Count; i++)
        {
            total += (Markers[i] - Markers[i - 1]) / 48d;
        }

        return total / (Markers.Count - 1);
    }

    /// <summary>
    /// Estimates BPM based on the current TimeBaseMsPerBeat property.
    /// This was formerly TimelineMath.EstimateBpm().
    /// </summary>
    public double EstimateBpm()
    {
        // Uses the property TimeBaseMsPerBeat which should be set 
        // using EstimateMsPerBeat() during loading.
        double ms = TimeBaseMsPerBeat;
        return ms > 0 ? 60000d / ms : 120d;
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
        return GetPreviewTimingInternal(-GetSongStartTime());
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

    /// <summary>
    /// Reuses logic from Old: GetSongStartTime()
    /// Determines the time of the start beat relative to the timeline.
    /// </summary>
    private double GetSongStartTime()
    {
        if (Markers.Count == 0)
            return 0;

        int beatIndex = Math.Abs(StartBeat);

        // Safety check for index
        if (beatIndex >= Markers.Count)
            return 0;

        double time = Markers[beatIndex] / 48.0 / 1000.0;

        // Set opposite sign if startBeat is positive (Old logic)
        if (StartBeat > 0)
            time = -time;

        return time;
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
    public int SectionType { get; set; }
    public double StartBeat { get; set; }
    public string Comment { get; set; } = string.Empty;
}