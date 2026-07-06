using Avalonia;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Services.Motion;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Recordings", "View/Tools")]
public partial class RecordingsToolViewModel : TimelineToolViewModel, IDisposable, IAsyncDisposable
{
    private readonly IMotionInputClient _motionClient;
    private readonly IMotionRecordingRepository _recordingRepository;
    private readonly JdiMotionClassifierGenerator _classifierGenerator;
    private readonly RecordingLibraryService _recordingLibrary;
    private readonly RecordingLiveScoreDisplayController _liveScoreDisplay;
    private readonly RecordingBrowserController _recordingBrowser;
    private readonly RecordingDeviceConnectionController _deviceConnection;
    private readonly RecordingAttemptController _recordingAttempt;

    public ObservableCollection<RecordingListItem> Recordings { get; } = [];
    public ObservableCollection<RecordingKindOption> RecordingKinds { get; } =
    [
        new("msm", "MSM"),
        new("gesture", "Gesture")
    ];

    public ObservableCollection<RecordingStatsViewOption> ViewOptions { get; } =
    [
        new("summary", "Summary"),
        new("worstMoves", "Worst Moves"),
        new("worstAverages", "Worst Averages"),
        new("pointsGraph", "Points Graph"),
        new("accuracyGraph", "Accuracy Graph"),
        new("allMoves", "All Moves"),
        new("issues", "Issues")
    ];

    public ObservableCollection<RecordingStatItemViewModel> SummaryItems { get; } = [];
    public ObservableCollection<RecordingMoveScoreViewModel> WorstIndividualMoves { get; } = [];
    public ObservableCollection<RecordingMoveAggregateViewModel> WorstAverageMoves { get; } = [];
    public ObservableCollection<RecordingMoveScoreViewModel> MoveDetails { get; } = [];
    public ObservableCollection<RecordingIssueViewModel> Issues { get; } = [];
    public ObservableCollection<MotionDeviceInfo> Devices { get; } = [];
    public ObservableCollection<int> CoachIds { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewRecordingCommand))]
    public partial RecordingKindOption? SelectedRecordingKind { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteRecordingCommand))]
    public partial RecordingListItem? SelectedRecording { get; set; }

    [ObservableProperty]
    public partial RecordingStatsViewOption? SelectedView { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string Host { get; set; } = MotionInputEndpoint.DefaultDsu.Host;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial int Port { get; set; } = MotionInputEndpoint.DefaultDsu.Port;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    public partial MotionDeviceInfo? SelectedDevice { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateMsmsCommand))]
    public partial int SelectedCoachId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshDevicesCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopAndSaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelAttemptCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateMsmsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewRecordingCommand))]
    [NotifyPropertyChangedFor(nameof(CanEditRecordingSetup))]
    [NotifyPropertyChangedFor(nameof(ShowBrowserPage))]
    [NotifyPropertyChangedFor(nameof(ShowRecordingPage))]
    [NotifyPropertyChangedFor(nameof(ShowMsmRecordingSetup))]
    [NotifyPropertyChangedFor(nameof(ShowGestureRecordingSetup))]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseNewRecordingCommand))]
    [NotifyPropertyChangedFor(nameof(ShowBrowserPage))]
    [NotifyPropertyChangedFor(nameof(ShowRecordingPage))]
    [NotifyPropertyChangedFor(nameof(ShowMsmRecordingSetup))]
    [NotifyPropertyChangedFor(nameof(ShowGestureRecordingSetup))]
    public partial bool IsRecordingSetupVisible { get; set; }

    [ObservableProperty]
    public partial bool ScoreAgainstExistingClassifiers { get; set; }

    [ObservableProperty]
    public partial int SampleCount { get; set; }

    [ObservableProperty]
    public partial int GeneratedClassifierCount { get; set; }

    [ObservableProperty]
    public partial int LiveTotalScore { get; set; }

    [ObservableProperty]
    public partial string RecentMoveFeedbackText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecentMoveScoreText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double RecentMoveFeedbackOpacity { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "No active timeline";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasAnalysis { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<RecordingGraphPoint> ScoreGraphPoints { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<RecordingGraphPoint> AccuracyGraphPoints { get; set; } = [];

    [ObservableProperty]
    public partial string GraphTotalScoreText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<RecordingGraphMarker> GraphMarkers { get; set; } = [];

    [ObservableProperty]
    public partial double GraphMinBeat { get; set; }

    [ObservableProperty]
    public partial double GraphMaxBeat { get; set; } = 1.0;

    public bool HasActiveTimeline => ActiveTimeline != null;
    public bool HasRecordings => Recordings.Count > 0;
    public bool ShowNoTimeline => !HasActiveTimeline;
    public bool ShowNoRecordings => HasActiveTimeline && !HasRecordings && !IsBusy;
    public bool ShowNoAnalysis => HasActiveTimeline && HasRecordings && !HasAnalysis && !IsBusy;
    public bool ShowSummary => HasAnalysis && SelectedView?.Id == "summary";
    public bool ShowWorstMoves => HasAnalysis && SelectedView?.Id == "worstMoves";
    public bool ShowWorstAverages => HasAnalysis && SelectedView?.Id == "worstAverages";
    public bool ShowPointsGraph => HasAnalysis && SelectedView?.Id == "pointsGraph";
    public bool ShowAccuracyGraph => HasAnalysis && SelectedView?.Id == "accuracyGraph";
    public bool ShowAllMoves => HasAnalysis && SelectedView?.Id == "allMoves";
    public bool ShowIssues => HasAnalysis && SelectedView?.Id == "issues";
    public bool ShowBrowserPage => !ShowRecordingPage;
    public bool ShowRecordingPage => IsRecordingSetupVisible || IsRecording;
    public bool ShowMsmRecordingSetup => ShowRecordingPage && SelectedRecordingKind?.Id == "msm";
    public bool ShowGestureRecordingSetup => ShowRecordingPage && SelectedRecordingKind?.Id == "gesture";
    public bool CanEditRecordingSetup => !IsRecording;

    public RecordingsToolViewModel()
    {
        IServiceProvider? services = (Application.Current as App)?.Services;
        _motionClient = services?.GetService<IMotionInputClient>() ?? new DsuMotionInputClient();
        _recordingRepository = services?.GetService<IMotionRecordingRepository>() ?? new JsonMotionRecordingRepository();
        JdiMotionRecordingAnalyzer analyzer = services?.GetService<JdiMotionRecordingAnalyzer>() ?? new JdiMotionRecordingAnalyzer();
        _classifierGenerator = services?.GetService<JdiMotionClassifierGenerator>() ?? new JdiMotionClassifierGenerator();
        JdiMotionRecordingLiveScorer liveScorer = services?.GetService<JdiMotionRecordingLiveScorer>() ?? new JdiMotionRecordingLiveScorer();
        MotionRecordingScoreHudService scoreHud = services?.GetService<MotionRecordingScoreHudService>() ?? new MotionRecordingScoreHudService();
        _recordingLibrary = new RecordingLibraryService(_recordingRepository);
        _liveScoreDisplay = new RecordingLiveScoreDisplayController(scoreHud);
        _recordingBrowser = new RecordingBrowserController(this, _recordingLibrary, analyzer);
        _deviceConnection = new RecordingDeviceConnectionController(this, _motionClient);
        _recordingAttempt = new RecordingAttemptController(this, _motionClient, _recordingRepository, _recordingLibrary, liveScorer, _liveScoreDisplay);

        _deviceConnection.Attach();
        SelectedRecordingKind = RecordingKinds.First();
        SelectedView = ViewOptions.First();
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        if (timeline != null)
            _recordingAttempt.AttachTimeline(timeline);

        RefreshCoachIds(timeline);
        StartRecordingCommand.NotifyCanExecuteChanged();
        GenerateMsmsCommand.NotifyCanExecuteChanged();
        _ = _recordingBrowser.RefreshRecordingsAsync();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        if (IsRecording)
            _ = CancelAttemptAsync();

        if (timeline != null)
        {
            _recordingAttempt.DetachTimeline(timeline);
        }

        CoachIds.Clear();
        SelectedCoachId = 0;
        _recordingBrowser.ClearAll();
        StatusText = "No active timeline";
        StartRecordingCommand.NotifyCanExecuteChanged();
        GenerateMsmsCommand.NotifyCanExecuteChanged();
    }

    protected override void OnTimeChanged()
    {
        _recordingAttempt.HandleTimeChanged();
    }

    partial void OnSelectedRecordingChanged(RecordingListItem? value)
    {
        DeleteRecordingCommand.NotifyCanExecuteChanged();
        _ = _recordingBrowser.AnalyzeSelectedRecordingAsync();
    }

    partial void OnSelectedViewChanged(RecordingStatsViewOption? value)
    {
        NotifyViewStateChanged();
    }

    partial void OnSelectedRecordingKindChanged(RecordingKindOption? value)
    {
        NewRecordingCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowMsmRecordingSetup));
        OnPropertyChanged(nameof(ShowGestureRecordingSetup));

        if (ShowRecordingPage && value?.Id == "gesture")
            StatusText = "Gesture recording is not implemented yet";
        else if (ShowRecordingPage)
            StatusText = IsConnected ? "Ready to record" : "Connect a DSU device";
    }

    [RelayCommand(CanExecute = nameof(CanNewRecording))]
    private void NewRecording()
    {
        SelectedRecordingKind ??= RecordingKinds.FirstOrDefault(static kind => kind.Id == "msm") ?? RecordingKinds.FirstOrDefault();
        IsRecordingSetupVisible = true;
        if (SelectedRecordingKind?.Id == "gesture")
        {
            StatusText = "Gesture recording is not implemented yet";
            return;
        }

        StatusText = IsConnected ? "Ready to record" : "Connect a DSU device";
    }

    [RelayCommand]
    private async Task CloseNewRecordingAsync()
    {
        if (IsRecording)
        {
            bool discard = await RecordingsToolDialogs.ShowDiscardPromptAsync();
            if (!discard || !IsRecording)
                return;

            await CancelAttemptAsync();
        }

        IsRecordingSetupVisible = false;
        _liveScoreDisplay.Clear(this, clearTotal: true);
        StatusText = Recordings.Count == 0
            ? "No recordings found"
            : $"Loaded {Recordings.Count} recording(s)";
        NotifyViewStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        await _deviceConnection.ConnectAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        await _deviceConnection.DisconnectAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRefreshDevices))]
    private async Task RefreshDevicesAsync()
    {
        await _deviceConnection.RefreshDevicesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync()
    {
        await _recordingAttempt.StartAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopAndSaveAsync()
    {
        await StopAndSaveRecordingAttemptAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task CancelAttemptAsync()
    {
        await CancelRecordingAttemptAsync();
    }

    internal async Task StopAndSaveRecordingAttemptAsync()
    {
        string? path = await _recordingAttempt.StopAndSaveAsync();
        if (path != null)
        {
            await _recordingBrowser.RefreshRecordingsAsync(path);
        }
    }

    internal async Task CancelRecordingAttemptAsync()
    {
        await _recordingAttempt.CancelAsync();
        NotifyViewStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerateMsms))]
    private async Task GenerateMsmsAsync()
    {
        TimelineEditorViewModel timeline = ActiveTimeline ?? throw new InvalidOperationException("No active timeline.");

        try
        {
            StatusText = "Loading recordings...";
            List<RecordingSelectionItem> recordings = await _recordingLibrary.LoadCoachRecordingSelectionItemsAsync(timeline, SelectedCoachId);
            if (recordings.Count == 0)
            {
                StatusText = $"No recordings found for coach {SelectedCoachId}";
                return;
            }

            IReadOnlyList<RecordingSelectionItem>? selected = await RecordingsToolDialogs.ShowRecordingSelectionDialogAsync(recordings);
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
                SelectedCoachId,
                selected.Select(static item => item.Recording).ToArray());

            timeline.RefreshMoveAssetStatus();

            GeneratedClassifierCount = result.Classifiers.Count;
            StatusText = result.Issues.Count == 0
                ? $"Generated {result.Classifiers.Count} MSM(s) from {selected.Count} recording(s)"
                : $"Generated {result.Classifiers.Count} MSM(s), {result.Issues.Count} issue(s)";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshRecordingsAsync()
    {
        await _recordingBrowser.RefreshRecordingsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRecording))]
    private async Task DeleteRecordingAsync()
    {
        await _recordingBrowser.DeleteRecordingAsync();
    }

    private bool CanDeleteRecording() => SelectedRecording != null && !IsBusy && !IsRecording;

    public void FocusMove(int moveIndex)
    {
        _recordingBrowser.FocusMove(moveIndex);
    }

    internal void NotifyViewStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveTimeline));
        OnPropertyChanged(nameof(HasRecordings));
        OnPropertyChanged(nameof(ShowNoTimeline));
        OnPropertyChanged(nameof(ShowNoRecordings));
        OnPropertyChanged(nameof(ShowNoAnalysis));
        OnPropertyChanged(nameof(ShowSummary));
        OnPropertyChanged(nameof(ShowWorstMoves));
        OnPropertyChanged(nameof(ShowWorstAverages));
        OnPropertyChanged(nameof(ShowPointsGraph));
        OnPropertyChanged(nameof(ShowAccuracyGraph));
        OnPropertyChanged(nameof(ShowAllMoves));
        OnPropertyChanged(nameof(ShowIssues));
        OnPropertyChanged(nameof(ShowBrowserPage));
        OnPropertyChanged(nameof(ShowRecordingPage));
        OnPropertyChanged(nameof(ShowMsmRecordingSetup));
        OnPropertyChanged(nameof(ShowGestureRecordingSetup));
        DeleteRecordingCommand.NotifyCanExecuteChanged();
        NewRecordingCommand.NotifyCanExecuteChanged();
        CloseNewRecordingCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCoachIds(TimelineEditorViewModel? timeline)
    {
        CoachIds.Clear();

        int coachCount = Math.Max(0, timeline?.CoachCount ?? 0);
        if (timeline != null)
        {
            foreach (int coachId in timeline.Package.CoachTimelines.Select(t => t.CoachId).OrderBy(static id => id))
                CoachIds.Add(coachId);
        }

        if (CoachIds.Count == 0)
        {
            for (int i = 0; i < coachCount; i++)
                CoachIds.Add(i);
        }

        if (!CoachIds.Contains(SelectedCoachId))
            SelectedCoachId = CoachIds.FirstOrDefault();
    }

    private bool CanNewRecording() => !ShowRecordingPage && !IsRecording && ActiveTimeline != null;
    private bool CanConnect() => !IsConnected && !IsRecording && string.Equals(SelectedRecordingKind?.Id, "msm", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Host) && Port is > 0 and <= 65535;
    private bool CanDisconnect() => IsConnected && !IsRecording;
    private bool CanRefreshDevices() => IsConnected && !IsRecording;
    private bool CanStartRecording() => IsConnected && !IsRecording && ActiveTimeline != null
        && string.Equals(SelectedRecordingKind?.Id, "msm", StringComparison.Ordinal)
        && SelectedDevice is { IsConnected: true } && SelectedCoachId >= 0;
    private bool CanStopRecording() => IsRecording;
    private bool CanGenerateMsms() => !IsRecording && ActiveTimeline != null && SelectedCoachId >= 0;

    public async ValueTask DisposeAsync()
    {
        await _recordingAttempt.DisposeAsync();
        _deviceConnection.Detach();
        await _motionClient.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}