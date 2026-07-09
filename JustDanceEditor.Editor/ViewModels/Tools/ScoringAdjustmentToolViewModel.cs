using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Scoring;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Scoring Adjustment", "View/Tools")]
public partial class ScoringAdjustmentToolViewModel : TimelineToolViewModel, IDisposable
{
    private readonly ScoringAdjustmentPreviewRunner previewRunner;
    private readonly EditorSettingsService? editorSettings;
    private ScoringAdjustmentPreviewResult? preview;
    private MotionClassifierHeader? savedHeader;
    private byte[]? savedBytes;
    private string? assetPath;
    private string selectionKey = string.Empty;
    private bool applyingDraft;
    private bool syncingGuidedControls;

    [ObservableProperty] public partial string SelectedMoveId { get; set; } = string.Empty;
    [ObservableProperty] public partial string AssetKindText { get; set; } = "No move selected";
    [ObservableProperty] public partial string AssetPathText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusText { get; set; } = "Select a hand MSM move in Library or on the timeline.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoSelection))]
    [NotifyPropertyChangedFor(nameof(ShowMsmEditor))]
    [NotifyPropertyChangedFor(nameof(ShowGestureInfo))]
    [NotifyPropertyChangedFor(nameof(ShowMissingAsset))]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMsmEditor))]
    public partial bool IsMsmAvailable { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGestureInfo))]
    public partial bool IsGestureSelection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMissingAsset))]
    public partial bool IsMissingAsset { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SweepGraphEmptyText))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial double LowThreshold { get; set; } = ScoringAdjustmentDraftMapper.DefaultValue(ScoringAdjustmentParameter.LowThreshold);
    [ObservableProperty] public partial double HighThreshold { get; set; } = ScoringAdjustmentDraftMapper.DefaultValue(ScoringAdjustmentParameter.HighThreshold);
    [ObservableProperty] public partial double AutoCorrelationThreshold { get; set; } = ScoringAdjustmentDraftMapper.DefaultValue(ScoringAdjustmentParameter.AutoCorrelationThreshold);
    [ObservableProperty] public partial double DirectionImpactFactor { get; set; } = ScoringAdjustmentDraftMapper.DefaultValue(ScoringAdjustmentParameter.DirectionImpactFactor);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLowThresholdCustom))]
    public partial bool LowThresholdDefault { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHighThresholdCustom))]
    public partial bool HighThresholdDefault { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoCorrelationThresholdCustom))]
    [NotifyPropertyChangedFor(nameof(IsAutoCorrelationSensitivityCustom))]
    public partial bool AutoCorrelationThresholdDefault { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectionImpactFactorCustom))]
    [NotifyPropertyChangedFor(nameof(IsDirectionSensitivityCustom))]
    public partial bool DirectionImpactFactorDefault { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirectionImpactFactorCustom))]
    public partial bool IgnoreDirection { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoCorrelationThresholdCustom))]
    public partial bool IgnoreAutocorrelation { get; set; }

    [ObservableProperty] public partial ScoringAdjustmentParameter SelectedParameter { get; set; } = ScoringAdjustmentParameter.LowThreshold;
    [ObservableProperty] public partial IReadOnlyList<ScoringAdjustmentSweepSeries> SweepSeries { get; set; } = [];
    [ObservableProperty] public partial ScoringAdjustmentCurve? AdjustmentCurve { get; set; }
    [ObservableProperty] public partial double CurrentParameterValue { get; set; }
    [ObservableProperty] public partial bool CurrentParameterIsDefault { get; set; }
    [ObservableProperty] public partial double SavedParameterValue { get; set; }
    [ObservableProperty] public partial bool SavedParameterIsDefault { get; set; }
    [ObservableProperty] public partial double SavedLowThresholdValue { get; set; }
    [ObservableProperty] public partial double SavedHighThresholdValue { get; set; }
    [ObservableProperty] public partial bool SavedLowThresholdIsDefault { get; set; }
    [ObservableProperty] public partial bool SavedHighThresholdIsDefault { get; set; }
    [ObservableProperty] public partial bool IsWideLayout { get; set; }
    [ObservableProperty] public partial string AverageAccuracyText { get; set; } = "-";
    [ObservableProperty] public partial string AverageDeltaText { get; set; } = "-";
    [ObservableProperty] public partial string PointCountText { get; set; } = "-";
    [ObservableProperty] public partial string SelectedParameterGuidanceText { get; set; } = "Select a slider to see how the loaded recordings react across its full range.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGuidedTuningPanel))]
    [NotifyPropertyChangedFor(nameof(ShowAdvancedTuningPanel))]
    public partial bool UseAdvancedScoringTuning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoCorrelationSensitivityText))]
    [NotifyPropertyChangedFor(nameof(IsAutoCorrelationSensitivityCustom))]
    public partial double AutoCorrelationSensitivity { get; set; } = ScoringAdjustmentDraftMapper.SensitivityFromAutoCorrelation(
        ScoringAdjustmentDraftMapper.DefaultValue(ScoringAdjustmentParameter.AutoCorrelationThreshold));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionSensitivityText))]
    [NotifyPropertyChangedFor(nameof(IsDirectionSensitivityCustom))]
    public partial double DirectionSensitivity { get; set; } = ScoringAdjustmentDraftMapper.DefaultValue(ScoringAdjustmentParameter.DirectionImpactFactor);

    [ObservableProperty] public partial string AutoTuneSummaryText { get; set; } = "Load recordings to get guided shake and direction recommendations.";
    [ObservableProperty] public partial string AutoTuneShakeRecommendationText { get; set; } = "Shake recommendation: waiting for analysis.";
    [ObservableProperty] public partial string AutoTuneDirectionRecommendationText { get; set; } = "Direction recommendation: waiting for analysis.";
    [ObservableProperty] public partial string AutoTuneDistanceRecommendationText { get; set; } = "Distance recommendation: waiting for analysis.";

    public bool ShowNoSelection => !HasSelection;
    public bool ShowMsmEditor => HasSelection && IsMsmAvailable;
    public bool ShowGestureInfo => HasSelection && IsGestureSelection;
    public bool ShowMissingAsset => HasSelection && IsMissingAsset;
    public bool ShowGuidedTuningPanel => !UseAdvancedScoringTuning;
    public bool ShowAdvancedTuningPanel => UseAdvancedScoringTuning;
    public bool IsLowThresholdCustom => !LowThresholdDefault;
    public bool IsHighThresholdCustom => !HighThresholdDefault;
    public bool IsAutoCorrelationThresholdCustom => !AutoCorrelationThresholdDefault && !IgnoreAutocorrelation;
    public bool IsDirectionImpactFactorCustom => !DirectionImpactFactorDefault && !IgnoreDirection;
    public bool IsAutoCorrelationSensitivityCustom => !AutoCorrelationThresholdDefault;
    public bool IsDirectionSensitivityCustom => !DirectionImpactFactorDefault;
    public string AutoCorrelationSensitivityText => ScoringAdjustmentPresentation.Percent(AutoCorrelationSensitivity);
    public string DirectionSensitivityText => ScoringAdjustmentPresentation.Percent(DirectionSensitivity);
    public string SelectedParameterTitle => ScoringAdjustmentPresentation.ParameterTitle(SelectedParameter);
    public string SelectedParameterRangeText => ScoringAdjustmentPresentation.ParameterRange(SelectedParameter);

    public bool IsDirty => IsMsmAvailable && savedHeader != null
        && !ScoringAdjustmentDraftMapper.IsEquivalent(CaptureDraft(), ScoringAdjustmentDraftMapper.FromHeader(savedHeader));
    public int SettingsRow => 0;
    public int SettingsColumn => 0;
    public int SettingsRowSpan => IsWideLayout ? 2 : 1;
    public int SettingsColumnSpan => IsWideLayout ? 1 : 2;
    public int GraphRow => IsWideLayout ? 0 : 1;
    public int GraphColumn => IsWideLayout ? 1 : 0;
    public int GraphRowSpan => IsWideLayout ? 2 : 1;
    public int GraphColumnSpan => IsWideLayout ? 1 : 2;
    public string LowThresholdToolTip => "Raw distance at or below this value becomes the top distance score. Raise it to make near-correct takes reach Perfect more easily.";
    public string HighThresholdToolTip => "Raw distance at or above this value falls to X before final modifiers. Raise it to forgive rough takes.";
    public string AutoCorrelationThresholdToolTip => "Shake detector threshold. Lower values flag repeated shake-like motion more easily; higher values are more forgiving.";
    public string DirectionImpactFactorToolTip => "Direction tendency multiplier. Zero bypasses direction; one applies the full tendency.";
    public string IgnoreDirectionToolTip => "Bypasses direction tendency for this MSM.";
    public string IgnoreAutocorrelationToolTip => "Bypasses the shake detector for this MSM.";
    public string AverageAccuracyToolTip => "Average final score percentage for the current draft across the scored recording instances.";
    public string AverageDeltaToolTip => "Current final score average minus the saved MSM final score average.";
    public string PointCountToolTip => "Number of sampled values, scored move instances, and loaded recordings in the diagram.";
    public string AdjustmentCurveToolTip => "X is MoveSpace statistical distance. Y is the score from the current low/high thresholds.";
    public string SweepGraphToolTip => "Each colored line is one scored move instance across the selected slider range.";
    public string SweepGraphEmptyText => IsBusy
        ? "Calculating " + SelectedParameterTitle.ToLowerInvariant()
        : "No " + SelectedParameterTitle.ToLowerInvariant() + " data yet";

    private MotionRecordingScoringProfile PreviewScoringProfile => editorSettings?.ScoringProfile ?? MotionRecordingScoringProfile.JDNext;

    public ScoringAdjustmentToolViewModel(
        ITimelineContextService? timelineContext = null,
        EditorSettingsService? editorSettings = null)
        : this(new ScoringAdjustmentPreviewAnalyzer(), editorSettings, timelineContext)
    {
    }

    internal ScoringAdjustmentToolViewModel(
        IScoringAdjustmentPreviewAnalyzer previewAnalyzer,
        EditorSettingsService? editorSettings,
        ITimelineContextService? timelineContext = null)
        : base(timelineContext)
    {
        previewRunner = new(previewAnalyzer);
        this.editorSettings = editorSettings;
        UseAdvancedScoringTuning = editorSettings?.UseAdvancedScoringTuning ?? false;
        if (editorSettings != null)
            editorSettings.PropertyChanged += EditorSettings_PropertyChanged;
        if (TimelineContext != null)
            TimelineContext.PropertyChanged += TimelineContext_PropertyChanged;
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline) => RefreshSelection();
    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline) => ClearSelection();

    internal void LoadSelectedMove(string moveId, bool isFullBody)
    {
        if (ActiveTimeline == null || string.IsNullOrWhiteSpace(moveId))
        {
            ClearSelection();
            return;
        }

        string nextKey = (isFullBody ? "gesture:" : "msm:") + moveId;
        if (string.Equals(selectionKey, nextKey, StringComparison.OrdinalIgnoreCase))
            return;

        ResetSelectionState(nextKey, moveId);
        if (isFullBody)
            LoadGesture(moveId);
        else
            LoadMsm(moveId);
    }

    public void SelectParameter(string parameterName)
    {
        if (Enum.TryParse(parameterName, true, out ScoringAdjustmentParameter parameter))
            SelectedParameter = parameter;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (assetPath == null || savedBytes == null)
            return;

        byte[] updated = ScoringAdjustmentDraftMapper.Write(savedBytes, CaptureDraft());
        File.WriteAllBytes(assetPath, updated);
        savedBytes = updated;
        savedHeader = MotionClassifierHeaderEditor.ReadHeader(updated);
        StatusText = "Saved MSM settings.";
        NotifyDraftStateChanged(queuePreview: true);
    }

    private bool CanSave() => IsDirty && assetPath != null;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        if (savedHeader == null)
            return;
        ApplyDraft(ScoringAdjustmentDraftMapper.FromHeader(savedHeader));
        StatusText = "Draft reset to saved MSM settings.";
        NotifyDraftStateChanged(queuePreview: true);
    }

    private bool CanReset() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanApplyAutoTune))]
    private void ApplyAutoTune()
    {
        ScoringAdjustmentRecommendation? recommendation = preview?.Recommendation;
        if (recommendation == null)
            return;

        ApplyDraft(CaptureDraft() with
        {
            LowThreshold = recommendation.LowThreshold,
            LowThresholdDefault = false,
            HighThreshold = recommendation.HighThreshold,
            HighThresholdDefault = false,
            AutoCorrelationThreshold = recommendation.AutoCorrelationThreshold,
            AutoCorrelationThresholdDefault = false,
            DirectionImpactFactor = recommendation.DirectionSensitivity,
            DirectionImpactFactorDefault = false,
            IgnoreAutocorrelation = recommendation.AutoCorrelationSensitivity <= 0.000001,
            IgnoreDirection = recommendation.DirectionSensitivity <= 0.000001
        });
        StatusText = "Applied tuning recommendations as a draft.";
        NotifyDraftStateChanged(queuePreview: true);
    }

    private bool CanApplyAutoTune() => IsMsmAvailable && preview?.Recommendation != null;

    private void RefreshSelection()
    {
        switch (TimelineContext?.SelectedObjects.FirstOrDefault())
        {
            case LibraryItemViewModel { Type: ItemType.HandMove } hand:
                LoadSelectedMove(hand.Id, false);
                break;
            case LibraryItemViewModel { Type: ItemType.FullBodyMove } fullBody:
                LoadSelectedMove(fullBody.Id, true);
                break;
            case MoveClipViewModel move:
                LoadSelectedMove(move.MoveId, move.IsFullBody);
                break;
            default:
                ClearSelection();
                break;
        }
    }

    private void LoadMsm(string moveId)
    {
        TimelineEditorViewModel timeline = ActiveTimeline!;
        string folder = IntermediatePackageLayout.Resolve(timeline.RootPath, IntermediatePackageLayout.Assets.MovesFolder);
        string path = Path.Combine(folder, moveId + ".msm");
        AssetKindText = "Hand MSM";
        AssetPathText = path;
        if (!File.Exists(path))
        {
            IsMissingAsset = true;
            StatusText = "No MSM file exists for this hand move.";
            return;
        }

        try
        {
            assetPath = path;
            savedBytes = File.ReadAllBytes(path);
            savedHeader = MotionClassifierHeaderEditor.ReadHeader(savedBytes);
            IsMsmAvailable = true;
            ApplyDraft(ScoringAdjustmentDraftMapper.FromHeader(savedHeader));
            StatusText = string.Empty;
            NotifyDraftStateChanged(queuePreview: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            assetPath = null;
            savedBytes = null;
            savedHeader = null;
            IsMissingAsset = true;
            StatusText = "Could not read the MSM: " + ex.Message;
        }
    }

    private void LoadGesture(string moveId)
    {
        string rootPath = ActiveTimeline!.RootPath;
        string folder = IntermediatePackageLayout.Resolve(rootPath, IntermediatePackageLayout.Assets.GesturesFolder);
        string? path = Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder)
                .Select(directory => Path.Combine(directory, moveId + ".gesture"))
                .FirstOrDefault(File.Exists)
            : null;
        AssetKindText = "Full-body gesture";
        AssetPathText = path ?? Path.Combine(folder, "*", moveId + ".gesture");
        IsGestureSelection = true;
        IsMissingAsset = path == null;
        StatusText = path == null
            ? "No gesture file exists for this full-body move. MSM settings do not apply."
            : "This is a full-body gesture asset, not a hand MSM. MSM thresholds do not apply.";
    }

    private void ResetSelectionState(string nextKey, string moveId)
    {
        previewRunner.Cancel();
        selectionKey = nextKey;
        preview = null;
        savedBytes = null;
        savedHeader = null;
        assetPath = null;
        SelectedMoveId = moveId;
        HasSelection = true;
        IsMsmAvailable = false;
        IsGestureSelection = false;
        IsMissingAsset = false;
        ResetPreviewPresentation();
    }

    private void ClearSelection()
    {
        if (string.IsNullOrEmpty(selectionKey) && !HasSelection)
            return;
        previewRunner.Cancel();
        selectionKey = string.Empty;
        preview = null;
        savedBytes = null;
        savedHeader = null;
        assetPath = null;
        SelectedMoveId = string.Empty;
        AssetKindText = "No move selected";
        AssetPathText = string.Empty;
        StatusText = "Select a hand MSM move in Library or on the timeline.";
        HasSelection = false;
        IsMsmAvailable = false;
        IsGestureSelection = false;
        IsMissingAsset = false;
        ResetPreviewPresentation();
    }

    private ScoringAdjustmentDraft CaptureDraft()
        => new(
            LowThreshold, LowThresholdDefault,
            HighThreshold, HighThresholdDefault,
            AutoCorrelationThreshold, AutoCorrelationThresholdDefault,
            DirectionImpactFactor, DirectionImpactFactorDefault,
            IgnoreDirection, IgnoreAutocorrelation);

    private void ApplyDraft(ScoringAdjustmentDraft draft)
    {
        applyingDraft = true;
        try
        {
            LowThreshold = draft.LowThreshold;
            LowThresholdDefault = draft.LowThresholdDefault;
            HighThreshold = draft.HighThreshold;
            HighThresholdDefault = draft.HighThresholdDefault;
            AutoCorrelationThreshold = draft.AutoCorrelationThreshold;
            AutoCorrelationThresholdDefault = draft.AutoCorrelationThresholdDefault;
            DirectionImpactFactor = draft.DirectionImpactFactor;
            DirectionImpactFactorDefault = draft.DirectionImpactFactorDefault;
            IgnoreDirection = draft.IgnoreDirection;
            IgnoreAutocorrelation = draft.IgnoreAutocorrelation;
        }
        finally
        {
            applyingDraft = false;
        }
        SyncGuidedControls();
    }

    private void NotifyParameterChanged(ScoringAdjustmentParameter parameter, double value)
    {
        double clamped = ScoringAdjustmentDraftMapper.Clamp(parameter, value);
        if (Math.Abs(value - clamped) > 0.000001)
        {
            SetParameterValue(parameter, clamped);
            return;
        }
        if (!applyingDraft)
        {
            SelectedParameter = parameter;
            NotifyDraftStateChanged(queuePreview: true);
        }
    }

    private void NotifyDraftStateChanged(bool queuePreview)
    {
        if (applyingDraft)
            return;
        SyncGuidedControls();
        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        UpdateMarkers();
        UpdatePreviewPresentation();
        if (queuePreview && IsMsmAvailable)
            QueuePreview(TimeSpan.FromMilliseconds(80));
    }

    private void SyncGuidedControls()
    {
        if (syncingGuidedControls)
            return;
        ScoringAdjustmentDraft draft = CaptureDraft();
        syncingGuidedControls = true;
        AutoCorrelationSensitivity = draft.IgnoreAutocorrelation
            ? 0.0
            : ScoringAdjustmentDraftMapper.SensitivityFromAutoCorrelation(
                ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.AutoCorrelationThreshold));
        DirectionSensitivity = draft.IgnoreDirection
            ? 0.0
            : ScoringAdjustmentDraftMapper.ClampUnit(
                ScoringAdjustmentDraftMapper.EffectiveValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor));
        syncingGuidedControls = false;
    }

    private void ApplyGuidedAutoCorrelation(double value)
    {
        if (syncingGuidedControls || applyingDraft)
            return;
        double sensitivity = ScoringAdjustmentDraftMapper.ClampUnit(value);
        if (Math.Abs(value - sensitivity) > 0.000001)
        {
            AutoCorrelationSensitivity = sensitivity;
            return;
        }
        applyingDraft = true;
        AutoCorrelationThresholdDefault = false;
        IgnoreAutocorrelation = sensitivity <= 0.000001;
        AutoCorrelationThreshold = ScoringAdjustmentDraftMapper.AutoCorrelationFromSensitivity(sensitivity);
        applyingDraft = false;
        NotifyDraftStateChanged(queuePreview: true);
    }

    private void ApplyGuidedDirection(double value)
    {
        if (syncingGuidedControls || applyingDraft)
            return;
        double sensitivity = ScoringAdjustmentDraftMapper.ClampUnit(value);
        if (Math.Abs(value - sensitivity) > 0.000001)
        {
            DirectionSensitivity = sensitivity;
            return;
        }
        applyingDraft = true;
        DirectionImpactFactorDefault = false;
        IgnoreDirection = sensitivity <= 0.000001;
        DirectionImpactFactor = sensitivity;
        applyingDraft = false;
        NotifyDraftStateChanged(queuePreview: true);
    }

    private void QueuePreview(TimeSpan delay)
    {
        if (ActiveTimeline == null || savedBytes == null || !IsMsmAvailable)
            return;

        IsBusy = true;
        UpdateRecommendationText();
        _ = previewRunner.Run(
            ActiveTimeline.Package,
            ActiveTimeline.RootPath,
            SelectedMoveId,
            savedBytes,
            CaptureDraft(),
            PreviewScoringProfile,
            delay,
            result =>
            {
                preview = result;
                IsBusy = false;
                StatusText = result.ScoredMoveCount == 0 ? "No loaded recording contains this move." : string.Empty;
                UpdatePreviewPresentation();
                UpdateRecommendationText();
            },
            error =>
            {
                IsBusy = false;
                StatusText = error.Message;
            });
    }

    private void UpdatePreviewPresentation()
    {
        ScoringAdjustmentDraft draft = CaptureDraft();
        IReadOnlyList<ScoringAdjustmentSample>? samples = preview?.Samples;
        AdjustmentCurve = ScoringAdjustmentPreviewAnalyzer.CreateCurve(
            draft,
            samples,
            preview?.RecordingCount ?? 0,
            preview?.MoveInstanceCount ?? 0);

        IReadOnlyList<ScoringAdjustmentSweepSeries>? series = null;
        if (preview != null)
        {
            series = SelectedParameter is ScoringAdjustmentParameter.LowThreshold or ScoringAdjustmentParameter.HighThreshold
                ? ScoringAdjustmentPreviewAnalyzer.ProjectThresholdSweep(preview.Samples, draft, SelectedParameter, PreviewScoringProfile)
                : preview.Sweeps.GetValueOrDefault(SelectedParameter);
        }
        SweepSeries = series ?? [];
        bool ignored = SelectedParameter == ScoringAdjustmentParameter.AutoCorrelationThreshold
            ? IgnoreAutocorrelation
            : SelectedParameter == ScoringAdjustmentParameter.DirectionImpactFactor && IgnoreDirection;
        SelectedParameterGuidanceText = ScoringAdjustmentPresentation.ParameterGuidance(SelectedParameter, series, ignored);
        (AverageAccuracyText, AverageDeltaText) = ScoringAdjustmentPresentation.Statistics(
            SweepSeries,
            ScoringAdjustmentDraftMapper.GetValue(draft, SelectedParameter),
            ScoringAdjustmentDraftMapper.UsesDefault(draft, SelectedParameter));
        PointCountText = preview == null
            ? "-"
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} sampled values, {1}/{2} scored move instance(s), {3} recording(s)",
                preview.CandidateCount,
                preview.ScoredMoveCount,
                preview.MoveInstanceCount,
                preview.RecordingCount);
    }

    private void UpdateRecommendationText()
    {
        ScoringAdjustmentRecommendation? recommendation = preview?.Recommendation;
        if (recommendation == null)
        {
            AutoTuneSummaryText = preview?.ScoredMoveCount == 0
                ? "No scored recording samples for this move yet."
                : IsBusy ? "Analyzing loaded recordings for guided recommendations." : "Load recordings to get guided recommendations.";
            AutoTuneShakeRecommendationText = "Shake recommendation: waiting for analysis.";
            AutoTuneDirectionRecommendationText = "Direction recommendation: waiting for analysis.";
            AutoTuneDistanceRecommendationText = "Distance recommendation: waiting for analysis.";
        }
        else
        {
            AutoTuneSummaryText = $"Guided tuning ready from {recommendation.SampleCount} scored sample(s).";
            AutoTuneDistanceRecommendationText = $"Distance recommendation: Perfect {recommendation.LowThreshold:0.###}, fail {recommendation.HighThreshold:0.###} from sample distances {recommendation.DistanceMin:0.###}/{recommendation.DistanceMedian:0.###}/{recommendation.DistanceMax:0.###}.";
            AutoTuneShakeRecommendationText = $"Shake sensitivity: {ScoringAdjustmentPresentation.Percent(recommendation.AutoCorrelationSensitivity)} (threshold {recommendation.AutoCorrelationThreshold:0.###}, {recommendation.AutoCorrelationOccurrenceCount}/{recommendation.SampleCount} trips).";
            AutoTuneDirectionRecommendationText = $"Direction sensitivity: {ScoringAdjustmentPresentation.Percent(recommendation.DirectionSensitivity)} (negative direction sum {recommendation.DirectionNegativeImpactSum:0.###}).";
        }
        ApplyAutoTuneCommand.NotifyCanExecuteChanged();
    }

    private void UpdateMarkers()
    {
        ScoringAdjustmentDraft draft = CaptureDraft();
        CurrentParameterValue = ScoringAdjustmentDraftMapper.GetValue(draft, SelectedParameter);
        CurrentParameterIsDefault = ScoringAdjustmentDraftMapper.UsesDefault(draft, SelectedParameter);
        if (savedHeader == null)
        {
            SavedParameterValue = CurrentParameterValue;
            SavedParameterIsDefault = CurrentParameterIsDefault;
            SavedLowThresholdValue = LowThreshold;
            SavedHighThresholdValue = HighThreshold;
            SavedLowThresholdIsDefault = LowThresholdDefault;
            SavedHighThresholdIsDefault = HighThresholdDefault;
            return;
        }

        float savedValue = ScoringAdjustmentDraftMapper.HeaderValue(savedHeader, SelectedParameter);
        SavedParameterIsDefault = ScoringAdjustmentDraftMapper.IsDefault(savedValue);
        SavedParameterValue = SavedParameterIsDefault
            ? ScoringAdjustmentDraftMapper.DefaultValue(SelectedParameter)
            : ScoringAdjustmentDraftMapper.Clamp(SelectedParameter, savedValue);
        SavedLowThresholdIsDefault = ScoringAdjustmentDraftMapper.IsDefault(savedHeader.LowThreshold);
        SavedHighThresholdIsDefault = ScoringAdjustmentDraftMapper.IsDefault(savedHeader.HighThreshold);
        SavedLowThresholdValue = ScoringAdjustmentDraftMapper.SliderValue(savedHeader.LowThreshold, ScoringAdjustmentParameter.LowThreshold);
        SavedHighThresholdValue = ScoringAdjustmentDraftMapper.SliderValue(savedHeader.HighThreshold, ScoringAdjustmentParameter.HighThreshold);
    }

    private void ResetPreviewPresentation()
    {
        SweepSeries = [];
        AdjustmentCurve = null;
        AverageAccuracyText = "-";
        AverageDeltaText = "-";
        PointCountText = "-";
        SelectedParameterGuidanceText = ScoringAdjustmentPresentation.ParameterGuidance(SelectedParameter, null, false);
        UpdateRecommendationText();
    }

    private void SetParameterValue(ScoringAdjustmentParameter parameter, double value)
    {
        switch (parameter)
        {
            case ScoringAdjustmentParameter.LowThreshold: LowThreshold = value; break;
            case ScoringAdjustmentParameter.HighThreshold: HighThreshold = value; break;
            case ScoringAdjustmentParameter.AutoCorrelationThreshold: AutoCorrelationThreshold = value; break;
            case ScoringAdjustmentParameter.DirectionImpactFactor: DirectionImpactFactor = value; break;
        }
    }

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorSettingsService.UseAdvancedScoringTuning))
            UseAdvancedScoringTuning = editorSettings?.UseAdvancedScoringTuning ?? false;
        else if (e.PropertyName == nameof(EditorSettingsService.ScoringProfile))
            QueuePreview(TimeSpan.Zero);
    }

    private void TimelineContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.SelectedObjects))
            RefreshSelection();
    }

    partial void OnLowThresholdChanged(double value) => NotifyParameterChanged(ScoringAdjustmentParameter.LowThreshold, value);
    partial void OnHighThresholdChanged(double value) => NotifyParameterChanged(ScoringAdjustmentParameter.HighThreshold, value);
    partial void OnAutoCorrelationThresholdChanged(double value) => NotifyParameterChanged(ScoringAdjustmentParameter.AutoCorrelationThreshold, value);
    partial void OnDirectionImpactFactorChanged(double value) => NotifyParameterChanged(ScoringAdjustmentParameter.DirectionImpactFactor, value);
    partial void OnLowThresholdDefaultChanged(bool value) => NotifyDraftStateChanged(queuePreview: true);
    partial void OnHighThresholdDefaultChanged(bool value) => NotifyDraftStateChanged(queuePreview: true);
    partial void OnAutoCorrelationThresholdDefaultChanged(bool value) => NotifyDraftStateChanged(queuePreview: true);
    partial void OnDirectionImpactFactorDefaultChanged(bool value) => NotifyDraftStateChanged(queuePreview: true);
    partial void OnIgnoreDirectionChanged(bool value) => NotifyDraftStateChanged(queuePreview: true);
    partial void OnIgnoreAutocorrelationChanged(bool value) => NotifyDraftStateChanged(queuePreview: true);
    partial void OnAutoCorrelationSensitivityChanged(double value) => ApplyGuidedAutoCorrelation(value);
    partial void OnDirectionSensitivityChanged(double value) => ApplyGuidedDirection(value);

    partial void OnSelectedParameterChanged(ScoringAdjustmentParameter value)
    {
        OnPropertyChanged(nameof(SelectedParameterTitle));
        OnPropertyChanged(nameof(SelectedParameterRangeText));
        OnPropertyChanged(nameof(SweepGraphEmptyText));
        UpdateMarkers();
        UpdatePreviewPresentation();
    }

    partial void OnIsWideLayoutChanged(bool value)
    {
        OnPropertyChanged(nameof(SettingsRow));
        OnPropertyChanged(nameof(SettingsColumn));
        OnPropertyChanged(nameof(SettingsRowSpan));
        OnPropertyChanged(nameof(SettingsColumnSpan));
        OnPropertyChanged(nameof(GraphRow));
        OnPropertyChanged(nameof(GraphColumn));
        OnPropertyChanged(nameof(GraphRowSpan));
        OnPropertyChanged(nameof(GraphColumnSpan));
    }

    public override void Dispose()
    {
        previewRunner.Dispose();
        IsBusy = false;
        if (editorSettings != null)
            editorSettings.PropertyChanged -= EditorSettings_PropertyChanged;
        if (TimelineContext != null)
            TimelineContext.PropertyChanged -= TimelineContext_PropertyChanged;
        base.Dispose();
    }
}
