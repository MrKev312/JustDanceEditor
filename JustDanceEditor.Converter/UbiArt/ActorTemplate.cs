namespace JustDanceEditor.Converter.UbiArt;

public class ActorTemplate
{
    public string __class { get; set; } = "";
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
    public string __class { get; set; } = "";
    public Tapesrack[] TapesRack { get; set; } = [];
}

public class Tapesrack
{
    public string __class { get; set; } = "";
    public Entry[] Entries { get; set; } = [];
}

public class Entry
{
    public string __class { get; set; } = "";
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
}
