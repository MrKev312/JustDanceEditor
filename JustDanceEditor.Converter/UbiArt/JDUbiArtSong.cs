using JustDanceEditor.Converter.UbiArt.Tapes;

namespace JustDanceEditor.Converter.UbiArt;

public class JDUbiArtSong
{
    public string Name { get => DTape.MapName; set => DTape.MapName = value; }
    public JDVersion EngineVersion = JDVersion.Unknown;
    public JDVersion JDVersion = JDVersion.Unknown;
    public KaraokeTape KTape { get; set; } = new();
    public DanceTape DTape { get; set; } = new();
    public MusicTrack MTrack { get; set; } = new();
    public SongDesc SongDesc { get; set; } = new();
}

public enum JDVersion
{
    Unknown = -1,
    JDUnlimited = 0,
    JD1,
    JD2,
    JD3,
    JD4,
    JD2014,
    JD2015,
    JD2016,
    JD2017,
    JD2018,
    JD2019,
    JD2020,
    JD2021,
    JD2022
}