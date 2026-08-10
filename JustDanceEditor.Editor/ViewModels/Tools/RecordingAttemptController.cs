using Avalonia.Threading;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Services.Motion;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Scoring;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingAttemptController(
    RecordingsToolViewModel owner,
    IMotionInputClient motionClient,
    IMotionRecordingRepository recordingRepository,
    RecordingLibraryService recordingLibrary,
    JdiMotionRecordingLiveScorer liveScorer,
    RecordingLiveScoreDisplayController liveScoreDisplay,
    EditorSettingsService settings)
{
    private readonly RecordingSampleClock _sampleClock = new();
    private readonly Lock _recordingGate = new();
    private readonly SemaphoreSlim _liveScoringSemaphore = new(1, 1);

    private MotionRecordingDocument? _currentRecording;
    private MotionRecordingLiveScoreSession? _liveScoreSession;
    private CancellationTokenSource? _recordingCts;
    private int _scoringRunId;
    private bool _isAutoStopping;
    private bool _discardPromptOpen;

    public void AttachTimeline(TimelineEditorViewModel timeline)
    {
        timeline.Playback.InteractionBlocked += Playback_InteractionBlocked;
    }

    public void DetachTimeline(TimelineEditorViewModel timeline)
    {
        timeline.Playback.InteractionBlocked -= Playback_InteractionBlocked;
        timeline.Playback.IsInteractionLocked = false;
    }

    public void HandleTimeChanged()
    {
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        if (!owner.IsRecording || _isAutoStopping || timeline == null)
            return;

        TimeSpan currentTime = timeline.Playback.CurrentTime;
        QueueLiveScoring(currentTime.TotalSeconds);

        TimeSpan duration = timeline.Playback.Duration;
        if (duration <= TimeSpan.Zero)
            return;

        if (currentTime < duration - TimeSpan.FromMilliseconds(50))
            return;

        _isAutoStopping = true;
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await owner.StopAndSaveRecordingAttemptAsync();
            }
            finally
            {
                _isAutoStopping = false;
            }
        });
    }

    public async Task StartAsync()
    {
        TimelineEditorViewModel timeline = owner.ActiveTimeline ?? throw new InvalidOperationException("No active timeline.");
        MotionDeviceInfo device = owner.SelectedDevice ?? throw new InvalidOperationException("No DSU device selected.");

        timeline.Playback.Pause();
        timeline.Playback.Seek(TimeSpan.Zero);

        owner.SampleCount = 0;
        owner.GeneratedClassifierCount = 0;
        _sampleClock.Reset();
        liveScoreDisplay.Clear(owner, clearTotal: true);
        liveScoreDisplay.IsActive = true;
        _scoringRunId++;

        MotionRecordingDocument[] previousRecordings = await recordingLibrary.LoadCoachRecordingsAsync(timeline, owner.SelectedCoachId);
        _liveScoreSession = await liveScorer.CreateSessionAsync(
            timeline.RootPath,
            timeline.Package,
            owner.SelectedCoachId,
            previousRecordings,
            CreateLiveScoringOptions());
        _recordingCts = new CancellationTokenSource();

        MotionRecordingDocument recording = new()
        {
            CoachId = owner.SelectedCoachId,
            DeviceId = device.MacAddress,
            DeviceName = device.DisplayName,
            SongId = timeline.Package.Metadata.SongID.ToString("D"),
            MapName = timeline.Package.Metadata.MapName,
            StartedAtUtc = DateTimeOffset.UtcNow,
            TimelineStartSeconds = timeline.Playback.CurrentTime.TotalSeconds
        };

        lock (_recordingGate)
            _currentRecording = recording;

        motionClient.SampleReceived += MotionClient_SampleReceived;
        owner.IsRecording = true;
        timeline.Playback.IsInteractionLocked = true;
        owner.StatusText = BuildRecordingStatus();

        try
        {
            await motionClient.StartStreamingAsync(device.Id, _recordingCts.Token);
            timeline.Playback.Play();
        }
        catch
        {
            motionClient.SampleReceived -= MotionClient_SampleReceived;
            owner.IsRecording = false;
            timeline.Playback.IsInteractionLocked = false;
            _liveScoreSession = null;
            _scoringRunId++;
            _sampleClock.Reset();
            liveScoreDisplay.Clear(owner, clearTotal: true);
            liveScoreDisplay.IsActive = false;
            lock (_recordingGate)
                _currentRecording = null;
            throw;
        }
    }

    public async Task<string?> StopAndSaveAsync()
    {
        TimelineEditorViewModel timeline = owner.ActiveTimeline ?? throw new InvalidOperationException("No active timeline.");
        double endSeconds = timeline.Playback.CurrentTime.TotalSeconds;
        await ScoreCompletedMovesAsync(endSeconds, waitForTurn: true);
        MotionRecordingDocument? recording = DetachCurrentRecording(timeline, endSeconds);
        liveScoreDisplay.Clear(owner, clearTotal: recording == null || recording.Samples.Count == 0);
        liveScoreDisplay.IsActive = false;

        await motionClient.StopStreamingAsync();
        timeline.Playback.Pause();

        if (recording == null || recording.Samples.Count == 0)
        {
            owner.StatusText = "No samples captured";
            return null;
        }

        string path = await recordingRepository.SaveAsync(timeline.RootPath, recording);
        owner.GeneratedClassifierCount = 0;
        owner.StatusText = $"Saved {Path.GetFileName(path)}";
        owner.IsRecordingSetupVisible = false;
        return path;
    }

    public async Task CancelAsync()
    {
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        DetachCurrentRecording(timeline, timeline?.Playback.CurrentTime.TotalSeconds ?? 0);

        await motionClient.StopStreamingAsync();
        timeline?.Playback.Pause();
        owner.SampleCount = 0;
        owner.GeneratedClassifierCount = 0;
        liveScoreDisplay.Clear(owner, clearTotal: true);
        liveScoreDisplay.IsActive = false;
        owner.IsRecordingSetupVisible = false;
        owner.StatusText = "Attempt discarded";
    }

    public async ValueTask DisposeAsync()
    {
        _liveScoreSession = null;
        _scoringRunId++;
        liveScoreDisplay.Clear(owner, clearTotal: false);
        liveScoreDisplay.IsActive = false;

        if (owner.ActiveTimeline != null)
            DetachTimeline(owner.ActiveTimeline);

        motionClient.SampleReceived -= MotionClient_SampleReceived;
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingCts = null;

        await motionClient.StopStreamingAsync();
    }

    private MotionRecordingLiveScoringOptions CreateLiveScoringOptions()
    {
        MotionRecordingScoringProfile profile = settings.ScoringProfile;
        return new MotionRecordingLiveScoringOptions
        {
            ClassifierSource = owner.ScoreAgainstExistingClassifiers
                ? MotionRecordingClassifierSource.ExistingFiles
                : MotionRecordingClassifierSource.CurrentAndPreviousRecordings,
            ScoringProfile = profile,
            GoldMoveValue = MotionRecordingScoreMath.GetDefaultGoldMoveValue(profile)
        };
    }

    private string BuildRecordingStatus()
    {
        MotionRecordingLiveScoreSession? session = _liveScoreSession;
        string mode = owner.ScoreAgainstExistingClassifiers ? "existing MSM" : "live MSM";
        if (session == null || session.InitializationIssues.Count == 0)
            return $"Recording ({mode})";

        return $"Recording ({mode}, {session.InitializationIssues.Count} scoring issue(s))";
    }

    private MotionRecordingDocument? DetachCurrentRecording(TimelineEditorViewModel? timeline, double endSeconds)
    {
        motionClient.SampleReceived -= MotionClient_SampleReceived;
        _recordingCts?.Cancel();
        _recordingCts?.Dispose();
        _recordingCts = null;
        owner.IsRecording = false;
        _liveScoreSession = null;
        _scoringRunId++;
        _sampleClock.Reset();
        timeline?.Playback.IsInteractionLocked = false;

        lock (_recordingGate)
        {
            MotionRecordingDocument? recording = _currentRecording;
            _currentRecording = null;
            recording?.TimelineEndSeconds = endSeconds;
            return recording;
        }
    }

    private void QueueLiveScoring(double currentTimeSeconds)
    {
        if (_liveScoreSession == null)
            return;

        _ = ScoreCompletedMovesAsync(currentTimeSeconds, waitForTurn: false);
    }

    private async Task ScoreCompletedMovesAsync(double currentTimeSeconds, bool waitForTurn)
    {
        MotionRecordingLiveScoreSession? session = _liveScoreSession;
        if (session == null)
            return;

        int runId = _scoringRunId;
        bool entered = waitForTurn
            ? await WaitForLiveScoringTurnAsync()
            : _liveScoringSemaphore.Wait(0);
        if (!entered)
            return;

        try
        {
            session = _liveScoreSession;
            if (session == null || runId != _scoringRunId)
                return;

            MotionRecordingDocument? snapshot;
            lock (_recordingGate)
            {
                snapshot = _currentRecording == null
                    ? null
                    : RecordingAttemptDocuments.CreateSnapshot(_currentRecording, currentTimeSeconds);
            }

            if (snapshot == null)
                return;

            double scoringTimeSeconds = GetLiveScoringEndSeconds(snapshot, currentTimeSeconds);
            IReadOnlyList<MotionRecordingLiveScore> scores = await Task.Run(
                () => session.ScoreCompletedMoves(snapshot, scoringTimeSeconds));

            if (runId != _scoringRunId)
                return;

            await Dispatcher.UIThread.InvokeAsync(() => liveScoreDisplay.ApplyScores(owner, scores, session.TotalScore));
        }
        catch (Exception ex)
        {
            if (runId == _scoringRunId)
                await Dispatcher.UIThread.InvokeAsync(() => owner.StatusText = ex.Message);
        }
        finally
        {
            _liveScoringSemaphore.Release();
        }
    }

    private async Task<bool> WaitForLiveScoringTurnAsync()
    {
        await _liveScoringSemaphore.WaitAsync();
        return true;
    }

    internal static double GetLiveScoringEndSeconds(MotionRecordingDocument recording, double playbackTimeSeconds)
    {
        if (recording.Samples.Count == 0)
            return recording.TimelineStartSeconds;

        double capturedThroughSeconds = double.NegativeInfinity;
        foreach (RecordedMotionSample sample in recording.Samples)
            capturedThroughSeconds = Math.Max(capturedThroughSeconds, sample.MapTime);
        if (double.IsNaN(capturedThroughSeconds) || double.IsInfinity(capturedThroughSeconds))
            return recording.TimelineStartSeconds;

        return Math.Min(playbackTimeSeconds, Math.Max(recording.TimelineStartSeconds, capturedThroughSeconds));
    }

    private void MotionClient_SampleReceived(object? sender, MotionSensorSampleEventArgs e)
    {
        TimelineEditorViewModel? timeline = owner.ActiveTimeline;
        MotionRecordingDocument? recording;
        int count;

        lock (_recordingGate)
        {
            recording = _currentRecording;
            if (recording == null || timeline == null)
                return;

            recording.Samples.Add(new RecordedMotionSample
            {
                MapTime = _sampleClock.GetMapTimeSeconds(timeline, e.Sample),
                AccX = e.Sample.AccX,
                AccY = e.Sample.AccY,
                AccZ = e.Sample.AccZ,
                GyroX = e.Sample.GyroX,
                GyroY = e.Sample.GyroY,
                GyroZ = e.Sample.GyroZ,
                SensorTimestampMicroseconds = e.Sample.SensorTimestampMicroseconds
            });
            count = recording.Samples.Count;
        }

        Dispatcher.UIThread.Post(() => owner.SampleCount = count, DispatcherPriority.Background);
    }

    private void Playback_InteractionBlocked(object? sender, PlaybackInteractionBlockedEventArgs e)
    {
        if (!owner.IsRecording || _discardPromptOpen)
            return;

        _ = Dispatcher.UIThread.InvokeAsync(() => PromptDiscardForPlaybackInteractionAsync(e));
    }

    private async Task PromptDiscardForPlaybackInteractionAsync(PlaybackInteractionBlockedEventArgs e)
    {
        if (!owner.IsRecording || _discardPromptOpen)
            return;

        _discardPromptOpen = true;
        try
        {
            bool discard = await RecordingsToolDialogs.ShowDiscardPromptAsync(owner.DialogOwner);
            if (!discard || !owner.IsRecording)
                return;

            await owner.CancelRecordingAttemptAsync();

            if (e.Kind == PlaybackInteractionKind.Seek && e.TargetTime.HasValue && owner.ActiveTimeline != null)
                owner.ActiveTimeline.Playback.Seek(e.TargetTime.Value);
        }
        finally
        {
            _discardPromptOpen = false;
        }
    }
}