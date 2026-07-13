namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed class MotionTrainingSelectionDocument
{
    public int FormatVersion { get; set; } = 1;
    public List<MotionTrainingExclusion> Exclusions { get; set; } = [];

    public bool IsExcluded(Guid recordingId, int coachId, long timelineClipId, string moveId, int moveOccurrence)
        => Exclusions.Any(exclusion =>
            exclusion.RecordingId == recordingId &&
            exclusion.CoachId == coachId &&
            MatchesMove(exclusion, timelineClipId, moveId, moveOccurrence));

    public void SetExcluded(
        Guid recordingId,
        int coachId,
        long timelineClipId,
        string moveId,
        int moveOccurrence,
        bool isExcluded)
    {
        Exclusions.RemoveAll(exclusion =>
            exclusion.RecordingId == recordingId &&
            exclusion.CoachId == coachId &&
            MatchesMove(exclusion, timelineClipId, moveId, moveOccurrence));

        if (!isExcluded)
            return;

        Exclusions.Add(new MotionTrainingExclusion
        {
            RecordingId = recordingId,
            CoachId = coachId,
            TimelineClipId = timelineClipId,
            MoveId = moveId,
            MoveOccurrence = moveOccurrence
        });
    }

    private static bool MatchesMove(
        MotionTrainingExclusion exclusion,
        long timelineClipId,
        string moveId,
        int moveOccurrence)
    {
        if (timelineClipId != 0 && exclusion.TimelineClipId != 0)
            return exclusion.TimelineClipId == timelineClipId;

        return exclusion.MoveOccurrence == moveOccurrence &&
               string.Equals(exclusion.MoveId, moveId, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MotionTrainingExclusion
{
    public Guid RecordingId { get; set; }
    public int CoachId { get; set; }
    public long TimelineClipId { get; set; }
    public string MoveId { get; set; } = string.Empty;
    public int MoveOccurrence { get; set; }
}
