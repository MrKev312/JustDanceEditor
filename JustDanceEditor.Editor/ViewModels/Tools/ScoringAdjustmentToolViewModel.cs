using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Scoring;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

public enum ScoringAdjustmentParameter
{
    LowThreshold,
    HighThreshold,
    AutoCorrelationThreshold,
    DirectionImpactFactor
}

public sealed record ScoringAdjustmentSweepPoint(double Value, float Accuracy, bool IsDefaultValue);

public sealed record ScoringAdjustmentSweepSeries(
    string Label,
    int SeriesIndex,
    int RecordingIndex,
    int CoachId,
    int MoveIndex,
    float SavedAccuracy,
    IReadOnlyList<ScoringAdjustmentSweepPoint> Points)
{
    public ScoringAdjustmentSweepPoint? DefaultPoint => Points.FirstOrDefault(static point => point.IsDefaultValue);
    public IReadOnlyList<ScoringAdjustmentSweepPoint> RangePoints => [.. Points.Where(static point => !point.IsDefaultValue).OrderBy(static point => point.Value)];
}

public sealed record ScoringAdjustmentCurvePoint(float StatisticalDistance, float PercentageScore);

public sealed record ScoringAdjustmentCurveSample(
    string Label,
    int SeriesIndex,
    int RecordingIndex,
    int CoachId,
    int MoveIndex,
    float StatisticalDistance,
    float PercentageScore);

public sealed record ScoringAdjustmentCurve(
    IReadOnlyList<ScoringAdjustmentCurvePoint> Points,
    IReadOnlyList<ScoringAdjustmentCurveSample> Samples,
    int CandidateCount,
    int RecordingCount,
    int MoveInstanceCount,
    int ScoredMoveCount);

[RunCommand("Scoring Adjustment", "View/Tools")]
public partial class ScoringAdjustmentToolViewModel : TimelineToolViewModel, IDisposable
{
    private const uint IgnoreDirectionFlag = 1U << 0;
    private const uint IgnoreAutoCorrelationFlag = 1U << 1;
    private const int SweepSampleCount = 25;
    private const int AdjustmentCurvePiecewisePointCount = 3;
    private const double AutoTuneAcceptableAutoCorrelationOccurrenceProportion = 0.01;
    private const double AutoTuneAcceptableDirectionNegativeImpactProportion = 0.01;
    private const double AutoTunePerfectDistanceQuantile = 0.75;
    private const double AutoTuneFailDistanceQuantile = 0.95;

    private readonly JsonMotionRecordingRepository _recordingRepository = new();
    private readonly EditorSettingsService? _editorSettings = (Avalonia.Application.Current as App)?.Services?.GetService<EditorSettingsService>();
    private readonly object _previewGate = new();
    private readonly Dictionary<ScoringAdjustmentParameter, CachedSweep> _sweepCache = [];
    private CachedAdjustmentCache? _adjustmentCache;

    private byte[]? _originalBytes;
    private MotionClassifierHeader? _originalHeader;
    private string? _assetPath;
    private string _selectionKey = string.Empty;
    private IReadOnlyList<LoadedRecording>? _loadedRecordings;
    private CancellationTokenSource? _previewCts;
    private int _previewRequestId;
    private int _assetVersion;
    private bool _isPreviewRunning;
    private bool _previewPending;
    private TimeSpan _pendingPreviewDelay = TimeSpan.Zero;
    private bool _isApplyingDraft;
    private bool _isSyncingGuidedTuningControls;
    private bool _isDisposed;
    private AutoTuneRecommendation? _autoTuneRecommendation;

    [ObservableProperty]
    public partial string SelectedMoveId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AssetKindText { get; set; } = "No move selected";

    [ObservableProperty]
    public partial string AssetPathText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Select a hand MSM move in Library or on the timeline.";

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

    [ObservableProperty]
    public partial double LowThreshold { get; set; } = GetDefaultValue(ScoringAdjustmentParameter.LowThreshold);

    [ObservableProperty]
    public partial double HighThreshold { get; set; } = GetDefaultValue(ScoringAdjustmentParameter.HighThreshold);

    [ObservableProperty]
    public partial double AutoCorrelationThreshold { get; set; } = GetDefaultValue(ScoringAdjustmentParameter.AutoCorrelationThreshold);

    [ObservableProperty]
    public partial double DirectionImpactFactor { get; set; } = GetDefaultValue(ScoringAdjustmentParameter.DirectionImpactFactor);

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

    [ObservableProperty]
    public partial ScoringAdjustmentParameter SelectedParameter { get; set; } = ScoringAdjustmentParameter.LowThreshold;

    [ObservableProperty]
    public partial IReadOnlyList<ScoringAdjustmentSweepSeries> SweepSeries { get; set; } = [];

    [ObservableProperty]
    public partial ScoringAdjustmentCurve? AdjustmentCurve { get; set; }

    [ObservableProperty]
    public partial double CurrentParameterValue { get; set; }

    [ObservableProperty]
    public partial bool CurrentParameterIsDefault { get; set; }

    [ObservableProperty]
    public partial double SavedParameterValue { get; set; }

    [ObservableProperty]
    public partial bool SavedParameterIsDefault { get; set; }

    [ObservableProperty]
    public partial double SavedLowThresholdValue { get; set; }

    [ObservableProperty]
    public partial double SavedHighThresholdValue { get; set; }

    [ObservableProperty]
    public partial bool SavedLowThresholdIsDefault { get; set; }

    [ObservableProperty]
    public partial bool SavedHighThresholdIsDefault { get; set; }

    [ObservableProperty]
    public partial bool IsWideLayout { get; set; }

    [ObservableProperty]
    public partial string AverageAccuracyText { get; set; } = "-";

    [ObservableProperty]
    public partial string AverageDeltaText { get; set; } = "-";

    [ObservableProperty]
    public partial string PointCountText { get; set; } = "-";

    [ObservableProperty]
    public partial string SelectedParameterGuidanceText { get; set; } = "Select a slider to see how the loaded recordings react across its full range.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowGuidedTuningPanel))]
    [NotifyPropertyChangedFor(nameof(ShowAdvancedTuningPanel))]
    public partial bool UseAdvancedScoringTuning { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoCorrelationSensitivityText))]
    [NotifyPropertyChangedFor(nameof(IsAutoCorrelationSensitivityCustom))]
    public partial double AutoCorrelationSensitivity { get; set; } = AutoCorrelationThresholdToSensitivity(GetDefaultValue(ScoringAdjustmentParameter.AutoCorrelationThreshold));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DirectionSensitivityText))]
    [NotifyPropertyChangedFor(nameof(IsDirectionSensitivityCustom))]
    public partial double DirectionSensitivity { get; set; } = GetDefaultValue(ScoringAdjustmentParameter.DirectionImpactFactor);

    [ObservableProperty]
    public partial string AutoTuneSummaryText { get; set; } = "Load recordings to get guided shake and direction recommendations.";

    [ObservableProperty]
    public partial string AutoTuneShakeRecommendationText { get; set; } = "Shake recommendation: waiting for analysis.";

    [ObservableProperty]
    public partial string AutoTuneDirectionRecommendationText { get; set; } = "Direction recommendation: waiting for analysis.";

    [ObservableProperty]
    public partial string AutoTuneDistanceRecommendationText { get; set; } = "Distance recommendation: waiting for analysis.";

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
    public string AutoCorrelationSensitivityText => FormatPercentRatio(AutoCorrelationSensitivity);
    public string DirectionSensitivityText => FormatPercentRatio(DirectionSensitivity);
    public string SelectedParameterTitle => SelectedParameter switch
    {
        ScoringAdjustmentParameter.LowThreshold => "Low threshold sweep",
        ScoringAdjustmentParameter.HighThreshold => "High threshold sweep",
        ScoringAdjustmentParameter.AutoCorrelationThreshold => "Autocorr sweep",
        ScoringAdjustmentParameter.DirectionImpactFactor => "Direction impact sweep",
        _ => "Parameter sweep"
    };
    public string SelectedParameterRangeText => GetParameterRange(SelectedParameter) is { } range
        ? string.Format(
            CultureInfo.InvariantCulture,
            "Slider range: {0:0.###} <= value <= {1:0.###}",
            range.Min,
            range.Max)
        : string.Empty;
    public bool IsDirty => IsMsmAvailable && _originalHeader != null && IsDraftDifferentFrom(_originalHeader);
    public int SettingsRow => 0;
    public int SettingsColumn => 0;
    public int SettingsRowSpan => IsWideLayout ? 2 : 1;
    public int SettingsColumnSpan => IsWideLayout ? 1 : 2;
    public int GraphRow => IsWideLayout ? 0 : 1;
    public int GraphColumn => IsWideLayout ? 1 : 0;
    public int GraphRowSpan => IsWideLayout ? 2 : 1;
    public int GraphColumnSpan => IsWideLayout ? 1 : 2;
    public string LowThresholdToolTip => "Raw distance at or below this value becomes the top distance score. Raise it to make near-correct takes reach Perfect more easily; lower it when almost-right takes are scoring too high.";
    public string HighThresholdToolTip => "Raw distance at or above this value falls to X before final modifiers. Raise it to forgive rough takes; lower it when weak takes need to fail sooner. Tune this together with Low.";
    public string AutoCorrelationThresholdToolTip => "Shake detector threshold. Lower values flag repeated shake-like motion more easily and can cap the final score; higher values are more forgiving.";
    public string DirectionImpactFactorToolTip => "Direction tendency multiplier. 0 bypasses direction, 1 applies full direction tendency; wrong direction can lower the final score, right direction can add a small bonus.";
    public string IgnoreDirectionToolTip => "Bypasses direction tendency for this MSM.";
    public string IgnoreAutocorrelationToolTip => "Bypasses the shake detector for this MSM.";
    public string AverageAccuracyToolTip => "Average final score percentage for the current draft across the scored recording instances.";
    public string AverageDeltaToolTip => "Current final score average minus the saved MSM final score average.";
    public string PointCountToolTip => "Number of sampled values, scored move instances, and loaded recordings included in the current diagram.";
    public string AdjustmentCurveToolTip => "X is MoveSpace statistical distance from 0 to the current high threshold. Y is the threshold score percentage from the current low/high thresholds.";
    public string SweepGraphToolTip => "Each colored line is one scored move instance. The line shows final score percentage across the selected slider range; the Default strip shows the same instances with that MSM value stored as -1.";
    public string SweepGraphEmptyText => IsBusy
        ? "Calculating " + SelectedParameterTitle.ToLowerInvariant()
        : "No " + SelectedParameterTitle.ToLowerInvariant() + " data yet";

    private MotionRecordingScoringProfile PreviewScoringProfile => _editorSettings?.ScoringProfile ?? MotionRecordingScoringProfile.JDNext;

    public ScoringAdjustmentToolViewModel()
    {
        UseAdvancedScoringTuning = _editorSettings?.UseAdvancedScoringTuning ?? false;
        if (_editorSettings != null)
            _editorSettings.PropertyChanged += EditorSettings_PropertyChanged;

        if (TimelineContext != null)
            TimelineContext.PropertyChanged += TimelineContext_PropertyChanged;
    }

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorSettingsService.UseAdvancedScoringTuning))
            UseAdvancedScoringTuning = _editorSettings?.UseAdvancedScoringTuning ?? false;
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        RefreshSelection();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        ClearSelection();
    }

    private void TimelineContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.SelectedObjects))
            RefreshSelection();
    }

    private void RefreshSelection()
    {
        object? selected = TimelineContext?.SelectedObjects.FirstOrDefault();
        switch (selected)
        {
            case LibraryItemViewModel { Type: ItemType.HandMove } hand:
                LoadSelectedMove(hand.Id, isFullBody: false);
                break;
            case LibraryItemViewModel { Type: ItemType.FullBodyMove } fullBody:
                LoadSelectedMove(fullBody.Id, isFullBody: true);
                break;
            case MoveClipViewModel move:
                LoadSelectedMove(move.MoveId, move.IsFullBody);
                break;
            default:
                ClearSelection();
                break;
        }
    }

    internal void LoadSelectedMove(string moveId, bool isFullBody)
    {
        if (ActiveTimeline == null || string.IsNullOrWhiteSpace(moveId))
        {
            ClearSelection();
            return;
        }

        string selectionKey = (isFullBody ? "gesture:" : "msm:") + moveId;
        if (string.Equals(_selectionKey, selectionKey, StringComparison.OrdinalIgnoreCase))
            return;

        _selectionKey = selectionKey;
        CancelPreview();
        _loadedRecordings = null;
        _originalBytes = null;
        _originalHeader = null;
        _assetPath = null;
        _autoTuneRecommendation = null;
        InvalidateSweepCache();
        ResetPreviewText();

        SelectedMoveId = moveId;
        HasSelection = true;
        IsMsmAvailable = false;
        IsGestureSelection = false;
        IsMissingAsset = false;
        CurrentParameterValue = 0.0;
        SavedParameterValue = 0.0;
        CurrentParameterIsDefault = false;
        SavedParameterIsDefault = false;

        if (isFullBody)
        {
            LoadGestureSelection(moveId);
            return;
        }

        LoadMsmSelection(moveId);
    }

    private void LoadMsmSelection(string moveId)
    {
        TimelineEditorViewModel timeline = ActiveTimeline ?? throw new InvalidOperationException("No active timeline.");
        string movesFolder = IntermediatePackageLayout.Resolve(timeline.RootPath, IntermediatePackageLayout.Assets.MovesFolder);
        string path = Path.Combine(movesFolder, moveId + ".msm");
        AssetKindText = "Hand MSM";
        AssetPathText = path;

        if (!File.Exists(path))
        {
            IsMissingAsset = true;
            StatusText = "No MSM file exists for this hand move.";
            NotifyModeChanged();
            return;
        }

        _assetPath = path;
        _originalBytes = File.ReadAllBytes(path);
        _originalHeader = MotionClassifierHeaderEditor.ReadHeader(_originalBytes);
        IsMsmAvailable = true;
        StatusText = string.Empty;
        ApplyHeaderToDraft(_originalHeader);
        NotifyModeChanged();
        QueuePreviewRefresh(TimeSpan.Zero);
    }

    private void LoadGestureSelection(string moveId)
    {
        TimelineEditorViewModel timeline = ActiveTimeline ?? throw new InvalidOperationException("No active timeline.");
        string? gesturePath = ResolveGesturePath(timeline.RootPath, moveId);
        AssetKindText = "Full-body gesture";
        AssetPathText = gesturePath ?? Path.Combine(
            IntermediatePackageLayout.Resolve(timeline.RootPath, IntermediatePackageLayout.Assets.GesturesFolder),
            "*",
            moveId + ".gesture");
        IsGestureSelection = true;
        IsMissingAsset = gesturePath == null;
        StatusText = gesturePath == null
            ? "No gesture file exists for this full-body move. MSM settings do not apply."
            : "This is a full-body gesture asset, not a hand MSM. MSM thresholds do not apply.";
        NotifyModeChanged();
    }

    private static string? ResolveGesturePath(string rootPath, string moveId)
    {
        string gesturesFolder = IntermediatePackageLayout.Resolve(rootPath, IntermediatePackageLayout.Assets.GesturesFolder);
        if (!Directory.Exists(gesturesFolder))
            return null;

        foreach (string folder in Directory.EnumerateDirectories(gesturesFolder))
        {
            string candidate = Path.Combine(folder, moveId + ".gesture");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private void ClearSelection()
    {
        _selectionKey = string.Empty;
        CancelPreview();
        _loadedRecordings = null;
        _originalBytes = null;
        _originalHeader = null;
        _assetPath = null;
        _autoTuneRecommendation = null;
        InvalidateSweepCache();

        SelectedMoveId = string.Empty;
        AssetKindText = "No move selected";
        AssetPathText = string.Empty;
        StatusText = "Select a hand MSM move in Library or on the timeline.";
        HasSelection = false;
        IsMsmAvailable = false;
        IsGestureSelection = false;
        IsMissingAsset = false;
        IsBusy = false;
        CurrentParameterValue = 0.0;
        SavedParameterValue = 0.0;
        CurrentParameterIsDefault = false;
        SavedParameterIsDefault = false;
        ResetPreviewText();
        NotifyModeChanged();
    }

    private void ApplyHeaderToDraft(MotionClassifierHeader header)
    {
        _isApplyingDraft = true;
        try
        {
            LowThresholdDefault = IsDefaultHeaderValue(header.LowThreshold);
            HighThresholdDefault = IsDefaultHeaderValue(header.HighThreshold);
            AutoCorrelationThresholdDefault = IsDefaultHeaderValue(header.AutoCorrelationThreshold);
            DirectionImpactFactorDefault = IsDefaultHeaderValue(header.DirectionImpactFactor);

            LowThreshold = ResolveDraftSliderValue(header.LowThreshold, ScoringAdjustmentParameter.LowThreshold);
            HighThreshold = ResolveDraftSliderValue(header.HighThreshold, ScoringAdjustmentParameter.HighThreshold);
            AutoCorrelationThreshold = ResolveDraftSliderValue(header.AutoCorrelationThreshold, ScoringAdjustmentParameter.AutoCorrelationThreshold);
            DirectionImpactFactor = ResolveDraftSliderValue(header.DirectionImpactFactor, ScoringAdjustmentParameter.DirectionImpactFactor);

            IgnoreDirection = (header.CustomizationBitField & IgnoreDirectionFlag) != 0;
            IgnoreAutocorrelation = (header.CustomizationBitField & IgnoreAutoCorrelationFlag) != 0;
        }
        finally
        {
            _isApplyingDraft = false;
        }

        SyncGuidedTuningControlsFromDraft();
        NotifyDraftChanged(changedParameter: null, queuePreview: false);
    }

    partial void OnLowThresholdChanged(double value) => NotifyParameterValueChanged(ScoringAdjustmentParameter.LowThreshold, value);
    partial void OnHighThresholdChanged(double value) => NotifyParameterValueChanged(ScoringAdjustmentParameter.HighThreshold, value);
    partial void OnAutoCorrelationThresholdChanged(double value) => NotifyParameterValueChanged(ScoringAdjustmentParameter.AutoCorrelationThreshold, value);
    partial void OnDirectionImpactFactorChanged(double value) => NotifyParameterValueChanged(ScoringAdjustmentParameter.DirectionImpactFactor, value);
    partial void OnIsBusyChanged(bool value) => UpdateAutoTuneRecommendationText();
    partial void OnLowThresholdDefaultChanged(bool value) => NotifyDraftChanged(ScoringAdjustmentParameter.LowThreshold, queuePreview: true);
    partial void OnHighThresholdDefaultChanged(bool value) => NotifyDraftChanged(ScoringAdjustmentParameter.HighThreshold, queuePreview: true);
    partial void OnAutoCorrelationThresholdDefaultChanged(bool value) => NotifyDraftChanged(ScoringAdjustmentParameter.AutoCorrelationThreshold, queuePreview: true);
    partial void OnDirectionImpactFactorDefaultChanged(bool value) => NotifyDraftChanged(ScoringAdjustmentParameter.DirectionImpactFactor, queuePreview: true);
    partial void OnIgnoreDirectionChanged(bool value) => NotifyDraftChanged(ScoringAdjustmentParameter.DirectionImpactFactor, queuePreview: true);
    partial void OnIgnoreAutocorrelationChanged(bool value) => NotifyDraftChanged(ScoringAdjustmentParameter.AutoCorrelationThreshold, queuePreview: true);
    partial void OnAutoCorrelationSensitivityChanged(double value) => ApplyGuidedAutoCorrelationSensitivity(value);
    partial void OnDirectionSensitivityChanged(double value) => ApplyGuidedDirectionSensitivity(value);

    private void ApplyGuidedAutoCorrelationSensitivity(double value)
    {
        if (_isSyncingGuidedTuningControls || _isApplyingDraft)
        {
            NotifyGuidedTuningControlTextChanged();
            return;
        }

        double clamped = ClampUnit(value);
        if (Math.Abs(value - clamped) > 0.000001)
        {
            AutoCorrelationSensitivity = clamped;
            return;
        }

        _isApplyingDraft = true;
        try
        {
            AutoCorrelationThresholdDefault = false;
            IgnoreAutocorrelation = clamped <= 0.000001;
            AutoCorrelationThreshold = AutoCorrelationSensitivityToThreshold(clamped);
        }
        finally
        {
            _isApplyingDraft = false;
        }

        NotifyDraftChanged(ScoringAdjustmentParameter.AutoCorrelationThreshold, queuePreview: true);
    }

    private void ApplyGuidedDirectionSensitivity(double value)
    {
        if (_isSyncingGuidedTuningControls || _isApplyingDraft)
        {
            NotifyGuidedTuningControlTextChanged();
            return;
        }

        double clamped = ClampUnit(value);
        if (Math.Abs(value - clamped) > 0.000001)
        {
            DirectionSensitivity = clamped;
            return;
        }

        _isApplyingDraft = true;
        try
        {
            DirectionImpactFactorDefault = false;
            IgnoreDirection = clamped <= 0.000001;
            DirectionImpactFactor = clamped;
        }
        finally
        {
            _isApplyingDraft = false;
        }

        NotifyDraftChanged(ScoringAdjustmentParameter.DirectionImpactFactor, queuePreview: true);
    }

    private void SyncGuidedTuningControlsFromDraft()
    {
        if (_isSyncingGuidedTuningControls)
            return;

        ScoringAdjustmentDraft draft = CaptureDraft();
        _isSyncingGuidedTuningControls = true;
        try
        {
            AutoCorrelationSensitivity = draft.IgnoreAutocorrelation
                ? 0.0
                : AutoCorrelationThresholdToSensitivity(ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.AutoCorrelationThreshold));
            DirectionSensitivity = draft.IgnoreDirection
                ? 0.0
                : ClampUnit(ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor));
        }
        finally
        {
            _isSyncingGuidedTuningControls = false;
        }

        NotifyGuidedTuningControlTextChanged();
    }

    private void NotifyGuidedTuningControlTextChanged()
    {
        OnPropertyChanged(nameof(AutoCorrelationSensitivityText));
        OnPropertyChanged(nameof(DirectionSensitivityText));
        OnPropertyChanged(nameof(IsAutoCorrelationSensitivityCustom));
        OnPropertyChanged(nameof(IsDirectionSensitivityCustom));
    }

    private void NotifyParameterValueChanged(ScoringAdjustmentParameter parameter, double value)
    {
        if (TryClampParameterValue(parameter, value))
            return;

        NotifyDraftChanged(parameter, queuePreview: true);
    }

    private bool TryClampParameterValue(ScoringAdjustmentParameter parameter, double value)
    {
        ParameterRange range = GetParameterRange(parameter);
        double fallback = GetDefaultValue(parameter);
        double clamped = double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, range.Min, range.Max);

        if (Math.Abs(value - clamped) <= 0.000001)
            return false;

        SetParameterValue(parameter, clamped);
        return true;
    }

    private void SetParameterValue(ScoringAdjustmentParameter parameter, double value)
    {
        switch (parameter)
        {
            case ScoringAdjustmentParameter.LowThreshold:
                LowThreshold = value;
                break;
            case ScoringAdjustmentParameter.HighThreshold:
                HighThreshold = value;
                break;
            case ScoringAdjustmentParameter.AutoCorrelationThreshold:
                AutoCorrelationThreshold = value;
                break;
            case ScoringAdjustmentParameter.DirectionImpactFactor:
                DirectionImpactFactor = value;
                break;
        }
    }

    partial void OnSelectedParameterChanged(ScoringAdjustmentParameter value)
    {
        OnPropertyChanged(nameof(SelectedParameterTitle));
        OnPropertyChanged(nameof(SelectedParameterRangeText));
        OnPropertyChanged(nameof(SweepGraphEmptyText));
        UpdateCurrentMarkerFromDraft();
        _ = TryApplyCachedSweepToSelection();
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

    private void NotifyDraftChanged(ScoringAdjustmentParameter? changedParameter, bool queuePreview)
    {
        if (_isApplyingDraft)
            return;

        if (changedParameter.HasValue && SelectedParameter != changedParameter.Value)
            SelectedParameter = changedParameter.Value;

        SyncGuidedTuningControlsFromDraft();
        OnPropertyChanged(nameof(IsDirty));
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ApplyAutoTuneCommand.NotifyCanExecuteChanged();
        UpdateCurrentMarkerFromDraft();
        bool adjustmentCacheIsCurrent = TryApplyCachedAdjustmentCurve();

        if (!queuePreview || !IsMsmAvailable)
            return;

        bool selectedParameterChanged = changedParameter.HasValue && changedParameter.Value == SelectedParameter;
        if (TryApplyCachedSweepToSelection() && selectedParameterChanged && adjustmentCacheIsCurrent)
            return;

        QueuePreviewRefresh(TimeSpan.FromMilliseconds(selectedParameterChanged ? 0 : 80));
    }

    private bool IsDraftDifferentFrom(MotionClassifierHeader header)
        => Math.Abs(GetDraftHeaderValue(ScoringAdjustmentParameter.LowThreshold) - header.LowThreshold) > 0.000001f
            || Math.Abs(GetDraftHeaderValue(ScoringAdjustmentParameter.HighThreshold) - header.HighThreshold) > 0.000001f
            || Math.Abs(GetDraftHeaderValue(ScoringAdjustmentParameter.AutoCorrelationThreshold) - header.AutoCorrelationThreshold) > 0.000001f
            || Math.Abs(GetDraftHeaderValue(ScoringAdjustmentParameter.DirectionImpactFactor) - header.DirectionImpactFactor) > 0.000001f
            || BuildCustomizationBitField(CaptureDraft()) != header.CustomizationBitField;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_assetPath == null || _originalBytes == null)
            return;

        byte[] savedBytes = BuildDraftBytes(_originalBytes, CaptureDraft());
        File.WriteAllBytes(_assetPath, savedBytes);
        _originalBytes = savedBytes;
        _originalHeader = MotionClassifierHeaderEditor.ReadHeader(savedBytes);
        InvalidateSweepCache();
        StatusText = "Saved MSM settings.";
        NotifyDraftChanged(changedParameter: null, queuePreview: false);
        QueuePreviewRefresh(TimeSpan.Zero);
    }

    private bool CanSave() => IsDirty && _assetPath != null;

    [RelayCommand(CanExecute = nameof(CanReset))]
    private void Reset()
    {
        if (_originalHeader == null)
            return;

        ScoringAdjustmentDraft currentDraft = CaptureDraft();
        ScoringAdjustmentDraft savedDraft = CreateDraftFromHeader(_originalHeader);
        if (AreDraftsEquivalent(currentDraft, savedDraft))
        {
            StatusText = string.Empty;
            NotifyDraftChanged(changedParameter: null, queuePreview: false);
            return;
        }

        bool needsPrimitiveRebuild = _adjustmentCache == null || RequiresPrimitiveRebuild(currentDraft, savedDraft);
        ApplyHeaderToDraft(_originalHeader);
        StatusText = "Draft reset to saved MSM settings.";
        if (needsPrimitiveRebuild)
        {
            InvalidateSweepCache();
            QueuePreviewRefresh(TimeSpan.Zero);
        }
        else
        {
            _ = TryApplyCachedAdjustmentCurve();
            _ = TryApplyCachedSweepToSelection();
        }
    }

    private bool CanReset() => IsDirty;

    [RelayCommand(CanExecute = nameof(CanApplyAutoTune))]
    private void ApplyAutoTune()
    {
        if (_autoTuneRecommendation == null)
        {
            StatusText = "Guided tuning analysis is still loading.";
            QueuePreviewRefresh(TimeSpan.Zero);
            return;
        }

        _isApplyingDraft = true;
        try
        {
            LowThresholdDefault = false;
            HighThresholdDefault = false;
            AutoCorrelationThresholdDefault = false;
            DirectionImpactFactorDefault = false;

            LowThreshold = _autoTuneRecommendation.LowThreshold;
            HighThreshold = _autoTuneRecommendation.HighThreshold;

            IgnoreAutocorrelation = _autoTuneRecommendation.AutoCorrelationSensitivity <= 0.000001;
            AutoCorrelationThreshold = _autoTuneRecommendation.AutoCorrelationThreshold;

            IgnoreDirection = _autoTuneRecommendation.DirectionSensitivity <= 0.000001;
            DirectionImpactFactor = _autoTuneRecommendation.DirectionSensitivity;
        }
        finally
        {
            _isApplyingDraft = false;
        }

        SyncGuidedTuningControlsFromDraft();
        NotifyDraftChanged(changedParameter: null, queuePreview: true);
        StatusText = "Applied tuning recommendations as a draft.";
    }

    private bool CanApplyAutoTune() => IsMsmAvailable && _autoTuneRecommendation != null;

    private ScoringAdjustmentDraft CaptureDraft()
        => new(
            LowThreshold,
            LowThresholdDefault,
            HighThreshold,
            HighThresholdDefault,
            AutoCorrelationThreshold,
            AutoCorrelationThresholdDefault,
            DirectionImpactFactor,
            DirectionImpactFactorDefault,
            IgnoreDirection,
            IgnoreAutocorrelation);

    private float GetDraftHeaderValue(ScoringAdjustmentParameter parameter)
        => GetParameterIsDefault(parameter)
            ? -1.0f
            : (float)Math.Clamp(GetParameterValue(parameter), GetParameterRange(parameter).Min, GetParameterRange(parameter).Max);

    private static byte[] BuildDraftBytes(byte[] source, ScoringAdjustmentDraft draft)
        => MotionClassifierHeaderEditor.UpdateHeader(source, new MotionClassifierHeaderUpdate
        {
            LowThreshold = draft.LowThresholdDefault ? -1.0f : (float)Math.Clamp(draft.LowThreshold, 0.4, 1.4),
            HighThreshold = draft.HighThresholdDefault ? -1.0f : (float)Math.Clamp(draft.HighThreshold, 1.5, 6.0),
            AutoCorrelationThreshold = draft.AutoCorrelationThresholdDefault ? -1.0f : (float)Math.Clamp(draft.AutoCorrelationThreshold, 0.5, 1.3),
            DirectionImpactFactor = draft.DirectionImpactFactorDefault ? -1.0f : (float)Math.Clamp(draft.DirectionImpactFactor, 0.0, 1.0),
            CustomizationBitField = BuildCustomizationBitField(draft)
        });

    private static byte[] BuildDraftBytesWithParameter(byte[] source, ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter, SweepCandidate candidate)
        => BuildDraftBytes(source, WithParameterCandidate(draft, parameter, candidate));

    private static ScoringAdjustmentDraft WithParameterCandidate(ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter, SweepCandidate candidate)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => draft with { LowThreshold = candidate.Value, LowThresholdDefault = candidate.IsDefault },
            ScoringAdjustmentParameter.HighThreshold => draft with { HighThreshold = candidate.Value, HighThresholdDefault = candidate.IsDefault },
            ScoringAdjustmentParameter.AutoCorrelationThreshold => draft with { AutoCorrelationThreshold = candidate.Value, AutoCorrelationThresholdDefault = candidate.IsDefault },
            ScoringAdjustmentParameter.DirectionImpactFactor => draft with { DirectionImpactFactor = candidate.Value, DirectionImpactFactorDefault = candidate.IsDefault },
            _ => draft
        };

    private static ScoringAdjustmentDraft CreatePrimitiveProjectionDraft(ScoringAdjustmentDraft draft)
        => draft with
        {
            DirectionImpactFactor = GetDefaultValue(ScoringAdjustmentParameter.DirectionImpactFactor),
            DirectionImpactFactorDefault = false,
            IgnoreDirection = false,
            IgnoreAutocorrelation = false
        };

    private static ScoringAdjustmentDraft CreateDraftFromHeader(MotionClassifierHeader header)
        => new(
            ResolveDraftSliderValue(header.LowThreshold, ScoringAdjustmentParameter.LowThreshold),
            IsDefaultHeaderValue(header.LowThreshold),
            ResolveDraftSliderValue(header.HighThreshold, ScoringAdjustmentParameter.HighThreshold),
            IsDefaultHeaderValue(header.HighThreshold),
            ResolveDraftSliderValue(header.AutoCorrelationThreshold, ScoringAdjustmentParameter.AutoCorrelationThreshold),
            IsDefaultHeaderValue(header.AutoCorrelationThreshold),
            ResolveDraftSliderValue(header.DirectionImpactFactor, ScoringAdjustmentParameter.DirectionImpactFactor),
            IsDefaultHeaderValue(header.DirectionImpactFactor),
            (header.CustomizationBitField & IgnoreDirectionFlag) != 0,
            (header.CustomizationBitField & IgnoreAutoCorrelationFlag) != 0);

    private static bool RequiresPrimitiveRebuild(ScoringAdjustmentDraft current, ScoringAdjustmentDraft next)
        => Math.Abs(current.AutoCorrelationThreshold - next.AutoCorrelationThreshold) > 0.000001
            || current.AutoCorrelationThresholdDefault != next.AutoCorrelationThresholdDefault
            || current.IgnoreAutocorrelation != next.IgnoreAutocorrelation;

    private static bool AreDraftsEquivalent(ScoringAdjustmentDraft left, ScoringAdjustmentDraft right)
        => Math.Abs(left.LowThreshold - right.LowThreshold) <= 0.000001
            && left.LowThresholdDefault == right.LowThresholdDefault
            && Math.Abs(left.HighThreshold - right.HighThreshold) <= 0.000001
            && left.HighThresholdDefault == right.HighThresholdDefault
            && Math.Abs(left.AutoCorrelationThreshold - right.AutoCorrelationThreshold) <= 0.000001
            && left.AutoCorrelationThresholdDefault == right.AutoCorrelationThresholdDefault
            && Math.Abs(left.DirectionImpactFactor - right.DirectionImpactFactor) <= 0.000001
            && left.DirectionImpactFactorDefault == right.DirectionImpactFactorDefault
            && left.IgnoreDirection == right.IgnoreDirection
            && left.IgnoreAutocorrelation == right.IgnoreAutocorrelation;

    private void QueuePreviewRefresh(TimeSpan delay)
    {
        if (_isDisposed || ActiveTimeline == null || _originalBytes == null || !IsMsmAvailable)
            return;

        lock (_previewGate)
        {
            if (_isPreviewRunning)
            {
                if (!_previewPending || delay < _pendingPreviewDelay)
                    _pendingPreviewDelay = delay;

                _previewPending = true;
                return;
            }

            _isPreviewRunning = true;
            _previewPending = false;
            _pendingPreviewDelay = TimeSpan.Zero;
        }

        ScoringAdjustmentDraft draft = CaptureDraft();
        int assetVersion = _assetVersion;
        byte[] originalBytes = _originalBytes;
        TimelineEditorViewModel timeline = ActiveTimeline;
        string moveId = SelectedMoveId;

        CancellationTokenSource cts = new();
        int requestId;
        lock (_previewGate)
        {
            _previewCts?.Cancel();
            _previewCts = cts;
            requestId = ++_previewRequestId;
        }

        IsBusy = true;
        StatusText = string.Empty;
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                CancellationToken cancellationToken = cts.Token;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                IReadOnlyList<LoadedRecording> recordings = _loadedRecordings
                    ?? await LoadRecordingsAsync(timeline.RootPath, cancellationToken).ConfigureAwait(false);

                if (IsCurrentPreviewRequest(requestId, cts))
                    _loadedRecordings = recordings;

                IReadOnlyList<MotionRecordingDocument> documents = [.. recordings.Select(static recording => recording.Document)];
                PreviewResult result = await BuildPreviewResultAsync(
                    timeline.Package,
                    moveId,
                    documents,
                    originalBytes,
                    draft,
                    assetVersion,
                    cancellationToken).ConfigureAwait(false);
                PostPreviewResult(requestId, cts, result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                PostPreviewError(requestId, cts, ex.Message);
            }
        });
    }

    private async Task<IReadOnlyList<LoadedRecording>> LoadRecordingsAsync(string rootPath, CancellationToken cancellationToken)
    {
        List<LoadedRecording> recordings = [];
        foreach (string recordingPath in _recordingRepository.ListRecordingFiles(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                MotionRecordingDocument document = await _recordingRepository.LoadAsync(recordingPath, cancellationToken).ConfigureAwait(false);
                recordings.Add(new LoadedRecording(recordingPath, document));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Ignore malformed recordings in previews, matching the recording browser behavior.
            }
        }

        return recordings;
    }

    private async Task<PreviewResult> BuildPreviewResultAsync(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> documents,
        byte[] originalBytes,
        ScoringAdjustmentDraft draft,
        int assetVersion,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MotionRecordingMoveScorePoint> savedPoints = ScoreMovePoints(
            package,
            moveId,
            documents,
            originalBytes,
            PreviewScoringProfile,
            CancellationToken.None);
        byte[] primitiveBytes = BuildDraftBytes(originalBytes, CreatePrimitiveProjectionDraft(draft));
        IReadOnlyList<MotionRecordingMoveScorePoint> primitivePoints = ScoreMovePoints(
            package,
            moveId,
            documents,
            primitiveBytes,
            PreviewScoringProfile,
            CancellationToken.None);

        Task<CachedSweep>[] sweepTasks = [.. new[]
            {
                ScoringAdjustmentParameter.AutoCorrelationThreshold,
                ScoringAdjustmentParameter.DirectionImpactFactor
            }
            .Select(parameter => Task.Run(
                () => BuildParameterSweep(package, moveId, documents, originalBytes, savedPoints, draft, parameter, assetVersion, cancellationToken),
                CancellationToken.None))];
        Task<CachedAdjustmentCache> adjustmentTask = Task.Run(
            () => BuildAdjustmentCache(documents, savedPoints, primitivePoints, draft, assetVersion),
            CancellationToken.None);

        CachedSweep[] sweeps = await Task.WhenAll(sweepTasks).ConfigureAwait(false);
        CachedAdjustmentCache adjustmentCache = await adjustmentTask.ConfigureAwait(false);
        AutoTuneRecommendation? recommendation = BuildAutoTuneRecommendation(
            sweeps.FirstOrDefault(static sweep => sweep.Parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold),
            adjustmentCache);
        return new PreviewResult(assetVersion, documents.Count, sweeps, adjustmentCache, recommendation);
    }

    private CachedSweep BuildParameterSweep(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> documents,
        byte[] originalBytes,
        IReadOnlyList<MotionRecordingMoveScorePoint> savedPoints,
        ScoringAdjustmentDraft draft,
        ScoringAdjustmentParameter parameter,
        int assetVersion,
        CancellationToken cancellationToken)
    {
        SweepCacheKey cacheKey = CreateCacheKey(parameter, draft, assetVersion);
        Dictionary<MoveScorePointKey, SeriesBuilder> builders = [];
        Dictionary<MoveScorePointKey, MotionRecordingMoveScorePoint> savedByKey = [];
        Dictionary<double, int> autoCorrelationOccurrenceCounts = [];

        foreach (MotionRecordingMoveScorePoint point in savedPoints)
        {
            MoveScorePointKey key = MoveScorePointKey.From(point);
            savedByKey[key] = point;
            if (!string.IsNullOrWhiteSpace(point.Issue))
                continue;

            builders[key] = new SeriesBuilder(
                CreateSeriesLabel(point),
                point.RecordingIndex,
                point.CoachId,
                point.MoveIndex,
                GetPreviewAccuracy(point));
        }

        foreach (SweepCandidate candidate in GetSweepCandidates(parameter))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            byte[] bytes = BuildDraftBytesWithParameter(originalBytes, draft, parameter, candidate);
            IReadOnlyList<MotionRecordingMoveScorePoint> points = ScoreMovePoints(package, moveId, documents, bytes, PreviewScoringProfile, CancellationToken.None);
            IReadOnlyList<MotionRecordingMoveScorePoint> autoCorrelationCountPoints = points;
            if (parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold && draft.IgnoreAutocorrelation)
            {
                byte[] countBytes = BuildDraftBytesWithParameter(
                    originalBytes,
                    draft with { IgnoreAutocorrelation = false },
                    parameter,
                    candidate);
                autoCorrelationCountPoints = ScoreMovePoints(package, moveId, documents, countBytes, PreviewScoringProfile, CancellationToken.None);
            }

            if (parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold)
            {
                foreach (MotionRecordingMoveScorePoint point in autoCorrelationCountPoints)
                {
                    if (!string.IsNullOrWhiteSpace(point.Issue))
                        continue;

                    if (point.MoveSpace?.AutoCorrelationTime > -6.0f)
                    {
                        double countKey = Math.Round(candidate.Value, 6);
                        autoCorrelationOccurrenceCounts[countKey] = autoCorrelationOccurrenceCounts.TryGetValue(countKey, out int count)
                            ? count + 1
                            : 1;
                    }
                }
            }

            foreach (MotionRecordingMoveScorePoint point in points)
            {
                if (!string.IsNullOrWhiteSpace(point.Issue))
                    continue;

                MoveScorePointKey key = MoveScorePointKey.From(point);
                if (!builders.TryGetValue(key, out SeriesBuilder? builder))
                {
                    float savedAccuracy = savedByKey.TryGetValue(key, out MotionRecordingMoveScorePoint? savedPoint)
                        && string.IsNullOrWhiteSpace(savedPoint.Issue)
                            ? GetPreviewAccuracy(savedPoint)
                            : GetPreviewAccuracy(point);
                    builder = new SeriesBuilder(
                        CreateSeriesLabel(point),
                        point.RecordingIndex,
                        point.CoachId,
                        point.MoveIndex,
                        savedAccuracy);
                    builders[key] = builder;
                }

                builder.Points.Add(new ScoringAdjustmentSweepPoint(candidate.Value, GetPreviewAccuracy(point), candidate.IsDefault));
            }
        }

        List<ScoringAdjustmentSweepSeries> series = [];
        int seriesIndex = 0;
        foreach (SeriesBuilder builder in builders.Values
            .OrderBy(static builder => builder.RecordingIndex)
            .ThenBy(static builder => builder.CoachId)
            .ThenBy(static builder => builder.MoveIndex))
        {
            IReadOnlyList<ScoringAdjustmentSweepPoint> points =
            [
                .. builder.Points
                    .Where(static point => point.IsDefaultValue)
                    .Take(1),
                .. builder.Points
                    .Where(static point => !point.IsDefaultValue)
                    .OrderBy(static point => point.Value)
            ];
            if (points.Count == 0)
                continue;

            series.Add(new ScoringAdjustmentSweepSeries(
                builder.Label,
                seriesIndex++,
                builder.RecordingIndex,
                builder.CoachId,
                builder.MoveIndex,
                builder.SavedAccuracy,
                points));
        }

        int moveInstanceCount = savedPoints.Count;
        int scoredMoveCount = series.Count;
        float savedAverage = scoredMoveCount == 0 ? 0.0f : series.Average(static item => item.SavedAccuracy);
        return new CachedSweep(
            parameter,
            cacheKey,
            series,
            SweepSampleCount + 1,
            documents.Count,
            moveInstanceCount,
            scoredMoveCount,
            savedAverage,
            autoCorrelationOccurrenceCounts);
    }

    private CachedAdjustmentCache BuildAdjustmentCache(
        IReadOnlyList<MotionRecordingDocument> documents,
        IReadOnlyList<MotionRecordingMoveScorePoint> savedPoints,
        IReadOnlyList<MotionRecordingMoveScorePoint> primitivePoints,
        ScoringAdjustmentDraft draft,
        int assetVersion)
    {
        AdjustmentCacheKey cacheKey = CreateAdjustmentCacheKey(draft, assetVersion);
        Dictionary<MoveScorePointKey, MotionRecordingMoveScorePoint> savedByKey = [];
        foreach (MotionRecordingMoveScorePoint point in savedPoints)
            savedByKey[MoveScorePointKey.From(point)] = point;

        List<AdjustmentSeriesInfo> seriesInfos = [.. primitivePoints
            .Where(static point => string.IsNullOrWhiteSpace(point.Issue) && point.MoveSpace != null)
            .Select(point =>
            {
                MoveScorePointKey key = MoveScorePointKey.From(point);
                bool hasSavedPoint = savedByKey.TryGetValue(key, out MotionRecordingMoveScorePoint? savedPoint)
                    && string.IsNullOrWhiteSpace(savedPoint.Issue);
                float savedPercentageScore = hasSavedPoint
                    ? GetPreviewAccuracy(savedPoint!)
                    : GetPreviewAccuracy(point);
                return new AdjustmentSeriesInfo(
                    key,
                    CreateSeriesLabel(point),
                    point.RecordingIndex,
                    point.CoachId,
                    point.MoveIndex,
                    savedPercentageScore,
                    point.MoveSpace!);
            })];

        return new CachedAdjustmentCache(
            cacheKey,
            seriesInfos,
            documents.Count,
            savedPoints.Count,
            seriesInfos.Count);
    }

    private static AutoTuneRecommendation? BuildAutoTuneRecommendation(
        CachedSweep? autoCorrelationSweep,
        CachedAdjustmentCache adjustmentCache)
    {
        int sampleCount = adjustmentCache.ScoredMoveCount;
        if (sampleCount <= 0)
            return null;

        (double lowThreshold, double highThreshold, double distanceMin, double distanceMedian, double distanceMax) =
            ResolveRecommendedDistanceThresholds(adjustmentCache.SeriesInfos);
        (double autoThreshold, int autoOccurrenceCount, double autoOccurrenceProportion) =
            ResolveRecommendedAutoCorrelationThreshold(autoCorrelationSweep, sampleCount);
        double directionNegativeImpactSum = 0.0;
        foreach (AdjustmentSeriesInfo info in adjustmentCache.SeriesInfos)
        {
            if (info.MoveSpace.DirectionTendencyIgnored)
                continue;

            float directionImpact = SanitizeFinite(info.MoveSpace.DirectionTendencyImpactOnScoreRatio, 0.0f);
            if (directionImpact < 0.0f)
                directionNegativeImpactSum += -directionImpact;
        }

        double directionSensitivity = directionNegativeImpactSum <= 0.000001
            ? 1.0
            : 0.1 * Math.Floor(AutoTuneAcceptableDirectionNegativeImpactProportion * 10.0 * sampleCount / directionNegativeImpactSum);
        directionSensitivity = ClampUnit(directionSensitivity);

        return new AutoTuneRecommendation(
            lowThreshold,
            highThreshold,
            distanceMin,
            distanceMedian,
            distanceMax,
            autoThreshold,
            AutoCorrelationThresholdToSensitivity(autoThreshold),
            autoOccurrenceCount,
            autoOccurrenceProportion,
            directionSensitivity,
            directionNegativeImpactSum,
            sampleCount);
    }

    private static (double LowThreshold, double HighThreshold, double Min, double Median, double Max) ResolveRecommendedDistanceThresholds(
        IReadOnlyList<AdjustmentSeriesInfo> seriesInfos)
    {
        List<double> distances =
        [
            .. seriesInfos
                .Select(static info => (double)info.MoveSpace.StatisticalDistance)
                .Where(static value => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0)
                .OrderBy(static value => value)
        ];

        if (distances.Count == 0)
        {
            return (
                GetDefaultValue(ScoringAdjustmentParameter.LowThreshold),
                GetDefaultValue(ScoringAdjustmentParameter.HighThreshold),
                0.0,
                0.0,
                0.0);
        }

        double min = distances[0];
        double median = Quantile(distances, 0.5);
        double max = distances[^1];
        double perfectDistance = Quantile(distances, AutoTunePerfectDistanceQuantile);
        double roughAcceptedDistance = Quantile(distances, AutoTuneFailDistanceQuantile);
        ParameterRange lowRange = GetParameterRange(ScoringAdjustmentParameter.LowThreshold);
        ParameterRange highRange = GetParameterRange(ScoringAdjustmentParameter.HighThreshold);

        double low = Math.Clamp(perfectDistance, lowRange.Min, lowRange.Max);
        double tailSpan = Math.Max(roughAcceptedDistance - low, 0.0);
        double safetyMargin = Math.Max(0.25, tailSpan * 0.5);
        double highCandidate = roughAcceptedDistance + safetyMargin;
        if (distances.Count < 4)
            highCandidate = Math.Max(highCandidate, low + 1.0);

        double high = Math.Clamp(
            Math.Max(Math.Max(highCandidate, low + 0.75), highRange.Min),
            highRange.Min,
            highRange.Max);

        return (low, high, min, median, max);
    }

    private static double Quantile(IReadOnlyList<double> sortedValues, double quantile)
    {
        if (sortedValues.Count == 0)
            return 0.0;

        if (sortedValues.Count == 1)
            return sortedValues[0];

        double position = Math.Clamp(quantile, 0.0, 1.0) * (sortedValues.Count - 1);
        int lowerIndex = (int)Math.Floor(position);
        int upperIndex = (int)Math.Ceiling(position);
        if (lowerIndex == upperIndex)
            return sortedValues[lowerIndex];

        double ratio = position - lowerIndex;
        return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * ratio);
    }

    private static (double Threshold, int OccurrenceCount, double OccurrenceProportion) ResolveRecommendedAutoCorrelationThreshold(
        CachedSweep? sweep,
        int sampleCount)
    {
        ParameterRange range = GetParameterRange(ScoringAdjustmentParameter.AutoCorrelationThreshold);
        if (sweep == null || sampleCount <= 0)
            return (range.Max, 0, 0.0);

        List<double> candidates = [.. sweep.Series
            .SelectMany(static series => series.RangePoints)
            .Select(static point => Math.Round(point.Value, 6))
            .Distinct()
            .OrderBy(static value => value)];

        if (candidates.Count == 0)
            return (range.Max, 0, 0.0);

        double recommended = candidates[0];
        int recommendedCount = sweep.AutoCorrelationOccurrenceCounts.TryGetValue(Math.Round(recommended, 6), out int count)
            ? count
            : 0;

        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            double candidate = candidates[i];
            int occurrenceCount = sweep.AutoCorrelationOccurrenceCounts.TryGetValue(Math.Round(candidate, 6), out count)
                ? count
                : 0;
            double proportion = occurrenceCount / (double)sampleCount;
            if (proportion <= AutoTuneAcceptableAutoCorrelationOccurrenceProportion)
            {
                recommended = candidate;
                recommendedCount = occurrenceCount;
                continue;
            }

            int saferIndex = Math.Min(candidates.Count - 1, i + 1);
            recommended = candidates[saferIndex];
            recommendedCount = sweep.AutoCorrelationOccurrenceCounts.TryGetValue(Math.Round(recommended, 6), out count)
                ? count
                : 0;
            return (recommended, recommendedCount, recommendedCount / (double)sampleCount);
        }

        return (recommended, recommendedCount, recommendedCount / (double)sampleCount);
    }

    private IReadOnlyList<MotionRecordingMoveScorePoint> ScoreMovePoints(
        IntermediateSongPackage package,
        string moveId,
        IReadOnlyList<MotionRecordingDocument> documents,
        byte[] classifierBytes,
        MotionRecordingScoringProfile scoringProfile,
        CancellationToken cancellationToken)
    {
        JdiMotionRecordingMoveScorer scorer = new();
        return scorer.ScoreMoveInstances(
            package,
            moveId,
            documents,
            classifierBytes,
            scoringProfile: scoringProfile,
            cancellationToken: cancellationToken);
    }

    private void PostPreviewResult(int requestId, CancellationTokenSource cts, PreviewResult result)
        => Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrentPreviewRequest(requestId, cts))
                return;

            ScoringAdjustmentDraft currentDraft = CaptureDraft();
            foreach (CachedSweep sweep in result.Sweeps)
            {
                SweepCacheKey expectedKey = CreateCacheKey(sweep.Parameter, currentDraft, _assetVersion);
                if (sweep.CacheKey == expectedKey && result.AssetVersion == _assetVersion)
                    _sweepCache[sweep.Parameter] = sweep;
            }

            AdjustmentCacheKey expectedAdjustmentKey = CreateAdjustmentCacheKey(currentDraft, _assetVersion);
            if (result.AdjustmentCache.CacheKey == expectedAdjustmentKey && result.AssetVersion == _assetVersion)
                _adjustmentCache = result.AdjustmentCache;

            _autoTuneRecommendation = result.AutoTuneRecommendation;
            UpdateAutoTuneRecommendationText();
            _ = TryApplyCachedAdjustmentCurve();
            _ = TryApplyCachedSweepToSelection();
            CompletePreviewRequest();
        });

    private void PostPreviewError(int requestId, CancellationTokenSource cts, string message)
        => Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrentPreviewRequest(requestId, cts))
                return;

            StatusText = message;
            CompletePreviewRequest();
        });

    private void CompletePreviewRequest()
    {
        bool shouldRunAgain;
        TimeSpan delay;
        lock (_previewGate)
        {
            _isPreviewRunning = false;
            shouldRunAgain = _previewPending && !_isDisposed;
            delay = _pendingPreviewDelay;
            _previewPending = false;
            _pendingPreviewDelay = TimeSpan.Zero;
        }

        IsBusy = false;
        NotifyDraftChanged(changedParameter: null, queuePreview: false);
        if (shouldRunAgain)
            QueuePreviewRefresh(delay);
    }

    private bool IsCurrentPreviewRequest(int requestId, CancellationTokenSource cts)
    {
        lock (_previewGate)
            return !_isDisposed && requestId == _previewRequestId && ReferenceEquals(cts, _previewCts) && !cts.IsCancellationRequested;
    }

    private bool TryApplyCachedSweepToSelection()
    {
        if (!IsMsmAvailable)
            return false;

        ScoringAdjustmentDraft draft = CaptureDraft();
        if ((SelectedParameter is ScoringAdjustmentParameter.LowThreshold
                or ScoringAdjustmentParameter.HighThreshold)
            && _adjustmentCache != null
            && _adjustmentCache.CacheKey == CreateAdjustmentCacheKey(draft, _assetVersion))
        {
            _sweepCache[SelectedParameter] = BuildSweepFromAdjustmentCache(_adjustmentCache, draft, SelectedParameter, _assetVersion);
        }

        if (!_sweepCache.TryGetValue(SelectedParameter, out CachedSweep? sweep)
            || sweep.CacheKey != CreateCacheKey(SelectedParameter, draft, _assetVersion))
        {
            SweepSeries = [];
            AverageAccuracyText = "-";
            AverageDeltaText = "-";
            PointCountText = "-";
            SelectedParameterGuidanceText = CreateParameterGuidance(SelectedParameter, null);
            return false;
        }

        SweepSeries = sweep.Series;
        UpdateCurrentMarkerFromDraft();
        UpdateCurrentStatsFromSweep(sweep);
        SelectedParameterGuidanceText = CreateParameterGuidance(SelectedParameter, sweep);
        PointCountText = string.Format(
            CultureInfo.InvariantCulture,
            "{0} sampled values, {1}/{2} scored move instance(s), {3} recording(s)",
            sweep.CandidateCount,
            sweep.ScoredMoveCount,
            sweep.MoveInstanceCount,
            sweep.RecordingCount);
        StatusText = sweep.Series.Count == 0
            ? "No loaded recording contains this move."
            : string.Empty;
        return true;
    }

    private CachedSweep BuildSweepFromAdjustmentCache(
        CachedAdjustmentCache adjustmentCache,
        ScoringAdjustmentDraft draft,
        ScoringAdjustmentParameter parameter,
        int assetVersion)
    {
        bool lowSweep = parameter == ScoringAdjustmentParameter.LowThreshold;
        bool highSweep = parameter == ScoringAdjustmentParameter.HighThreshold;
        double fixedLow = ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.LowThreshold);
        double fixedHigh = ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.HighThreshold);
        MotionRecordingScoringProfile scoringProfile = PreviewScoringProfile;

        Dictionary<MoveScorePointKey, SeriesBuilder> builders = [];
        foreach (AdjustmentSeriesInfo info in adjustmentCache.SeriesInfos)
        {
            builders[info.Key] = new SeriesBuilder(
                info.Label,
                info.RecordingIndex,
                info.CoachId,
                info.MoveIndex,
                info.SavedPercentageScore);
        }

        foreach (SweepCandidate candidate in GetSweepCandidates(parameter))
        {
            double low = lowSweep ? candidate.Value : fixedLow;
            double high = highSweep ? candidate.Value : fixedHigh;
            foreach (AdjustmentSeriesInfo info in adjustmentCache.SeriesInfos)
            {
                if (!builders.TryGetValue(info.Key, out SeriesBuilder? builder))
                    continue;

                float score = ProjectPreviewPercentage(info, low, high, draft, scoringProfile);
                builder.Points.Add(new ScoringAdjustmentSweepPoint(candidate.Value, score, candidate.IsDefault));
            }
        }

        List<ScoringAdjustmentSweepSeries> series = [];
        int seriesIndex = 0;
        foreach (SeriesBuilder builder in builders.Values
            .OrderBy(static builder => builder.RecordingIndex)
            .ThenBy(static builder => builder.CoachId)
            .ThenBy(static builder => builder.MoveIndex))
        {
            IReadOnlyList<ScoringAdjustmentSweepPoint> points =
            [
                .. builder.Points
                    .Where(static point => point.IsDefaultValue)
                    .Take(1),
                .. builder.Points
                    .Where(static point => !point.IsDefaultValue)
                    .OrderBy(static point => point.Value)
            ];
            if (points.Count == 0)
                continue;

            series.Add(new ScoringAdjustmentSweepSeries(
                builder.Label,
                seriesIndex++,
                builder.RecordingIndex,
                builder.CoachId,
                builder.MoveIndex,
                builder.SavedAccuracy,
                points));
        }

        int scoredMoveCount = series.Count;
        float savedAverage = scoredMoveCount == 0 ? 0.0f : series.Average(static item => item.SavedAccuracy);
        return new CachedSweep(
            parameter,
            CreateCacheKey(parameter, draft, assetVersion),
            series,
            SweepSampleCount + 1,
            adjustmentCache.RecordingCount,
            adjustmentCache.MoveInstanceCount,
            scoredMoveCount,
            savedAverage,
            new Dictionary<double, int>());
    }

    private bool TryApplyCachedAdjustmentCurve()
    {
        if (!IsMsmAvailable)
            return false;

        ScoringAdjustmentDraft draft = CaptureDraft();
        AdjustmentCacheKey currentCacheKey = CreateAdjustmentCacheKey(draft, _assetVersion);
        CachedAdjustmentCache? cache = _adjustmentCache != null
            && _adjustmentCache.CacheKey == currentCacheKey
            ? _adjustmentCache
            : null;

        AdjustmentCurve = CreateAdjustmentCurve(cache, draft);
        return cache != null;
    }

    private void UpdateCurrentStatsFromSweep(CachedSweep sweep)
    {
        if (sweep.Series.Count == 0)
        {
            AverageAccuracyText = "-";
            AverageDeltaText = "-";
            return;
        }

        float currentAverage = EstimateAverageAt(sweep.Series, GetParameterValue(SelectedParameter), GetParameterIsDefault(SelectedParameter));
        float savedAverage = sweep.SavedAverageAccuracy;
        AverageAccuracyText = currentAverage.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        AverageDeltaText = (currentAverage - savedAverage >= 0 ? "+" : string.Empty) + (currentAverage - savedAverage).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private string CreateParameterGuidance(ScoringAdjustmentParameter parameter, CachedSweep? sweep)
    {
        string tuningHint = parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => "Low controls where distance starts reaching Perfect. Raise it when good takes are just missing Perfect; lower it when sloppy-but-close takes are too generous.",
            ScoringAdjustmentParameter.HighThreshold => "High controls where distance collapses to X. Raise it to forgive rough takes; lower it when wrong or weak takes need to fail sooner.",
            ScoringAdjustmentParameter.AutoCorrelationThreshold => "Autocorr controls when repeated shake-like motion is detected and capped. Lower values catch more takes; higher values let more takes through.",
            ScoringAdjustmentParameter.DirectionImpactFactor => "Direction impact scales the direction tendency returned by MoveSpace. Lower it to soften wrong-direction penalties; raise it to trust the direction signal more.",
            _ => "Use the sweep to compare loaded recordings across the selected range."
        };

        if (sweep == null)
            return tuningHint + " Waiting for the current diagram to finish calculating.";

        if (sweep.Series.Count == 0)
            return tuningHint + " No loaded recording has a scored instance for this move yet.";

        List<(double Value, float Average)> averages = BuildSweepAverages(sweep);
        if (averages.Count == 0)
            return tuningHint + " No range samples are available for this parameter yet.";

        float minAverage = averages.Min(static item => item.Average);
        float maxAverage = averages.Max(static item => item.Average);
        if (maxAverage - minAverage <= 0.1f)
            return tuningHint + " " + CreateFlatSweepGuidance(parameter);

        string rangeText = string.Format(
            CultureInfo.InvariantCulture,
            "Sampled average range: {0:0.0}% to {1:0.0}% ({2} spread vs saved average).",
            minAverage,
            maxAverage,
            FormatSignedPercent(maxAverage - minAverage));
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} Use the lines to pick the setting where good takes stay high and weak takes separate cleanly.",
            tuningHint,
            rangeText);
    }

    private string CreateFlatSweepGuidance(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.AutoCorrelationThreshold => IgnoreAutocorrelation
                ? "Flat line: Ignore autocorr is on, so this threshold is bypassed."
                : "Flat line: none of the loaded samples cross the shake detector differently inside this range.",
            ScoringAdjustmentParameter.DirectionImpactFactor => IgnoreDirection
                ? "Flat line: Ignore direction is on, so direction impact is bypassed."
                : "Flat line: the loaded samples have no effective direction tendency, or the final score is already capped/saturated.",
            ScoringAdjustmentParameter.LowThreshold => "Flat line: these samples are already saturated or already failing across the sampled low range. Add borderline takes if the move still feels wrong.",
            ScoringAdjustmentParameter.HighThreshold => "Flat line: these samples are already saturated or already failing across the sampled high range. Tune against borderline bad takes, not only clean examples.",
            _ => "Flat line: the selected setting does not change the MoveSpace score for the loaded samples."
        };

    private void UpdateAutoTuneRecommendationText()
    {
        if (_autoTuneRecommendation == null)
        {
            bool noSamples = IsMsmAvailable && _adjustmentCache != null && _adjustmentCache.ScoredMoveCount == 0;
            AutoTuneSummaryText = noSamples
                ? "No scored recording samples for this move yet."
                : IsBusy
                    ? "Analyzing loaded recordings for guided recommendations."
                    : "Load recordings to get guided shake and direction recommendations.";
            AutoTuneShakeRecommendationText = noSamples
                ? "Shake recommendation: add or select recordings containing this move."
                : "Shake recommendation: waiting for analysis.";
            AutoTuneDirectionRecommendationText = noSamples
                ? "Direction recommendation: add or select recordings containing this move."
                : "Direction recommendation: waiting for analysis.";
            AutoTuneDistanceRecommendationText = noSamples
                ? "Distance recommendation: add or select recordings containing this move."
                : "Distance recommendation: waiting for analysis.";
            ApplyAutoTuneCommand.NotifyCanExecuteChanged();
            return;
        }

        string sampleText = _autoTuneRecommendation.SampleCount == 1
            ? "1 scored sample"
            : _autoTuneRecommendation.SampleCount.ToString(CultureInfo.InvariantCulture) + " scored samples";
        AutoTuneSummaryText = "guided tuning ready from " + sampleText + ".";
        AutoTuneDistanceRecommendationText = string.Format(
            CultureInfo.InvariantCulture,
            "Distance recommendation: Perfect {0:0.###}, fail {1:0.###} from sample distances {2:0.###}/{3:0.###}/{4:0.###}.",
            _autoTuneRecommendation.LowThreshold,
            _autoTuneRecommendation.HighThreshold,
            _autoTuneRecommendation.DistanceMin,
            _autoTuneRecommendation.DistanceMedian,
            _autoTuneRecommendation.DistanceMax);
        AutoTuneShakeRecommendationText = string.Format(
            CultureInfo.InvariantCulture,
            "Shake sensitivity: {0} (threshold {1:0.###}, {2}/{3} trips at that value).",
            FormatPercentRatio(_autoTuneRecommendation.AutoCorrelationSensitivity),
            _autoTuneRecommendation.AutoCorrelationThreshold,
            _autoTuneRecommendation.AutoCorrelationOccurrenceCount,
            _autoTuneRecommendation.SampleCount);
        AutoTuneDirectionRecommendationText = string.Format(
            CultureInfo.InvariantCulture,
            "Direction sensitivity: {0} (negative direction sum {1:0.###}).",
            FormatPercentRatio(_autoTuneRecommendation.DirectionSensitivity),
            _autoTuneRecommendation.DirectionNegativeImpactSum);
        ApplyAutoTuneCommand.NotifyCanExecuteChanged();
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(ShowNoSelection));
        OnPropertyChanged(nameof(ShowMsmEditor));
        OnPropertyChanged(nameof(ShowGestureInfo));
        OnPropertyChanged(nameof(ShowMissingAsset));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(SelectedParameterTitle));
        OnPropertyChanged(nameof(SelectedParameterRangeText));
        OnPropertyChanged(nameof(ShowGuidedTuningPanel));
        OnPropertyChanged(nameof(ShowAdvancedTuningPanel));
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
        ApplyAutoTuneCommand.NotifyCanExecuteChanged();
    }

    private void CancelPreview()
    {
        lock (_previewGate)
        {
            _previewCts?.Cancel();
            _previewRequestId++;
            _isPreviewRunning = false;
            _previewPending = false;
        }
    }

    public void SelectParameter(string parameterName)
    {
        if (Enum.TryParse(parameterName, ignoreCase: true, out ScoringAdjustmentParameter parameter))
            SelectedParameter = parameter;
    }

    private void UpdateCurrentMarkerFromDraft()
    {
        CurrentParameterValue = GetParameterValue(SelectedParameter);
        CurrentParameterIsDefault = GetParameterIsDefault(SelectedParameter);
        UpdateLowHighSavedMarkers();

        if (_originalHeader == null)
        {
            SavedParameterValue = CurrentParameterValue;
            SavedParameterIsDefault = CurrentParameterIsDefault;
            return;
        }

        float savedHeaderValue = GetParameterHeaderValue(_originalHeader, SelectedParameter);
        SavedParameterIsDefault = IsDefaultHeaderValue(savedHeaderValue);
        SavedParameterValue = SavedParameterIsDefault
            ? GetDefaultValue(SelectedParameter)
            : Math.Clamp(savedHeaderValue, GetParameterRange(SelectedParameter).Min, GetParameterRange(SelectedParameter).Max);
    }

    private void UpdateLowHighSavedMarkers()
    {
        if (_originalHeader == null)
        {
            SavedLowThresholdIsDefault = LowThresholdDefault;
            SavedHighThresholdIsDefault = HighThresholdDefault;
            SavedLowThresholdValue = LowThreshold;
            SavedHighThresholdValue = HighThreshold;
            return;
        }

        SavedLowThresholdIsDefault = IsDefaultHeaderValue(_originalHeader.LowThreshold);
        SavedHighThresholdIsDefault = IsDefaultHeaderValue(_originalHeader.HighThreshold);
        SavedLowThresholdValue = SavedLowThresholdIsDefault
            ? GetDefaultValue(ScoringAdjustmentParameter.LowThreshold)
            : Math.Clamp(_originalHeader.LowThreshold, GetParameterRange(ScoringAdjustmentParameter.LowThreshold).Min, GetParameterRange(ScoringAdjustmentParameter.LowThreshold).Max);
        SavedHighThresholdValue = SavedHighThresholdIsDefault
            ? GetDefaultValue(ScoringAdjustmentParameter.HighThreshold)
            : Math.Clamp(_originalHeader.HighThreshold, GetParameterRange(ScoringAdjustmentParameter.HighThreshold).Min, GetParameterRange(ScoringAdjustmentParameter.HighThreshold).Max);
    }

    private double GetParameterValue(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => LowThreshold,
            ScoringAdjustmentParameter.HighThreshold => HighThreshold,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => AutoCorrelationThreshold,
            ScoringAdjustmentParameter.DirectionImpactFactor => DirectionImpactFactor,
            _ => LowThreshold
        };

    private bool GetParameterIsDefault(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => LowThresholdDefault,
            ScoringAdjustmentParameter.HighThreshold => HighThresholdDefault,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => AutoCorrelationThresholdDefault,
            ScoringAdjustmentParameter.DirectionImpactFactor => DirectionImpactFactorDefault,
            _ => false
        };

    private static float GetParameterHeaderValue(MotionClassifierHeader header, ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => header.LowThreshold,
            ScoringAdjustmentParameter.HighThreshold => header.HighThreshold,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => header.AutoCorrelationThreshold,
            ScoringAdjustmentParameter.DirectionImpactFactor => header.DirectionImpactFactor,
            _ => header.LowThreshold
        };

    private static ParameterRange GetParameterRange(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => new ParameterRange(0.4, 1.4),
            ScoringAdjustmentParameter.HighThreshold => new ParameterRange(1.5, 6.0),
            ScoringAdjustmentParameter.AutoCorrelationThreshold => new ParameterRange(0.5, 1.3),
            ScoringAdjustmentParameter.DirectionImpactFactor => new ParameterRange(0.0, 1.0),
            _ => new ParameterRange(0.4, 1.4)
        };

    private static double GetDefaultValue(ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => 1.0,
            ScoringAdjustmentParameter.HighThreshold => 3.0,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => 0.7,
            ScoringAdjustmentParameter.DirectionImpactFactor => 1.0,
            _ => 1.0
        };

    private static double AutoCorrelationThresholdToSensitivity(double threshold)
    {
        ParameterRange range = GetParameterRange(ScoringAdjustmentParameter.AutoCorrelationThreshold);
        double clamped = Math.Clamp(threshold, range.Min, range.Max);
        return ClampUnit((range.Max - clamped) / (range.Max - range.Min));
    }

    private static double AutoCorrelationSensitivityToThreshold(double sensitivity)
    {
        ParameterRange range = GetParameterRange(ScoringAdjustmentParameter.AutoCorrelationThreshold);
        double ratio = ClampUnit(sensitivity);
        return range.Max + (ratio * (range.Min - range.Max));
    }

    private static double ClampUnit(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.0;

        return Math.Clamp(value, 0.0, 1.0);
    }

    private static string FormatPercentRatio(double value)
        => (ClampUnit(value) * 100.0).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static double ResolveDraftSliderValue(float headerValue, ScoringAdjustmentParameter parameter)
        => IsDefaultHeaderValue(headerValue)
            ? GetDefaultValue(parameter)
            : Math.Clamp(headerValue, GetParameterRange(parameter).Min, GetParameterRange(parameter).Max);

    private static bool IsDefaultHeaderValue(float value)
        => Math.Abs(value - -1.0f) < 0.000001f;

    private static IEnumerable<SweepCandidate> GetSweepCandidates(ScoringAdjustmentParameter parameter)
    {
        yield return new SweepCandidate(GetDefaultValue(parameter), IsDefault: true);

        ParameterRange range = GetParameterRange(parameter);
        for (int i = 0; i < SweepSampleCount; i++)
        {
            double value = range.Min + ((range.Max - range.Min) * i / (SweepSampleCount - 1));
            yield return new SweepCandidate(value, IsDefault: false);
        }
    }

    private static double ResolveEffectiveDraftValue(ScoringAdjustmentDraft draft, ScoringAdjustmentParameter parameter)
        => parameter switch
        {
            ScoringAdjustmentParameter.LowThreshold => draft.LowThresholdDefault ? GetDefaultValue(parameter) : draft.LowThreshold,
            ScoringAdjustmentParameter.HighThreshold => draft.HighThresholdDefault ? GetDefaultValue(parameter) : draft.HighThreshold,
            ScoringAdjustmentParameter.AutoCorrelationThreshold => draft.AutoCorrelationThresholdDefault ? GetDefaultValue(parameter) : draft.AutoCorrelationThreshold,
            ScoringAdjustmentParameter.DirectionImpactFactor => draft.DirectionImpactFactorDefault ? GetDefaultValue(parameter) : draft.DirectionImpactFactor,
            _ => GetDefaultValue(parameter)
        };

    private static float GetPreviewAccuracy(MotionRecordingMoveScorePoint point)
        => string.IsNullOrWhiteSpace(point.Issue)
            ? JdiMotionRecordingScoreMath.NormalizePercentage(point.AdjustedPercentageScore)
            : 0.0f;

    private static ScoringAdjustmentCurve CreateAdjustmentCurve(
        CachedAdjustmentCache? adjustmentCache,
        ScoringAdjustmentDraft draft)
    {
        List<ScoringAdjustmentCurvePoint> points = new(AdjustmentCurvePiecewisePointCount);
        double low = ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.LowThreshold);
        double high = ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.HighThreshold);
        points.Add(new ScoringAdjustmentCurvePoint(0.0f, ProjectDistanceToMoveSpacePercentage(0.0f, draft)));
        points.Add(new ScoringAdjustmentCurvePoint((float)low, ProjectDistanceToMoveSpacePercentage((float)low, draft)));
        points.Add(new ScoringAdjustmentCurvePoint((float)high, ProjectDistanceToMoveSpacePercentage((float)high, draft)));

        List<ScoringAdjustmentCurveSample> samples = [];
        int seriesIndex = 0;
        foreach (AdjustmentSeriesInfo info in (adjustmentCache?.SeriesInfos ?? [])
            .OrderBy(static info => info.RecordingIndex)
            .ThenBy(static info => info.CoachId)
            .ThenBy(static info => info.MoveIndex))
        {
            float statisticalDistance = info.MoveSpace.StatisticalDistance;
            float percentageScore = ProjectDistanceToMoveSpacePercentage(statisticalDistance, draft);
            samples.Add(new ScoringAdjustmentCurveSample(
                info.Label,
                seriesIndex++,
                info.RecordingIndex,
                info.CoachId,
                info.MoveIndex,
                statisticalDistance,
                percentageScore));
        }

        return new ScoringAdjustmentCurve(
            points,
            samples,
            points.Count,
            adjustmentCache?.RecordingCount ?? 0,
            adjustmentCache?.MoveInstanceCount ?? 0,
            adjustmentCache?.ScoredMoveCount ?? 0);
    }

    private static float ProjectDistanceToMoveSpacePercentage(
        float statisticalDistance,
        ScoringAdjustmentDraft draft)
    {
        double currentLow = ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.LowThreshold);
        double currentHigh = ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.HighThreshold);
        float currentRatio = MoveSpaceScorer.GetRatioScoreFromStatisticalDistance(
            statisticalDistance,
            (float)currentLow,
            (float)currentHigh);
        return Math.Clamp(currentRatio * 100.0f, 0.0f, 100.0f);
    }

    private static float ProjectMoveSpacePercentage(
        AdjustmentSeriesInfo info,
        double lowThreshold,
        double highThreshold)
        => ProjectMoveSpacePercentage(info.MoveSpace, lowThreshold, highThreshold);

    private static float ProjectPreviewPercentage(
        AdjustmentSeriesInfo info,
        double lowThreshold,
        double highThreshold,
        ScoringAdjustmentDraft draft,
        MotionRecordingScoringProfile scoringProfile)
    {
        MoveSpaceScoreResult projected = ProjectMoveSpaceResult(info.MoveSpace, lowThreshold, highThreshold, draft);
        return JdiMotionRecordingScoreMath.GetProfilePercentage(projected, scoringProfile);
    }

    private static float ProjectMoveSpacePercentage(MoveSpaceScoreResult moveSpace, double lowThreshold, double highThreshold)
    {
        float ratio = MoveSpaceScorer.GetRatioScoreFromStatisticalDistance(
            moveSpace.StatisticalDistance,
            (float)lowThreshold,
            (float)highThreshold);
        return Math.Clamp(100.0f * ratio, 0.0f, 100.0f);
    }

    private static MoveSpaceScoreResult ProjectMoveSpaceResult(
        MoveSpaceScoreResult moveSpace,
        double lowThreshold,
        double highThreshold,
        ScoringAdjustmentDraft draft)
    {
        float low = (float)lowThreshold;
        float high = (float)highThreshold;
        float ratio = MoveSpaceScorer.GetRatioScoreFromStatisticalDistance(moveSpace.StatisticalDistance, low, high);
        bool directionIgnored = moveSpace.DirectionTendencyIgnored || draft.IgnoreDirection;
        float directionImpact = directionIgnored
            ? 0.0f
            : SanitizeFinite(moveSpace.DirectionTendencyImpactOnScoreRatio, 0.0f)
                * (float)ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor);
        float autoCorrelationTime = draft.IgnoreAutocorrelation ? -6.0f : moveSpace.AutoCorrelationTime;

        return moveSpace with
        {
            RatioScore = ratio,
            PercentageScore = ratio * 100.0f,
            LowThreshold = low,
            HighThreshold = high,
            AutoCorrelationThreshold = (float)ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.AutoCorrelationThreshold),
            DirectionImpactFactor = (float)ResolveEffectiveDraftValue(draft, ScoringAdjustmentParameter.DirectionImpactFactor),
            DirectionTendencyIgnored = directionIgnored,
            DirectionTendencyImpactOnScoreRatio = directionImpact,
            AutoCorrelationTime = autoCorrelationTime
        };
    }

    private static List<(double Value, float Average)> BuildSweepAverages(CachedSweep sweep)
    {
        Dictionary<double, List<float>> valuesByCandidate = [];
        foreach (ScoringAdjustmentSweepSeries series in sweep.Series)
        {
            foreach (ScoringAdjustmentSweepPoint point in series.RangePoints)
            {
                double key = Math.Round(point.Value, 6);
                if (!valuesByCandidate.TryGetValue(key, out List<float>? values))
                {
                    values = [];
                    valuesByCandidate[key] = values;
                }

                values.Add(point.Accuracy);
            }
        }

        return [.. valuesByCandidate
            .Select(static item => (Value: item.Key, Average: item.Value.Count == 0 ? 0.0f : item.Value.Average()))
            .OrderBy(static item => item.Value)];
    }

    private static string FormatSignedPercent(float value)
        => (value >= 0.0f ? "+" : string.Empty) + value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static float SanitizeFinite(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    private static float EstimateAverageAt(
        IReadOnlyList<ScoringAdjustmentSweepSeries> series,
        double value,
        bool isDefault)
    {
        List<float> values = [];
        foreach (ScoringAdjustmentSweepSeries item in series)
        {
            ScoringAdjustmentSweepPoint? point = isDefault
                ? item.DefaultPoint
                : EstimatePointAt(item.RangePoints, value);
            if (point != null)
                values.Add(point.Accuracy);
        }

        return values.Count == 0 ? 0.0f : values.Average();
    }

    private static ScoringAdjustmentSweepPoint? EstimatePointAt(IReadOnlyList<ScoringAdjustmentSweepPoint> points, double value)
    {
        if (points.Count == 0)
            return null;

        ScoringAdjustmentSweepPoint lower = points[0];
        ScoringAdjustmentSweepPoint upper = points[^1];
        foreach (ScoringAdjustmentSweepPoint point in points)
        {
            if (point.Value <= value)
                lower = point;
            if (point.Value >= value)
            {
                upper = point;
                break;
            }
        }

        if (Math.Abs(upper.Value - lower.Value) < 0.000001)
            return lower;

        double ratio = (value - lower.Value) / (upper.Value - lower.Value);
        return new ScoringAdjustmentSweepPoint(
            value,
            (float)(lower.Accuracy + ((upper.Accuracy - lower.Accuracy) * ratio)),
            IsDefaultValue: false);
    }

    private SweepCacheKey CreateCacheKey(ScoringAdjustmentParameter parameter, ScoringAdjustmentDraft draft, int assetVersion)
        => new(
            assetVersion,
            parameter == ScoringAdjustmentParameter.LowThreshold ? 0.0 : draft.LowThreshold,
            parameter == ScoringAdjustmentParameter.LowThreshold ? false : draft.LowThresholdDefault,
            parameter == ScoringAdjustmentParameter.HighThreshold ? 0.0 : draft.HighThreshold,
            parameter == ScoringAdjustmentParameter.HighThreshold ? false : draft.HighThresholdDefault,
            parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold ? 0.0 : draft.AutoCorrelationThreshold,
            parameter == ScoringAdjustmentParameter.AutoCorrelationThreshold ? false : draft.AutoCorrelationThresholdDefault,
            parameter == ScoringAdjustmentParameter.DirectionImpactFactor ? 0.0 : draft.DirectionImpactFactor,
            parameter == ScoringAdjustmentParameter.DirectionImpactFactor ? false : draft.DirectionImpactFactorDefault,
            draft.IgnoreDirection,
            draft.IgnoreAutocorrelation);

    private AdjustmentCacheKey CreateAdjustmentCacheKey(ScoringAdjustmentDraft draft, int assetVersion)
        => new(
            assetVersion,
            draft.AutoCorrelationThreshold,
            draft.AutoCorrelationThresholdDefault,
            draft.IgnoreAutocorrelation);

    private static uint BuildCustomizationBitField(ScoringAdjustmentDraft draft)
    {
        uint bitfield = 0;
        if (draft.IgnoreDirection)
            bitfield |= IgnoreDirectionFlag;
        if (draft.IgnoreAutocorrelation)
            bitfield |= IgnoreAutoCorrelationFlag;
        return bitfield;
    }

    private static string CreateSeriesLabel(MotionRecordingMoveScorePoint point)
        => string.Format(
            CultureInfo.InvariantCulture,
            "Recording {0}, coach {1}, move {2}, beat {3:0.##}",
            point.RecordingIndex + 1,
            point.CoachId,
            point.MoveIndex,
            point.StartBeatLabel);

    private void InvalidateSweepCache()
    {
        _assetVersion++;
        _sweepCache.Clear();
        _adjustmentCache = null;
        SweepSeries = [];
        AdjustmentCurve = null;
    }

    private void ResetPreviewText()
    {
        SweepSeries = [];
        AdjustmentCurve = null;
        AverageAccuracyText = "-";
        AverageDeltaText = "-";
        PointCountText = "-";
        SelectedParameterGuidanceText = CreateParameterGuidance(SelectedParameter, null);
        UpdateAutoTuneRecommendationText();
    }

    public void Dispose()
    {
        if (TimelineContext != null)
            TimelineContext.PropertyChanged -= TimelineContext_PropertyChanged;

        lock (_previewGate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _previewCts?.Cancel();
        }

        GC.SuppressFinalize(this);
    }

    private sealed record LoadedRecording(string Path, MotionRecordingDocument Document);

    private sealed record ScoringAdjustmentDraft(
        double LowThreshold,
        bool LowThresholdDefault,
        double HighThreshold,
        bool HighThresholdDefault,
        double AutoCorrelationThreshold,
        bool AutoCorrelationThresholdDefault,
        double DirectionImpactFactor,
        bool DirectionImpactFactorDefault,
        bool IgnoreDirection,
        bool IgnoreAutocorrelation);

    private sealed record SweepCacheKey(
        int AssetVersion,
        double LowThreshold,
        bool LowThresholdDefault,
        double HighThreshold,
        bool HighThresholdDefault,
        double AutoCorrelationThreshold,
        bool AutoCorrelationThresholdDefault,
        double DirectionImpactFactor,
        bool DirectionImpactFactorDefault,
        bool IgnoreDirection,
        bool IgnoreAutocorrelation);

    private sealed record AdjustmentCacheKey(
        int AssetVersion,
        double AutoCorrelationThreshold,
        bool AutoCorrelationThresholdDefault,
        bool IgnoreAutocorrelation);

    private sealed record CachedSweep(
        ScoringAdjustmentParameter Parameter,
        SweepCacheKey CacheKey,
        IReadOnlyList<ScoringAdjustmentSweepSeries> Series,
        int CandidateCount,
        int RecordingCount,
        int MoveInstanceCount,
        int ScoredMoveCount,
        float SavedAverageAccuracy,
        IReadOnlyDictionary<double, int> AutoCorrelationOccurrenceCounts);

    private sealed record CachedAdjustmentCache(
        AdjustmentCacheKey CacheKey,
        IReadOnlyList<AdjustmentSeriesInfo> SeriesInfos,
        int RecordingCount,
        int MoveInstanceCount,
        int ScoredMoveCount);

    private sealed record PreviewResult(
        int AssetVersion,
        int RecordingCount,
        IReadOnlyList<CachedSweep> Sweeps,
        CachedAdjustmentCache AdjustmentCache,
        AutoTuneRecommendation? AutoTuneRecommendation);

    private sealed record SweepCandidate(double Value, bool IsDefault);

    private sealed record ParameterRange(double Min, double Max);

    private sealed record AdjustmentSeriesInfo(
        MoveScorePointKey Key,
        string Label,
        int RecordingIndex,
        int CoachId,
        int MoveIndex,
        float SavedPercentageScore,
        MoveSpaceScoreResult MoveSpace);

    private sealed record AutoTuneRecommendation(
        double LowThreshold,
        double HighThreshold,
        double DistanceMin,
        double DistanceMedian,
        double DistanceMax,
        double AutoCorrelationThreshold,
        double AutoCorrelationSensitivity,
        int AutoCorrelationOccurrenceCount,
        double AutoCorrelationOccurrenceProportion,
        double DirectionSensitivity,
        double DirectionNegativeImpactSum,
        int SampleCount);

    private sealed record MoveScorePointKey(int RecordingIndex, int CoachId, int MoveIndex)
    {
        public static MoveScorePointKey From(MotionRecordingMoveScorePoint point)
            => new(point.RecordingIndex, point.CoachId, point.MoveIndex);
    }

    private sealed class SeriesBuilder(
        string label,
        int recordingIndex,
        int coachId,
        int moveIndex,
        float savedAccuracy)
    {
        public string Label { get; } = label;
        public int RecordingIndex { get; } = recordingIndex;
        public int CoachId { get; } = coachId;
        public int MoveIndex { get; } = moveIndex;
        public float SavedAccuracy { get; } = savedAccuracy;
        public List<ScoringAdjustmentSweepPoint> Points { get; } = [];
    }
}
