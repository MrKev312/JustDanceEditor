using Avalonia.Platform.Storage;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Messaging;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Services.Motion;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.UbiArt.Recordings;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Recordings", "View/Tools")]
public partial class RecordingsToolViewModel : TimelineToolViewModel, IDisposable, IAsyncDisposable
{
    private readonly IMotionInputClient _motionClient;
    private readonly IMotionRecordingRepository _recordingRepository;
    private readonly EditorSettingsService _editorSettings;
    private readonly RecordingLibraryService _recordingLibrary;
    private readonly RecordingLiveScoreDisplayController _liveScoreDisplay;
    private readonly RecordingBrowserController _recordingBrowser;
    private readonly RecordingDeviceConnectionController _deviceConnection;
    private readonly RecordingAttemptController _recordingAttempt;
    private readonly IWindowService? _windows;
    private bool _disposed;

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
    [NotifyCanExecuteChangedFor(nameof(DeleteRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewRecordingCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportRecordingCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(ImportRecordingCommand))]
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
    internal Avalonia.Controls.Window? DialogOwner => _windows?.MainWindow;

    public RecordingsToolViewModel(
        ITimelineContextService? timelineContext = null,
        IMotionInputClient? motionClient = null,
        IMotionRecordingRepository? recordingRepository = null,
        JdiMotionRecordingAnalyzer? analyzer = null,
        EditorSettingsService? editorSettings = null,
        JdiMotionRecordingLiveScorer? liveScorer = null,
        MotionRecordingScoreHudService? scoreHud = null,
        IWindowService? windows = null)
        : base(timelineContext, deferInitialTimelineAttachment: true)
    {
        _motionClient = motionClient ?? new DsuMotionInputClient();
        _recordingRepository = recordingRepository ?? new JsonMotionRecordingRepository();
        analyzer ??= new JdiMotionRecordingAnalyzer();
        _editorSettings = editorSettings ?? new EditorSettingsService();
        liveScorer ??= new JdiMotionRecordingLiveScorer();
        scoreHud ??= new MotionRecordingScoreHudService();
        _windows = windows;
        _recordingLibrary = new RecordingLibraryService(_recordingRepository);
        _liveScoreDisplay = new RecordingLiveScoreDisplayController(scoreHud);
        _recordingBrowser = new RecordingBrowserController(this, _recordingLibrary, analyzer, _editorSettings);
        _deviceConnection = new RecordingDeviceConnectionController(this, _motionClient);
        _recordingAttempt = new RecordingAttemptController(this, _motionClient, _recordingRepository, _recordingLibrary, liveScorer, _liveScoreDisplay, _editorSettings);

        _editorSettings.PropertyChanged += EditorSettings_PropertyChanged;
        _deviceConnection.Attach();
        WeakReferenceMessenger.Default.Register<RecordingsToolViewModel, ScoringAdjustmentMoveSelectedMessage>(
            this,
            static (recipient, message) => recipient.SelectRecordingMove(message.RecordingPath, message.MoveIndex));
        SelectedRecordingKind = RecordingKinds.First();
        SelectedView = ViewOptions.First();
        InitializeTimelineContext();
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        if (timeline != null)
            _recordingAttempt.AttachTimeline(timeline);

        RefreshCoachIds(timeline);
        ApplyDefaultScoreModeFromExistingMsms();
        StartRecordingCommand.NotifyCanExecuteChanged();
        ImportRecordingCommand.NotifyCanExecuteChanged();
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
        ImportRecordingCommand.NotifyCanExecuteChanged();
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
        ApplyDefaultScoreModeFromExistingMsms();
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
            bool discard = await RecordingsToolDialogs.ShowDiscardPromptAsync(_windows?.MainWindow);
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

    [RelayCommand]
    private async Task RefreshRecordingsAsync()
    {
        await _recordingBrowser.RefreshRecordingsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanImportRecording))]
    private async Task ImportRecordingAsync()
    {
        TimelineEditorViewModel? timeline = ActiveTimeline;
        if (timeline == null || _windows?.MainWindow is not { } window)
            return;

        IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import REC Recordings",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Just Dance recordings") { Patterns = ["*.rec"] },
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            ]
        });
        if (files.Count == 0)
            return;

        IsBusy = true;
        NotifyViewStateChanged();
        int imported = 0;
        int failed = 0;
        string? lastPath = null;
        try
        {
            foreach (IStorageFile file in files)
            {
                try
                {
                    int? inferredCoach = UbiArtMotionRecordingConverter.InferCoachId(file.Name);
                    int? coachId = inferredCoach.HasValue && CoachIds.Contains(inferredCoach.Value)
                        ? inferredCoach
                        : await RecordingsToolDialogs.ShowCoachSelectionDialogAsync(file.Name, CoachIds, SelectedCoachId, window);
                    if (!coachId.HasValue)
                        continue;

                    await using Stream stream = await file.OpenReadAsync();
                    IReadOnlyList<string> importedPaths = await _recordingLibrary.ImportRecAsync(timeline, stream, file.Name, coachId.Value);
                    imported += importedPaths.Count;
                    lastPath = importedPaths.LastOrDefault() ?? lastPath;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException or ArgumentException)
                {
                    failed++;
                    EditorLog.Fallback(ex, $"Import REC recording '{file.Name}'");
                }
            }

            StatusText = (imported, failed) switch
            {
                (0, > 0) => $"Failed to import {failed} REC file(s)",
                ( > 0, > 0) => $"Imported {imported} recording(s); {failed} file(s) failed",
                ( > 0, 0) => $"Imported {imported} recording(s)",
                _ => "No recordings imported"
            };
        }
        finally
        {
            IsBusy = false;
            NotifyViewStateChanged();
        }

        if (imported > 0)
            await _recordingBrowser.RefreshRecordingsAsync(lastPath);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteRecording))]
    private async Task DeleteRecordingAsync()
    {
        await _recordingBrowser.DeleteRecordingAsync();
    }

    private bool CanDeleteRecording() => SelectedRecording != null && !IsBusy && !IsRecording;
    private bool CanImportRecording() => ActiveTimeline != null && !IsBusy && !IsRecording;

    public void FocusMove(int moveIndex)
    {
        _recordingBrowser.FocusMove(moveIndex);
    }

    private void EditorSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EditorSettingsService.ScoringProfile) || IsRecording)
            return;

        _ = _recordingBrowser.AnalyzeSelectedRecordingAsync();
    }

    private void SelectRecordingMove(string recordingPath, int moveIndex)
    {
        RecordingListItem? recording = Recordings.FirstOrDefault(item => string.Equals(item.Path, recordingPath, StringComparison.OrdinalIgnoreCase));
        if (recording != null)
            SelectedRecording = recording;

        FocusMove(moveIndex);
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

    private void ApplyDefaultScoreModeFromExistingMsms()
    {
        if (IsRecording || ActiveTimeline == null)
            return;

        string movesFolder = IntermediatePackageLayout.Resolve(
            ActiveTimeline.RootPath,
            IntermediatePackageLayout.Assets.MovesV7Folder);
        ScoreAgainstExistingClassifiers =
            Directory.Exists(movesFolder)
            && Directory.EnumerateFiles(movesFolder, "*.msm", SearchOption.TopDirectoryOnly).Any();
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
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _editorSettings.PropertyChanged -= EditorSettings_PropertyChanged;
        WeakReferenceMessenger.Default.Unregister<ScoringAdjustmentMoveSelectedMessage>(this);
        await _recordingAttempt.DisposeAsync();
        _deviceConnection.Detach();
        await _motionClient.DisposeAsync();
        base.Dispose();
    }

    public override void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}