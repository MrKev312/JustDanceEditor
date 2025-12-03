using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Timelines;

public class TimelineStructureDocument
{
    [JsonPropertyName("timeBaseMsPerBeat")]
    public double TimeBaseMsPerBeat { get; set; } = 500;

    [JsonPropertyName("markers")]
    public List<TimelineMarker> Markers { get; set; } = [];

    [JsonPropertyName("tempoSegments")]
    public List<TempoSegment> TempoSegments { get; set; } = [];

    [JsonPropertyName("signatures")]
    public List<SignatureSegment> Signatures { get; set; } = [];

    [JsonPropertyName("sections")]
    public List<SectionSegment> Sections { get; set; } = [];

    [JsonPropertyName("audioStartOffset")]
    public double AudioStartOffset { get; set; }

    [JsonPropertyName("videoStartOffset")]
    public double VideoStartOffset { get; set; }

    [JsonPropertyName("previewEntryBeat")]
    public double PreviewEntryBeat { get; set; }

    [JsonPropertyName("previewLoopStartBeat")]
    public double PreviewLoopStartBeat { get; set; }

    [JsonPropertyName("previewLoopEndBeat")]
    public double PreviewLoopEndBeat { get; set; }

    [JsonPropertyName("fadeIn")]
    public FadeRegion? FadeIn { get; set; }

    [JsonPropertyName("fadeOut")]
    public FadeRegion? FadeOut { get; set; }
}

public class TimelineMarker
{
    [JsonPropertyName("beatIndex")]
    public int BeatIndex { get; set; }

    [JsonPropertyName("timeMs")]
    public int TimeMs { get; set; }
}

public class TempoSegment
{
    [JsonPropertyName("startBeat")]
    public double StartBeat { get; set; }

    [JsonPropertyName("beatsPerMinute")]
    public double BeatsPerMinute { get; set; }
}

public class SignatureSegment
{
    [JsonPropertyName("startBeat")]
    public double StartBeat { get; set; }

    [JsonPropertyName("numerator")]
    public int Numerator { get; set; }

    [JsonPropertyName("denominator")]
    public int Denominator { get; set; }
}

public class SectionSegment
{
    [JsonPropertyName("startBeat")]
    public double StartBeat { get; set; }

    [JsonPropertyName("sectionType")]
    public string SectionType { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;
}

public class FadeRegion
{
    [JsonPropertyName("startBeat")]
    public double StartBeat { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("curveType")]
    public string CurveType { get; set; } = "linear";
}
