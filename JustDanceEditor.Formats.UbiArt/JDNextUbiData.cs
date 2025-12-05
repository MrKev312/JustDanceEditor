namespace JustDanceEditor.Formats.UbiArt;

public class JDNextUbiMapData
{
    public string artist { get; set; } = string.Empty;
    public Assetsmetadata assetsMetadata { get; set; } = new();
    public uint coachCount { get; set; }
    public uint[] coachNamesLocIds { get; set; } = [];
    public string credits { get; set; } = string.Empty;
    public int danceVersionLocId { get; set; }
    public uint difficulty { get; set; }
    public bool hasCameraScoring { get; set; }
    public bool hasSongTitleInCover { get; set; }
    public string lyricsColor { get; set; } = string.Empty;
    public float mapLength { get; set; }
    public string mapName { get; set; } = string.Empty;
    public uint originalJDVersion { get; set; }
    public string parentMapName { get; set; } = string.Empty;
    public uint sweatDifficulty { get; set; }
    public string[] tagIds { get; set; } = [];
    public string[] tags { get; set; } = [];
    public string title { get; set; } = string.Empty;
    public string[] searchTagsLocIds { get; set; } = [];
    public Assets assets { get; set; } = new();
}

public class Assetsmetadata
{
    public string audioPreviewTrk { get; set; } = string.Empty;
    public string videoPreviewMpd { get; set; } = string.Empty;
}

public class Assets
{
    public string audioPreviewopus { get; set; } = string.Empty;
    public string videoPreview_HIGHvp8webm { get; set; } = string.Empty;
    public string videoPreview_HIGHvp9webm { get; set; } = string.Empty;
    public string videoPreview_LOWvp8webm { get; set; } = string.Empty;
    public string videoPreview_LOWvp9webm { get; set; } = string.Empty;
    public string videoPreview_MIDvp8webm { get; set; } = string.Empty;
    public string videoPreview_MIDvp9webm { get; set; } = string.Empty;
    public string videoPreview_ULTRAvp8webm { get; set; } = string.Empty;
    public string videoPreview_ULTRAvp9webm { get; set; } = string.Empty;
    public string coachesLarge { get; set; } = string.Empty;
    public string coachesSmall { get; set; } = string.Empty;
    public string cover { get; set; } = string.Empty;
    public string cover1024 { get; set; } = string.Empty;
    public string coverSmall { get; set; } = string.Empty;
    public string songTitleLogo { get; set; } = string.Empty;
}