namespace JustDanceEditor.Formats.JDI.Timelines;

/// <summary>
/// Represents the raw rhythmic structure metadata extracted from music tracks.
/// </summary>
public class Structure
{
    public int startBeat { get; set; }
    public int endBeat { get; set; }
    public float videoStartTime { get; set; }
    public int previewEntry { get; set; }
    public int previewLoopStart { get; set; }
    public int previewLoopEnd { get; set; }
    public int previewDuration { get; set; } = 30; // 30 seconds default
    public Signature[] signatures { get; set; } = [];
    public int[] markers { get; set; } = [];
    public Section[] sections { get; set; } = [];
}

public class Signature
{
    public int beats { get; set; }
    public float marker { get; set; }
    public string comment { get; set; } = string.Empty;
}

public class Section
{
    public float marker { get; set; }
    public int sectionType { get; set; }
    public string comment { get; set; } = string.Empty;
}