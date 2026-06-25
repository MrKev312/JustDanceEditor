using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.UbiArt.Model;

public class SongDesc
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
    public InfoComponent[] Components { get; set; } = [];
}

public class InfoComponent
{
    [JsonPropertyName("__class")]
    public string Class { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public uint JDVersion { get; set; }
    public uint OriginalJDVersion { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string DancerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Credits { get; set; } = string.Empty;
    public PhoneImages PhoneImages { get; set; } = new();
    public int NumCoach { get; set; }
    public int MainCoach { get; set; }
    public uint Difficulty { get; set; }
    public uint SweatDifficulty { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public uint Energy { get; set; }
    [JsonIgnore]
    public uint EffectiveSweatDifficulty => SweatDifficulty != 0 ? SweatDifficulty : Energy;
    public int BackgroundType { get; set; }
    public int LyricsType { get; set; }
    public string[] Tags { get; set; } = [];
    public float Status { get; set; }
    public long LocaleID { get; set; }
    public int MojoValue { get; set; }
    public int CountInProgression { get; set; }
    public DefaultColors DefaultColors { get; set; } = new();
    public string VideoPreviewPath { get; set; } = string.Empty;
}

public class PhoneImages
{
    public string Cover { get; set; } = string.Empty;
    public string Coach1 { get; set; } = string.Empty;
    public string Coach2 { get; set; } = string.Empty;
    public string Coach3 { get; set; } = string.Empty;
    public string Coach4 { get; set; } = string.Empty;
}

public class DefaultColors
{
    [JsonPropertyName("songcolor_2a")]
    public float[] SongColor2a { get; set; } = [];
    public float[] Lyrics { get; set; } = [];
    public int[] Theme { get; set; } = [];
    [JsonPropertyName("songcolor_1a")]
    public float[] SongColor1a { get; set; } = [];
    [JsonPropertyName("songcolor_2b")]
    public float[] SongColor2b { get; set; } = [];
    [JsonPropertyName("songcolor_1b")]
    public float[] SongColor1b { get; set; } = [];
}