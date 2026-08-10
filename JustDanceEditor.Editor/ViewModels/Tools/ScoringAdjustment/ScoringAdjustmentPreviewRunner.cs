using Avalonia.Threading;

using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Scoring;

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class ScoringAdjustmentPreviewRunner(IScoringAdjustmentPreviewAnalyzer analyzer) : IDisposable
{
    private readonly Lock gate = new();
    private PreviewJob? pending;
    private int latestRequestId;
    private bool workerRunning;
    private bool disposed;

    public Task Run(
        IntermediateSongPackage package,
        string rootPath,
        string moveId,
        byte[] classifierBytes,
        ScoringAdjustmentDraft draft,
        MotionRecordingScoringProfile scoringProfile,
        TimeSpan delay,
        Action<ScoringAdjustmentPreviewResult> onCompleted,
        Action<Exception> onError)
    {
        PreviewJob job;
        PreviewJob? superseded;
        bool startWorker;
        lock (gate)
        {
            if (disposed)
                return Task.CompletedTask;

            job = new(
                ++latestRequestId,
                package,
                rootPath,
                moveId,
                classifierBytes,
                draft,
                scoringProfile,
                delay,
                onCompleted,
                onError);
            superseded = pending;
            pending = job;
            startWorker = !workerRunning;
            workerRunning = true;
        }

        superseded?.Complete();
        if (startWorker)
            _ = ProcessQueueAsync();
        return job.Completion;
    }

    public void Cancel()
    {
        PreviewJob? skipped;
        lock (gate)
        {
            latestRequestId++;
            skipped = pending;
            pending = null;
        }

        skipped?.Complete();
    }

    public void Dispose()
    {
        PreviewJob? skipped;
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            latestRequestId++;
            skipped = pending;
            pending = null;
        }

        skipped?.Complete();
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            PreviewJob? job;
            lock (gate)
            {
                job = pending;
                pending = null;
                if (job == null)
                {
                    workerRunning = false;
                    return;
                }
            }

            try
            {
                TimeSpan remainingDelay = job.Delay - Stopwatch.GetElapsedTime(job.EnqueuedTimestamp);
                if (remainingDelay > TimeSpan.Zero)
                    await Task.Delay(remainingDelay).ConfigureAwait(false);

                if (ShouldSkipBeforeAnalysis(job))
                    continue;

                ScoringAdjustmentPreviewResult result = await analyzer.AnalyzeAsync(
                    job.Package,
                    job.RootPath,
                    job.MoveId,
                    job.ClassifierBytes,
                    job.Draft,
                    job.ScoringProfile,
                    CancellationToken.None).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsLatest(job))
                        job.OnCompleted(result);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsLatest(job))
                        job.OnError(ex);
                });
            }
            finally
            {
                job.Complete();
            }
        }
    }

    private bool ShouldSkipBeforeAnalysis(PreviewJob job)
    {
        lock (gate)
            return disposed || pending != null || job.RequestId != latestRequestId;
    }

    private bool IsLatest(PreviewJob job)
    {
        lock (gate)
            return !disposed && job.RequestId == latestRequestId;
    }

    private sealed class PreviewJob(
        int requestId,
        IntermediateSongPackage package,
        string rootPath,
        string moveId,
        byte[] classifierBytes,
        ScoringAdjustmentDraft draft,
        MotionRecordingScoringProfile scoringProfile,
        TimeSpan delay,
        Action<ScoringAdjustmentPreviewResult> onCompleted,
        Action<Exception> onError)
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequestId { get; } = requestId;
        public IntermediateSongPackage Package { get; } = package;
        public string RootPath { get; } = rootPath;
        public string MoveId { get; } = moveId;
        public byte[] ClassifierBytes { get; } = classifierBytes;
        public ScoringAdjustmentDraft Draft { get; } = draft;
        public MotionRecordingScoringProfile ScoringProfile { get; } = scoringProfile;
        public TimeSpan Delay { get; } = delay;
        public long EnqueuedTimestamp { get; } = Stopwatch.GetTimestamp();
        public Action<ScoringAdjustmentPreviewResult> OnCompleted { get; } = onCompleted;
        public Action<Exception> OnError { get; } = onError;
        public Task Completion => completion.Task;

        public void Complete() => completion.TrySetResult();
    }
}