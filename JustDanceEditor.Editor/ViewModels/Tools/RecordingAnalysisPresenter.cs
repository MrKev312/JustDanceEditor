using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed record RecordingAnalysisPresentation(
    IReadOnlyList<RecordingStatItemViewModel> SummaryItems,
    IReadOnlyList<RecordingMoveScoreViewModel> WorstIndividualMoves,
    IReadOnlyList<RecordingMoveAggregateViewModel> WorstAverageMoves,
    IReadOnlyList<RecordingMoveScoreViewModel> MoveDetails,
    IReadOnlyList<RecordingIssueViewModel> Issues,
    IReadOnlyList<RecordingGraphPoint> ScoreGraphPoints,
    IReadOnlyList<RecordingGraphPoint> AccuracyGraphPoints,
    IReadOnlyList<RecordingGraphMarker> GraphMarkers,
    double GraphMinBeat,
    double GraphMaxBeat,
    string GraphTotalScoreText);

internal static class RecordingAnalysisPresenter
{
    public static RecordingAnalysisPresentation Create(
        TimelineEditorViewModel timeline,
        RecordingListItem selected,
        MotionRecordingAnalysisResult result)
    {
        MotionRecordingAnalyzedMove? worstMove = result.Moves.OrderBy(static move => move.PercentageScore).FirstOrDefault();
        MotionRecordingAnalyzedMove? bestMove = result.Moves.OrderByDescending(static move => move.PercentageScore).FirstOrDefault();

        List<RecordingStatItemViewModel> summary =
        [
            new("Total Score", $"{MathF.Round(result.TotalScore):0} / 13333"),
            new("Average Accuracy", FormatPercent(result.AveragePercentageScore)),
            new("Average Scored Accuracy", FormatPercent(result.AverageScoredPercentageScore)),
            new("Moves", $"{result.ScoredMoveCount} scored / {result.MoveCount} timeline"),
            new("Missing MSMs", result.MissingClassifierCount.ToString(CultureInfo.InvariantCulture)),
            new("Issues", result.IssueCount.ToString(CultureInfo.InvariantCulture)),
            new("Worst Move", worstMove == null ? "-" : $"{worstMove.MoveId} ({FormatPercent(worstMove.PercentageScore)})"),
            new("Best Move", bestMove == null ? "-" : $"{bestMove.MoveId} ({FormatPercent(bestMove.PercentageScore)})"),
            new("Duration", selected.DurationText),
            new("Samples", selected.SampleCount.ToString(CultureInfo.InvariantCulture))
        ];

        List<RecordingIssueViewModel> issues =
        [
            .. result.InitializationIssues.Select(static issue => new RecordingIssueViewModel(issue.MoveId, issue.Message)),
            .. result.Moves
                .Where(static move => !string.IsNullOrWhiteSpace(move.Issue))
                .Select(static move => new RecordingIssueViewModel(move.MoveId, move.Issue ?? string.Empty, move.MoveIndex))
        ];

        return new RecordingAnalysisPresentation(
            summary,
            [.. result.Moves
                .OrderBy(static move => move.PercentageScore)
                .ThenBy(static move => move.MoveIndex)
                .Take(30)
                .Select(static move => new RecordingMoveScoreViewModel(move))],
            [.. result.MoveAggregates
                .Take(30)
                .Select(static aggregate => new RecordingMoveAggregateViewModel(aggregate))],
            [.. result.Moves
                .OrderBy(static move => move.MoveIndex)
                .Select(static move => new RecordingMoveScoreViewModel(move))],
            issues,
            [.. result.Moves
                .OrderBy(static move => move.MoveIndex)
                .Select(static move => new RecordingGraphPoint(
                    move.EndBeatLabel,
                    move.TotalScore,
                    $"{move.MoveId}: {MathF.Round(move.TotalScore):0}"))],
            [.. result.Moves
                .OrderBy(static move => move.MoveIndex)
                .Select(static move => new RecordingGraphPoint(
                    move.EndBeatLabel,
                    move.PercentageScore,
                    $"{move.MoveId}: {move.PercentageScore:0.0}%"))],
            BuildGraphMarkers(timeline.TimelineStructure),
            GraphMinBeat: 0.0,
            GraphMaxBeat: Math.Max(1.0, timeline.TimelineStructure.EndBeat),
            GraphTotalScoreText: $"Total: {MathF.Round(result.TotalScore):0} / 13333");
    }

    private static IReadOnlyList<RecordingGraphMarker> BuildGraphMarkers(TimelineStructureDocument structure)
    {
        List<RecordingGraphMarker> markers = [];
        for (int i = 0; i < structure.Markers.Count; i++)
        {
            double beatLabel = i;
            if (beatLabel < 0.0 || beatLabel > structure.EndBeat)
                continue;

            markers.Add(new RecordingGraphMarker(beatLabel, beatLabel.ToString("0", CultureInfo.InvariantCulture)));
        }

        return markers;
    }

    private static string FormatPercent(float value) => $"{value:0.0}%";
}
