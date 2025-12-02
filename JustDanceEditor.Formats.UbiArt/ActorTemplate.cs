namespace JustDanceEditor.Formats.UbiArt;

public class ActorTemplate
{
    public string __class { get; set; } = string.Empty;
    public int WIP { get; set; }
    public int LOWUPDATE { get; set; }
    public int UPDATE_LAYER { get; set; }
    public int PROCEDURAL { get; set; }
    public int STARTPAUSED { get; set; }
    public int FORCEISENVIRONMENT { get; set; }
    public COMPONENT[] COMPONENTS { get; set; } = [];
}

public class COMPONENT
{
    public string __class { get; set; } = string.Empty;
    public Tapesrack[] TapesRack { get; set; } = [];
}

public class Tapesrack
{
    public string __class { get; set; } = string.Empty;
    public Entry[] Entries { get; set; } = [];
}

public class Entry
{
    public string __class { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
