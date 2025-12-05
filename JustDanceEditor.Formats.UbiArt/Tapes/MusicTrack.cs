using JustDanceEditor.Formats.JDI.Timelines;

namespace JustDanceEditor.Formats.UbiArt.Tapes;

public class MusicTrack
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

public class TrackDataHolder
{
    public string __class { get; set; } = string.Empty;
    public Trackdata trackData { get; set; } = new();
}

public class Trackdata
{
    public string __class { get; set; } = string.Empty;
    public Structure structure { get; set; } = new();
    public string path { get; set; } = string.Empty;
    public string url { get; set; } = string.Empty;
}