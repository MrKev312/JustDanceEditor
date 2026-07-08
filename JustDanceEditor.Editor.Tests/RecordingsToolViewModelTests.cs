using Avalonia.Headless.XUnit;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Services.Motion;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Formats.JDI.Recordings;

namespace JustDanceEditor.Editor.Tests;

public sealed class RecordingsToolViewModelTests
{
    [Fact]
    public async Task ActiveTimeline_SetAfterConstruction_AttachesWithoutCrash()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        await using RecordingsToolViewModel tool = new();
        try
        {
            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;
            timeline = new TimelineEditorViewModel(package, root, new PlaybackService(), new TimelineSettingsService());

            tool.ActiveTimeline = timeline;

            Assert.Same(timeline, tool.ActiveTimeline);
            Assert.Contains(0, tool.CoachIds);
        }
        finally
        {
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task StopAndSave_DeactivatesGameplayScoreHud()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);

        TimelineEditorViewModel? timeline = null;
        await using RecordingsToolViewModel tool = new();
        FakeMotionInputClient motionClient = new();
        JsonMotionRecordingRepository repository = new();
        MotionRecordingScoreHudService scoreHud = new();
        RecordingAttemptController controller = new(
            tool,
            motionClient,
            repository,
            new RecordingLibraryService(repository),
            new JdiMotionRecordingLiveScorer(),
            new RecordingLiveScoreDisplayController(scoreHud));

        try
        {
            IntermediateSongPackage package = new();
            package.Metadata.CoachCount = 1;

            FakePlaybackService playback = new()
            {
                DurationValue = TimeSpan.FromSeconds(1)
            };
            timeline = new TimelineEditorViewModel(package, root, playback, new TimelineSettingsService());

            tool.ActiveTimeline = timeline;
            tool.SelectedDevice = motionClient.Devices[0];
            tool.SelectedCoachId = 0;

            await controller.StartAsync();
            Assert.True(scoreHud.IsActive);

            playback.CurrentTimeValue = TimeSpan.FromSeconds(1);
            string? path = await controller.StopAndSaveAsync();

            Assert.NotNull(path);
            Assert.False(scoreHud.IsActive);
        }
        finally
        {
            await controller.DisposeAsync();
            timeline?.Playback.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GetLiveScoringEndSeconds_WhenSamplesLagPlayback_UsesCapturedSampleTime()
    {
        MotionRecordingDocument recording = new()
        {
            TimelineStartSeconds = 0
        };
        recording.Samples.Add(new RecordedMotionSample { MapTime = 1.25 });
        recording.Samples.Add(new RecordedMotionSample { MapTime = 1.5 });

        double scoringEnd = RecordingAttemptController.GetLiveScoringEndSeconds(recording, playbackTimeSeconds: 2.0);

        Assert.Equal(1.5, scoringEnd);
    }

    [Fact]
    public void GetLiveScoringEndSeconds_WhenSamplesLeadPlayback_UsesPlaybackTime()
    {
        MotionRecordingDocument recording = new()
        {
            TimelineStartSeconds = 0
        };
        recording.Samples.Add(new RecordedMotionSample { MapTime = 2.25 });

        double scoringEnd = RecordingAttemptController.GetLiveScoringEndSeconds(recording, playbackTimeSeconds: 2.0);

        Assert.Equal(2.0, scoringEnd);
    }

    private sealed class FakeMotionInputClient : IMotionInputClient
    {
        public bool IsConnected => true;
        public bool IsStreaming { get; private set; }
        public IReadOnlyList<MotionDeviceInfo> Devices { get; } =
        [
            new(new MotionDeviceId(0), "Test DSU Slot 0", true, 100, "00:11:22:33:44:55")
        ];

        public event EventHandler? DevicesChanged;
        public event EventHandler<MotionSensorSampleEventArgs>? SampleReceived;

        public Task ConnectAsync(MotionInputEndpoint endpoint, CancellationToken cancellationToken = default)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsStreaming = false;
            return Task.CompletedTask;
        }

        public Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task StartStreamingAsync(MotionDeviceId deviceId, CancellationToken cancellationToken = default)
        {
            IsStreaming = true;
            SampleReceived?.Invoke(this, new MotionSensorSampleEventArgs(new MotionSensorSample(
                deviceId,
                1_000_000,
                0.1f,
                0.2f,
                1.0f,
                0.0f,
                0.0f,
                0.0f,
                DateTimeOffset.UtcNow)));
            return Task.CompletedTask;
        }

        public Task StopStreamingAsync()
        {
            IsStreaming = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsStreaming = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePlaybackService : IPlaybackService
    {
        public bool IsPlaying { get; private set; }
        public TimeSpan CurrentTime => CurrentTimeValue;
        public TimeSpan CurrentTimeValue { get; set; }
        public TimeSpan Duration => DurationValue;
        public TimeSpan DurationValue { get; set; }
        public double CurrentBeat => CurrentTime.TotalSeconds * 2.0;
        public bool IsInteractionLocked { get; set; }
        public bool IsMetronomeEnabled { get; set; }

        public event EventHandler? TimeChanged;
        public event EventHandler? PlayStateChanged;
        public event EventHandler<PlaybackInteractionBlockedEventArgs>? InteractionBlocked;

        public Task LoadMediaAsync(
            PcmWaveAudioData? audio,
            Func<double, double> beatToSeconds,
            Func<double, double> secondsToBeat)
            => Task.CompletedTask;

        public void Play()
        {
            IsPlaying = true;
            PlayStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            if (IsInteractionLocked)
            {
                InteractionBlocked?.Invoke(this, new PlaybackInteractionBlockedEventArgs(PlaybackInteractionKind.Pause));
                return;
            }

            IsPlaying = false;
            PlayStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Seek(TimeSpan time)
        {
            if (IsInteractionLocked)
            {
                InteractionBlocked?.Invoke(this, new PlaybackInteractionBlockedEventArgs(PlaybackInteractionKind.Seek, time));
                return;
            }

            CurrentTimeValue = time;
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SeekToBeat(double beat)
        {
            Seek(TimeSpan.FromSeconds(beat * 0.5));
        }

        public void UpdateMetronome(
            double zeroBeatTimeSeconds,
            double bpm,
            int beatsPerMeasure,
            IEnumerable<double>? sectionStarts = null)
        {
        }

        public void SetExtendedEnd(TimeSpan end)
        {
            DurationValue = end;
        }

        public void Dispose()
        {
        }
    }
}
