namespace JustDanceEditor.Converter.UbiArt;

public class SongDesc
{
    public string __class { get; set; }
    public int WIP { get; set; }
    public int LOWUPDATE { get; set; }
    public int UPDATE_LAYER { get; set; }
    public int PROCEDURAL { get; set; }
    public int STARTPAUSED { get; set; }
    public int FORCEISENVIRONMENT { get; set; }
    public InfoComponent[] COMPONENTS { get; set; }
}

public class InfoComponent
{
    public string __class { get; set; }
    public string MapName { get; set; }
    // The engine version
    public int JDVersion { get; set; }
    // The original game version
    public int OriginalJDVersion { get; set; }
    public string Artist { get; set; }
    public string DancerName { get; set; }
    public string Title { get; set; }
    public string Credits { get; set; }
    public Phoneimages PhoneImages { get; set; }
    public int NumCoach { get; set; }
    public int MainCoach { get; set; }
    public int Difficulty { get; set; }
    public int SweatDifficulty { get; set; }
    public int backgroundType { get; set; }
    public int LyricsType { get; set; }
    public string[] Tags { get; set; } = [];
    public float Status { get; set; }
    public long LocaleID { get; set; }
    public int MojoValue { get; set; }
    public int CountInProgression { get; set; }
    public Defaultcolors DefaultColors { get; set; }
    public string VideoPreviewPath { get; set; }
}

public class Phoneimages
{
    public string cover { get; set; }
    public string coach1 { get; set; }
    public string coach2 { get; set; }
    public string coach3 { get; set; }
    public string coach4 { get; set; }
}

public class Defaultcolors
{
    public float[] songcolor_2a { get; set; }
    public float[] lyrics { get; set; }
    public int[] theme { get; set; }
    public float[] songcolor_1a { get; set; }
    public float[] songcolor_2b { get; set; }
    public float[] songcolor_1b { get; set; }
}
