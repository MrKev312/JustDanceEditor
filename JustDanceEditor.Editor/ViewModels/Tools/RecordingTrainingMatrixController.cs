using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingTrainingMatrixController(
    MsmTrainingToolViewModel owner,
    RecordingLibraryService recordingLibrary,
    JdiMotionTrainingMatrixAnalyzer analyzer)
{
    private readonly JsonMotionTrainingSelectionRepository _selectionRepository = new();
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private int _refreshVersion;

    public async Task RefreshAsync(bool preserveCurrentMatrix = false)
    {
        int version = Interlocked.Increment(ref _refreshVersion);
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        if (timeline == null || owner.SelectedCoachId is not int coachId)
        {
            owner.TrainingMatrix = null;
            owner.IsTrainingMatrixBusy = false;
            owner.NotifyViewStateChanged();
            return;
        }

        owner.IsTrainingMatrixBusy = true;
        if (!preserveCurrentMatrix)
            owner.TrainingMatrix = null;
        owner.NotifyViewStateChanged();
        try
        {
            List<RecordingSelectionItem> items = await recordingLibrary.LoadCoachRecordingSelectionItemsAsync(timeline, coachId);
            MotionTrainingSelectionDocument selection = await _selectionRepository.LoadAsync(timeline.RootPath);
            MotionTrainingMatrixResult result = await Task.Run(
                () => analyzer.Analyze(
                    timeline.RootPath,
                    timeline.Package,
                    coachId,
                    items.Select(static item => item.Recording).ToArray(),
                    selection,
                    owner.CompareToExistingMsms));
            if (version != _refreshVersion)
                return;

            owner.TrainingMatrix = CreatePresentation(result, items);
            owner.StatusText = result.Rows.Count == 0
                ? $"No recordings found for coach {coachId}"
                : "Click a cell to include or exclude that move sample from MSM generation";
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion)
                owner.StatusText = ex.Message;
        }
        finally
        {
            if (version == _refreshVersion)
            {
                owner.IsTrainingMatrixBusy = false;
                owner.NotifyViewStateChanged();
            }
        }
    }

    public async Task ToggleAsync(int rowIndex, int columnIndex)
    {
        await _toggleGate.WaitAsync();
        try
        {
            TimelineEditorViewModel? timeline = owner.ActiveTimeline;
            RecordingTrainingMatrixViewModel? matrix = owner.TrainingMatrix;
            if (timeline == null || matrix == null || owner.SelectedCoachId is not int coachId ||
                rowIndex < 0 || rowIndex >= matrix.Rows.Count ||
                columnIndex < 0 || columnIndex >= matrix.Columns.Count)
            {
                return;
            }

            RecordingTrainingRowViewModel row = matrix.Rows[rowIndex];
            RecordingTrainingColumnViewModel column = matrix.Columns[columnIndex];
            RecordingTrainingCellViewModel cell = row.Cells[columnIndex];
            bool exclude = !cell.IsExcluded;

            MotionTrainingSelectionDocument selection = await _selectionRepository.LoadAsync(timeline.RootPath);
            selection.SetExcluded(
                row.RecordingId,
                coachId,
                column.TimelineClipId,
                column.MoveId,
                column.MoveOccurrence,
                exclude);
            await _selectionRepository.SaveAsync(timeline.RootPath, selection);

            owner.TrainingMatrix = matrix.WithExclusion(rowIndex, columnIndex, exclude);
            owner.NotifyViewStateChanged();
            await RefreshAsync(preserveCurrentMatrix: true);
        }
        catch (Exception ex)
        {
            owner.StatusText = ex.Message;
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    public void Clear()
    {
        Interlocked.Increment(ref _refreshVersion);
        owner.TrainingMatrix = null;
        owner.IsTrainingMatrixBusy = false;
    }

    private static RecordingTrainingMatrixViewModel CreatePresentation(
        MotionTrainingMatrixResult result,
        IReadOnlyList<RecordingSelectionItem> items)
    {
        Dictionary<Guid, string> names = items
            .GroupBy(static item => item.Recording.RecordingId)
            .ToDictionary(static group => group.Key, static group => group.First().DisplayName);
        RecordingTrainingColumnViewModel[] columns =
        [
            .. result.Columns.Select((column, index) => new RecordingTrainingColumnViewModel(
                index,
                column.MoveIndex,
                column.TimelineClipId,
                column.MoveId,
                column.MoveOccurrence,
                column.StartBeat))
        ];
        RecordingTrainingRowViewModel[] rows =
        [
            .. result.Rows.Select((row, rowIndex) => new RecordingTrainingRowViewModel(
                rowIndex,
                row.RecordingId,
                names.GetValueOrDefault(row.RecordingId, $"Recording {rowIndex + 1}"),
                [
                    .. row.Cells.Select((cell, columnIndex) => new RecordingTrainingCellViewModel(
                        rowIndex,
                        columnIndex,
                        cell.PercentageScore,
                        cell.DifferenceFromConsensus,
                        cell.IsExcluded,
                        cell.Issue))
                ]))
        ];
        return new RecordingTrainingMatrixViewModel(columns, rows);
    }
}
