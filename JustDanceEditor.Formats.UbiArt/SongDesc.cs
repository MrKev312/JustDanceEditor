namespace JustDanceEditor.Formats.UbiArt;

public class SongDesc
{
    public string __class { get; set; } = string.Empty;
    public int WIP { get; set; }
    public int LOWUPDATE { get; set; }
    public int UPDATE_LAYER { get; set; }
    public int PROCEDURAL { get; set; }
    public int STARTPAUSED { get; set; }
    public int FORCEISENVIRONMENT { get; set; }
    public InfoComponent[] COMPONENTS { get; set; } = [];
}

public class InfoComponent
{
    public string __class { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public uint JDVersion { get; set; }
    public uint OriginalJDVersion { get; set; }
    public string Artist { get; set; } = string.Empty;
    public string DancerName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Credits { get; set; } = string.Empty;
    public Phoneimages PhoneImages { get; set; } = new();
    public int NumCoach { get; set; }
    public int MainCoach { get; set; }
    public uint Difficulty { get; set; }
    public uint SweatDifficulty { get; set; }
    public int backgroundType { get; set; }
    public int LyricsType { get; set; }
    public string[] Tags { get; set; } = [];
    public float Status { get; set; }
    public long LocaleID { get; set; }
    public int MojoValue { get; set; }
    public int CountInProgression { get; set; }
    public Defaultcolors DefaultColors { get; set; } = new();
    public string VideoPreviewPath { get; set; } = string.Empty;
}

public class Phoneimages
{
    public string cover { get; set; } = string.Empty;
    public string coach1 { get; set; } = string.Empty;
    public string coach2 { get; set; } = string.Empty;
    public string coach3 { get; set; } = string.Empty;
    public string coach4 { get; set; } = string.Empty;
}

public class Defaultcolors
{
    public float[] songcolor_2a { get; set; } = [];
    public float[] lyrics { get; set; } = [];
    public int[] theme { get; set; } = [];
    public float[] songcolor_1a { get; set; } = [];
    public float[] songcolor_2b { get; set; } = [];
    public float[] songcolor_1b { get; set; } = [];
}