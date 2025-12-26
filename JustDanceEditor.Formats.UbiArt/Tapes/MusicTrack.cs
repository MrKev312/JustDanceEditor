namespace JustDanceEditor.Formats.UbiArt.Tapes;

public sealed record MusicTrack
{
    public string __class { get; set; } = string.Empty;
    public int WIP { get; set; }
    public int LOWUPDATE { get; set; }
    public int UPDATE_LAYER { get; set; }
    public int PROCEDURAL { get; set; }
    public int STARTPAUSED { get; set; }
    public int FORCEISENVIRONMENT { get; set; }
    public TrackDataHolder[] COMPONENTS { get; set; } = [];
}

public sealed record TrackDataHolder
{
    public string __class { get; set; } = string.Empty;
    public Trackdata trackData { get; set; } = new();
}

public sealed record Trackdata
{
    public string __class { get; set; } = string.Empty;
    public Structure structure { get; set; } = new();
    public string path { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
}

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
