namespace JustDanceEditor.Formats.JDI.Timelines;

public class TimelineStructureDocument
{
    public double TimeBaseMsPerBeat { get; set; } = 500;
    /// <summary>
    /// This is a list of beats in milliseconds where markers are placed.
    /// </summary>
    /// <remarks>
    /// Converting to beat markers is multiplying by 48
    /// </remarks>
    public List<int> Markers { get; set; } = [];
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