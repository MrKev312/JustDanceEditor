namespace JustDanceEditor.Formats.JDI.Metadata;

public class IntermediateMetadata
{
    public Guid SongID { get; set; }
    public string MapName { get; set; } = string.Empty;
    public string ParentMapName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Credits { get; set; } = string.Empty;
    public string LyricsColor { get; set; } = "#FFFFFFFF";
    public double MapLengthSeconds { get; set; }
    public uint OriginalJDVersion { get; set; }
    public int CoachCount { get; set; }
    public JdiLocId[]? CoachNamesLocIds { get; set; }
    public JdiLocId DanceVersionLocId { get; set; } = JdiLocId.Zero;
    public uint Difficulty { get; set; }
    public uint SweatDifficulty { get; set; }
    public List<string> Tags { get; set; } = [];
    public float Status { get; set; }
    public int MojoValue { get; set; }
    public int CountInProgression { get; set; }
    public Dictionary<string, string> AdditionalMetadata { get; set; } = [];

    public void Validate()
    {
        if (CoachCount < 0)
            throw new InvalidOperationException("CoachCount cannot be negative.");

        if (CoachNamesLocIds == null)
            return;

        if (CoachNamesLocIds.Length != CoachCount)
            throw new InvalidOperationException("CoachNamesLocIds must contain an entry for every coach or be null.");

        if (CoachNamesLocIds.Any(locId => string.IsNullOrWhiteSpace(locId.Value)))
            throw new InvalidOperationException("CoachNamesLocIds cannot contain blank values.");
    }
}