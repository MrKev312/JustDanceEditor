using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("MSM Training", "View/Tools")]
public partial class MsmTrainingToolViewModel : TimelineToolViewModel
{
    private readonly JdiMotionClassifierGenerator _classifierGenerator;
    private readonly RecordingLibraryService _recordingLibrary;
    private readonly RecordingTrainingMatrixController _matrixController;
    private readonly IWindowService? _windows;

    public ObservableCollection<int> CoachIds { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateMsmsCommand))]
    public partial int? SelectedCoachId { get; set; }

    [ObservableProperty]
    public partial bool CompareToExistingMsms { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrainingMatrix))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyTrainingMatrix))]
    public partial RecordingTrainingMatrixViewModel? TrainingMatrix { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyTrainingMatrix))]
    public partial bool IsTrainingMatrixBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Open a song to inspect its recording samples.";

    public bool HasTrainingMatrix => TrainingMatrix != null;
    public bool ShowNoTimeline => ActiveTimeline == null;
    public bool ShowEmptyTrainingMatrix => ActiveTimeline != null && !IsTrainingMatrixBusy && !HasTrainingMatrix;
    public string ComparisonDescription =>
        "Green samples are close to the recording consensus, red samples are outliers, and black samples are excluded from generation. Hover for details; click to toggle.";

    public MsmTrainingToolViewModel(
        ITimelineContextService? timelineContext = null,
        IMotionRecordingRepository? recordingRepository = null,
        JdiMotionClassifierGenerator? classifierGenerator = null,
        JdiMotionTrainingMatrixAnalyzer? trainingAnalyzer = null,
        IWindowService? windows = null)
        : base(timelineContext, deferInitialTimelineAttachment: true)
    {
        IMotionRecordingRepository repository = recordingRepository ?? new JsonMotionRecordingRepository();
        _classifierGenerator = classifierGenerator ?? new JdiMotionClassifierGenerator();
        _recordingLibrary = new RecordingLibraryService(repository);
        _matrixController = new RecordingTrainingMatrixController(
            this,
            _recordingLibrary,
            trainingAnalyzer ?? new JdiMotionTrainingMatrixAnalyzer());
        _windows = windows;
        InitializeTimelineContext();
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        RefreshCoachIds(timeline);
        GenerateMsmsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowNoTimeline));
        OnPropertyChanged(nameof(ShowEmptyTrainingMatrix));

        if (timeline == null)
        {
            _matrixController.Clear();
            StatusText = "Open a song to inspect its recording samples.";
            return;
        }

        _ = _matrixController.RefreshAsync();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
        => _matrixController.Clear();

    partial void OnSelectedCoachIdChanged(int? value)
    {
        GenerateMsmsCommand.NotifyCanExecuteChanged();
        if (ActiveTimeline != null && value.HasValue)
            _ = _matrixController.RefreshAsync();
    }

    partial void OnCompareToExistingMsmsChanged(bool value)
    {
        if (ActiveTimeline != null)
            _ = _matrixController.RefreshAsync(preserveCurrentMatrix: true);
    }

    [RelayCommand]
    private Task RefreshTrainingMatrixAsync()
        => ActiveTimeline == null
            ? Task.CompletedTask
            : _matrixController.RefreshAsync(preserveCurrentMatrix: true);

    [RelayCommand(CanExecute = nameof(CanGenerateMsms))]
    private async Task GenerateMsmsAsync()
    {
        TimelineEditorViewModel timeline = ActiveTimeline ?? throw new InvalidOperationException("No active timeline.");
        int coachId = SelectedCoachId ?? throw new InvalidOperationException("No coach selected.");

        try
        {
            StatusText = "Loading recordings...";
            List<RecordingSelectionItem> recordings = await _recordingLibrary.LoadCoachRecordingSelectionItemsAsync(timeline, coachId);
            if (recordings.Count == 0)
            {
                StatusText = $"No recordings found for coach {coachId}";
                return;
            }

            IReadOnlyList<RecordingSelectionItem>? selected = await RecordingsToolDialogs.ShowRecordingSelectionDialogAsync(
                recordings,
                _windows?.MainWindow);
            if (selected == null)
            {
                StatusText = "MSM generation cancelled";
                return;
            }

            if (selected.Count == 0)
            {
                StatusText = "No recordings selected";
                return;
            }

            StatusText = "Generating MSMs...";
            MotionClassifierGenerationResult result = await _classifierGenerator.GenerateForCoachAsync(
                timeline.RootPath,
                timeline.Package,
                coachId,
                selected.Select(static item => item.Recording).ToArray());

            timeline.RefreshMoveAssetStatus();
            StatusText = result.Issues.Count == 0
                ? $"Generated {result.Classifiers.Count} MSM(s) from {selected.Count} recording(s)"
                : $"Generated {result.Classifiers.Count} MSM(s), {result.Issues.Count} issue(s)";
            await _matrixController.RefreshAsync(preserveCurrentMatrix: true);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    public Task ToggleTrainingCellAsync(int rowIndex, int columnIndex)
        => _matrixController.ToggleAsync(rowIndex, columnIndex);

    internal void NotifyViewStateChanged()
    {
        OnPropertyChanged(nameof(HasTrainingMatrix));
        OnPropertyChanged(nameof(ShowNoTimeline));
        OnPropertyChanged(nameof(ShowEmptyTrainingMatrix));
    }

    private void RefreshCoachIds(TimelineEditorViewModel? timeline)
    {
        CoachIds.Clear();
        if (timeline != null)
        {
            foreach (int coachId in timeline.Package.CoachTimelines.Select(static item => item.CoachId).Distinct().Order())
                CoachIds.Add(coachId);

            if (CoachIds.Count == 0)
            {
                for (int coachId = 0; coachId < Math.Max(0, timeline.CoachCount); coachId++)
                    CoachIds.Add(coachId);
            }
        }

        if (!SelectedCoachId.HasValue || !CoachIds.Contains(SelectedCoachId.Value))
            SelectedCoachId = CoachIds.Count == 0 ? null : CoachIds[0];
    }

    private bool CanGenerateMsms()
        => ActiveTimeline != null
           && SelectedCoachId is int coachId
           && CoachIds.Contains(coachId);
}
