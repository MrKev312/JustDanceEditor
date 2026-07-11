using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.Globalization;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public sealed record RecordingKindOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record RecordingStatsViewOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record RecordingGraphPoint(double BeatLabel, float Value, string Label);

public sealed record RecordingGraphMarker(double BeatLabel, string Label);

public sealed record RecordingSelectionItem(
    string Path,
    string DisplayName,
    MotionRecordingDocument Recording)
{
    public bool IsSelected { get; set; } = true;
}

public sealed class RecordingListItem(string path, MotionRecordingDocument recording)
{
    public string Path { get; } = path;
    public MotionRecordingDocument Recording { get; } = recording;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string DisplayName => $"Coach {CoachId} - {FileName}";
    public int CoachId => Recording.CoachId;
    public int SampleCount => Recording.Samples.Count;
    public string DurationText => Recording.Duration.TotalSeconds <= 0
        ? "-"
        : Recording.Duration.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);

    public override string ToString() => DisplayName;
}

public sealed record RecordingStatItemViewModel(string Name, string Value);

public sealed class RecordingMoveScoreViewModel(MotionRecordingAnalyzedMove move)
{
    public int MoveIndex { get; } = move.MoveIndex;
    public string MoveId { get; } = move.MoveId;
    public double StartBeat { get; } = move.StartBeatLabel;
    public float Accuracy { get; } = move.PercentageScore;
    public float AddedScore { get; } = move.AddedScore;
    public float TotalScore { get; } = move.TotalScore;
    public string BeatText { get; } = move.StartBeatLabel.ToString("0.##", CultureInfo.InvariantCulture);
    public string AccuracyText { get; } = $"{move.PercentageScore:0.0}%";
    public string AddedScoreText { get; } = MathF.Round(move.AddedScore).ToString("0", CultureInfo.InvariantCulture);
    public string TotalScoreText { get; } = MathF.Round(move.TotalScore).ToString("0", CultureInfo.InvariantCulture);
    public string Feedback { get; } = move.Feedback.ToString();
    public string Issue { get; } = move.Issue ?? string.Empty;
}

public sealed class RecordingMoveAggregateViewModel(MotionRecordingMoveAggregate aggregate)
{
    public string MoveId { get; } = aggregate.MoveId;
    public int Count { get; } = aggregate.Count;
    public float AverageAccuracy { get; } = aggregate.AveragePercentageScore;
    public float WorstAccuracy { get; } = aggregate.WorstPercentageScore;
    public float BestAccuracy { get; } = aggregate.BestPercentageScore;
    public float TotalAddedScore { get; } = aggregate.TotalAddedScore;
    public string AverageAccuracyText { get; } = $"{aggregate.AveragePercentageScore:0.0}%";
    public string WorstAccuracyText { get; } = $"{aggregate.WorstPercentageScore:0.0}%";
    public string BestAccuracyText { get; } = $"{aggregate.BestPercentageScore:0.0}%";
    public string TotalAddedScoreText { get; } = MathF.Round(aggregate.TotalAddedScore).ToString("0", CultureInfo.InvariantCulture);
    public int WorstMoveIndex { get; } = aggregate.WorstMoveIndex;
}

public sealed class RecordingIssueViewModel(string moveId, string message, int? moveIndex = null)
{
    public int? MoveIndex { get; } = moveIndex;
    public int MoveIndexSort { get; } = moveIndex ?? int.MaxValue;
    public string MoveIndexText { get; } = moveIndex.HasValue ? moveIndex.Value.ToString(CultureInfo.InvariantCulture) : "-";
    public string MoveId { get; } = string.IsNullOrWhiteSpace(moveId) ? "-" : moveId;
    public string Message { get; } = message;
}