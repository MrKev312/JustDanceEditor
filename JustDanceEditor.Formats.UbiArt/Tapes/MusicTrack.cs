using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Tapes;

public sealed record MusicTrack
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;

    [JsonPropertyName("WIP")]
    public int Wip { get; set; }

    [JsonPropertyName("LOWUPDATE")]
    public int LowUpdate { get; set; }

    [JsonPropertyName("UPDATE_LAYER")]
    public int UpdateLayer { get; set; }

    [JsonPropertyName("PROCEDURAL")]
    public int Procedural { get; set; }

    [JsonPropertyName("STARTPAUSED")]
    public int StartPaused { get; set; }

    [JsonPropertyName("FORCEISENVIRONMENT")]
    public int ForceIsEnvironment { get; set; }

    [JsonPropertyName("COMPONENTS")]
    public TrackDataHolder[] Components { get; set; } = [];
}

public sealed record TrackDataHolder
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;
    public TrackData TrackData { get; set; } = new();
}

public sealed record TrackData
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;
    public Structure Structure { get; set; } = new();
    public string Path { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public class Structure
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = "MusicTrackStructure";
    public int StartBeat { get; set; }
    public int EndBeat { get; set; }
    public float VideoStartTime { get; set; }
    public int PreviewEntry { get; set; }
    public int PreviewLoopStart { get; set; }
    public int PreviewLoopEnd { get; set; }
    public int PreviewDuration { get; set; } = 30; // 30 seconds default
    public int FadeStartBeat { get; set; }
    public int FadeEndBeat { get; set; }
    public int FadeInDuration { get; set; }
    public int FadeOutDuration { get; set; }
    public int FadeInType { get; set; }
    public int FadeOutType { get; set; }
    public bool UseFadeStartBeat { get; set; }
    public bool UseFadeEndBeat { get; set; }
    public int Volume { get; set; }
    public Signature[] Signatures { get; set; } = [];
    public int[] Markers { get; set; } = [];
    public Section[] Sections { get; set; } = [];
}

public class Signature
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = "MusicSignature";
    public int Beats { get; set; }
    public double Marker { get; set; }
}

public class Section
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = "MusicSection";
    public float Marker { get; set; }
    public int SectionType { get; set; }
    public string Comment { get; set; } = string.Empty;
}