namespace JustDanceEditor.Helpers;

public class SongData
{
    public Imageurls ImageURLs { get; set; }
    public Previewurls PreviewURLs { get; set; }
    public Contenturls ContentURLs { get; set; }
}

public class Imageurls
{
    public string coachesSmall { get; set; }
    public string coachesLarge { get; set; }
    public string Cover { get; set; }
    public string Cover1024 { get; set; }
    public string ConverSmall { get; set; }
    public string SongTitleLogo { get; set; }
}

public class Previewurls
{
    public string audioPreview { get; set; }
    public string HIGHvp8 { get; set; }
    public string HIGHvp9 { get; set; }
    public string LOWvp8 { get; set; }
    public string LOWvp9 { get; set; }
    public string MIDvp8 { get; set; }
    public string MIDvp9 { get; set; }
    public string ULTRAvp8 { get; set; }
    public string ULTRAvp9 { get; set; }
}

public class Contenturls
{
    public string UltraHD { get; set; }
    public string Ultravp9 { get; set; }
    public string HighHD { get; set; }
    public string Highvp9 { get; set; }
    public string MidHD { get; set; }
    public string Midvp9 { get; set; }
    public string LowHD { get; set; }
    public string Lowvp9 { get; set; }
    public string Audio { get; set; }
    public string mapPackage { get; set; }
}

