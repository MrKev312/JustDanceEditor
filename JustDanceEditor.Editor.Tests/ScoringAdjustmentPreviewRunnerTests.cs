using Avalonia.Headless.XUnit;

using JustDanceEditor.Editor.ViewModels.Tools;
using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Scoring;

namespace JustDanceEditor.Editor.Tests;

public sealed class ScoringAdjustmentPreviewRunnerTests
{
    [AvaloniaFact]
    public async Task CompletedPreview_CanBeImmediatelyReplacedByNextSliderUpdate()
    {
        using ScoringAdjustmentPreviewRunner runner = new(new ImmediateAnalyzer());

        await RunPreviewAsync(runner, CreateDraft(3.0));
        Exception? exception = await Record.ExceptionAsync(
            () => RunPreviewAsync(runner, CreateDraft(3.1)));

        Assert.Null(exception);
    }

    [AvaloniaFact]
    public async Task InFlightPreview_IsNotCanceledOrOverlappedByNextSliderUpdate()
    {
        BlockingAnalyzer analyzer = new();
        using ScoringAdjustmentPreviewRunner runner = new(analyzer);

        Task first = RunPreviewAsync(runner, CreateDraft(3.0));
        await analyzer.FirstStarted;
        Task second = RunPreviewAsync(runner, CreateDraft(3.1));

        bool firstWasCanceled = analyzer.FirstToken.IsCancellationRequested;
        int callsWhileFirstWasBlocked = analyzer.CallCount;
        int concurrencyWhileFirstWasBlocked = analyzer.MaxConcurrency;

        analyzer.ReleaseFirst();
        await Task.WhenAll(first, second);

        Assert.False(firstWasCanceled);
        Assert.False(analyzer.FirstToken.CanBeCanceled);
        Assert.Equal(1, callsWhileFirstWasBlocked);
        Assert.Equal(1, concurrencyWhileFirstWasBlocked);
        Assert.Equal(2, analyzer.CallCount);
        Assert.Equal(1, analyzer.MaxConcurrency);
    }

    [AvaloniaFact]
    public async Task SliderBurst_CoalescesPendingPreviewsToNewestDraft()
    {
        BlockingAnalyzer analyzer = new();
        using ScoringAdjustmentPreviewRunner runner = new(analyzer);

        Task first = RunPreviewAsync(runner, CreateDraft(3.0));
        await analyzer.FirstStarted;
        Task superseded = RunPreviewAsync(runner, CreateDraft(3.1));
        Task newest = RunPreviewAsync(runner, CreateDraft(3.2));

        analyzer.ReleaseFirst();
        await Task.WhenAll(first, superseded, newest);

        Assert.Equal(2, analyzer.CallCount);
        Assert.Equal([3.0, 3.2], analyzer.AnalyzedHighThresholds);
    }

    private static Task RunPreviewAsync(
        ScoringAdjustmentPreviewRunner runner,
        ScoringAdjustmentDraft draft)
        => runner.Run(
            new IntermediateSongPackage(),
            string.Empty,
            "move",
            [],
            draft,
            MotionRecordingScoringProfile.JDNext,
            TimeSpan.Zero,
            static _ => { },
            static exception => throw exception);

    private static ScoringAdjustmentDraft CreateDraft(double highThreshold)
        => new(
            LowThreshold: 1.0,
            LowThresholdDefault: false,
            HighThreshold: highThreshold,
            HighThresholdDefault: false,
            AutoCorrelationThreshold: 0.5,
            AutoCorrelationThresholdDefault: false,
            DirectionImpactFactor: 0.5,
            DirectionImpactFactorDefault: false,
            IgnoreDirection: false,
            IgnoreAutocorrelation: false);

    private static ScoringAdjustmentPreviewResult CreateResult(ScoringAdjustmentDraft draft)
        => new(
            draft,
            new Dictionary<ScoringAdjustmentParameter, IReadOnlyList<ScoringAdjustmentSweepSeries>>(),
            [],
            Recommendation: null,
            CandidateCount: 0,
            RecordingCount: 0,
            MoveInstanceCount: 0,
            ScoredMoveCount: 0);

    private sealed class ImmediateAnalyzer : IScoringAdjustmentPreviewAnalyzer
    {
        public Task<ScoringAdjustmentPreviewResult> AnalyzeAsync(
            IntermediateSongPackage package,
            string rootPath,
            string moveId,
            byte[] classifierBytes,
            ScoringAdjustmentDraft draft,
            MotionRecordingScoringProfile scoringProfile,
            CancellationToken cancellationToken)
            => Task.FromResult(CreateResult(draft));
    }

    private sealed class BlockingAnalyzer : IScoringAdjustmentPreviewAnalyzer
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<double> analyzedHighThresholds = [];
        private int activeCalls;

        public Task FirstStarted => firstStarted.Task;
        public CancellationToken FirstToken { get; private set; }
        public int CallCount { get; private set; }
        public int MaxConcurrency { get; private set; }
        public IReadOnlyList<double> AnalyzedHighThresholds
        {
            get
            {
                lock (gate)
                    return [.. analyzedHighThresholds];
            }
        }

        public void ReleaseFirst() => releaseFirst.TrySetResult();

        public async Task<ScoringAdjustmentPreviewResult> AnalyzeAsync(
            IntermediateSongPackage package,
            string rootPath,
            string moveId,
            byte[] classifierBytes,
            ScoringAdjustmentDraft draft,
            MotionRecordingScoringProfile scoringProfile,
            CancellationToken cancellationToken)
        {
            int callNumber;
            lock (gate)
            {
                callNumber = ++CallCount;
                activeCalls++;
                MaxConcurrency = Math.Max(MaxConcurrency, activeCalls);
                analyzedHighThresholds.Add(draft.HighThreshold);
                if (callNumber == 1)
                    FirstToken = cancellationToken;
            }

            try
            {
                if (callNumber == 1)
                {
                    firstStarted.TrySetResult();
                    await releaseFirst.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return CreateResult(draft);
            }
            finally
            {
                lock (gate)
                    activeCalls--;
            }
        }
    }
}
