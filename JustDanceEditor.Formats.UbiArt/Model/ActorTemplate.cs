using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Model;

public class ActorTemplate
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
    public Component[] Components { get; set; } = [];
}

public class Component
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;
    public TapesRack[] TapesRack { get; set; } = [];
}

public class TapesRack
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;
    public Entry[] Entries { get; set; } = [];
}

public class Entry
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}