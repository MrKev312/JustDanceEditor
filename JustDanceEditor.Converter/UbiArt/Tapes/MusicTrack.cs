﻿namespace JustDanceEditor.Converter.UbiArt.Tapes;

public class MusicTrack
{
    public string __class { get; set; } = "";
    public int WIP { get; set; }
    public int LOWUPDATE { get; set; }
    public int UPDATE_LAYER { get; set; }
    public int PROCEDURAL { get; set; }
    public int STARTPAUSED { get; set; }
    public int FORCEISENVIRONMENT { get; set; }
    public TrackDataHolder[] COMPONENTS { get; set; } = [];
}

public class TrackDataHolder
{
    public string __class { get; set; } = "";
    public Trackdata trackData { get; set; } = new();
}

public class Trackdata
{
    public string __class { get; set; } = "";
    public Structure structure { get; set; } = new();
    public string path { get; set; } = "";
    public string url { get; set; } = "";
}

public class Structure
{
    public string __class { get; set; } = "";
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
    public string __class { get; set; }  = "";
    public float marker { get; set; }
    public int beats { get; set; }
}

public class Section
{
    public string __class { get; set; } = "";
    public float marker { get; set; }
    public int sectionType { get; set; }
    public string comment { get; set; } = "";
}
