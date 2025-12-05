namespace JustDanceEditor.Formats.JDI.Timelines;

public class TimelineStructureDocument
{
    public double TimeBaseMsPerBeat { get; set; } = 500;
    public List<TimelineMarker> Markers { get; set; } = [];
    public List<TempoSegment> TempoSegments { get; set; } = [];
    public List<SignatureSegment> Signatures { get; set; } = [];
    public List<SectionSegment> Sections { get; set; } = [];
    public int StartBeat { get; set; }
    public int EndBeat { get; set; }
    public double VideoStartOffset { get; set; }
    public double PreviewEntryBeat { get; set; }
    public double PreviewLoopStartBeat { get; set; }
    public double PreviewLoopEndBeat { get; set; }
    public double PrevewDuration { get; set; }
}

public class TimelineMarker
{
    public int BeatIndex { get; set; }
    public int TimeMs { get; set; }
}

public class TempoSegment
{
    public double StartBeat { get; set; }
    public double BeatsPerMinute { get; set; }
}

public class SignatureSegment
{
    public double StartBeat { get; set; }
    public int Numerator { get; set; }
    public int Denominator { get; set; }
}

public class SectionSegment
{
    public double StartBeat { get; set; }
    public string SectionType { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

