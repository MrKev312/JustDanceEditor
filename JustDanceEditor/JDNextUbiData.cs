namespace JustDanceEditor;

public class JDNextUbiMapData
{
    public string artist { get; set; }
    public Assetsmetadata assetsMetadata { get; set; }
    public uint coachCount { get; set; }
    public uint[] coachNamesLocIds { get; set; }
    public string credits { get; set; }
    public int danceVersionLocId { get; set; }
    public uint difficulty { get; set; }
    public bool hasCameraScoring { get; set; }
    public bool hasSongTitleInCover { get; set; }
    public string lyricsColor { get; set; }
    public float mapLength { get; set; }
    public string mapName { get; set; }
    public uint originalJDVersion { get; set; }
    public string parentMapName { get; set; }
    public uint sweatDifficulty { get; set; }
    public string[] tagIds { get; set; }
    public string[] tags { get; set; }
    public string title { get; set; }
    public object[] searchTagsLocIds { get; set; }
    public Assets assets { get; set; }
}

public class Assetsmetadata
{
    public string audioPreviewTrk { get; set; }
    public string videoPreviewMpd { get; set; }
}

public class Assets
{
    public string audioPreviewopus { get; set; }
    public string videoPreview_HIGHvp8webm { get; set; }
    public string videoPreview_HIGHvp9webm { get; set; }
    public string videoPreview_LOWvp8webm { get; set; }
    public string videoPreview_LOWvp9webm { get; set; }
    public string videoPreview_MIDvp8webm { get; set; }
    public string videoPreview_MIDvp9webm { get; set; }
    public string videoPreview_ULTRAvp8webm { get; set; }
    public string videoPreview_ULTRAvp9webm { get; set; }
    public string coachesLarge { get; set; }
    public string coachesSmall { get; set; }
    public string cover { get; set; }
    public string cover1024 { get; set; }
    public string coverSmall { get; set; }
    public string songTitleLogo { get; set; }
}

