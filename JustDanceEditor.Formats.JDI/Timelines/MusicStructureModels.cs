namespace JustDanceEditor.Formats.JDI.Timelines;

/// <summary>
/// Represents the raw rhythmic structure metadata extracted from music tracks.
/// </summary>
public class Structure
{
    public string __class { get; set; } = string.Empty;
    public int[] markers { get; set; } = [];
    public Signature[] signatures { get; set; } = [];
    public Section[] sections { get; set; } = [];
    public int startBeat { get; set; }
    public int endBeat { get; set; }
    public int fadeStartBeat { get; set; }
    public bool useFadeStartBeat { get; set; }
    public int fadeEndBeat { get; set; }
    public bool useFadeEndBeat { get; set; }
    public float videoStartTime { get; set; }
    public int previewEntry { get; set; }
    public int previewLoopStart { get; set; }
    public int previewLoopEnd { get; set; }
    public float volume { get; set; }
    public float fadeInDuration { get; set; }
    public float fadeInType { get; set; }
    public float fadeOutDuration { get; set; }
    public int fadeOutType { get; set; }
}

public class Signature
{
    public string __class { get; set; } = string.Empty;
    public float marker { get; set; }
    public int beats { get; set; }
}

public class Section
{
    public string __class { get; set; } = string.Empty;
    public float marker { get; set; }
    public int sectionType { get; set; }
    public string comment { get; set; } = string.Empty;
}
