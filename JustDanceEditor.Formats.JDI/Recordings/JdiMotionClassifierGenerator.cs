using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed record MotionClassifierGenerationResult(
    int RecordingCount,
    int MoveCount,
    IReadOnlyList<GeneratedMotionClassifier> Classifiers,
    IReadOnlyList<MotionClassifierGenerationIssue> Issues);

public sealed record GeneratedMotionClassifier(string MoveId, string Path, int ExampleCount);

public sealed record MotionClassifierGenerationIssue(string MoveId, string Message);

public sealed class JdiMotionClassifierGenerator
{
    public async Task<MotionClassifierGenerationResult> GenerateForCoachAsync(
        string packageRoot,
        IntermediateSongPackage package,
        int coachId,
        IReadOnlyList<MotionRecordingDocument> recordings,
        MotionClassifierGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(recordings);
        if (coachId < 0)
            throw new ArgumentOutOfRangeException(nameof(coachId), "Coach IDs cannot be negative.");

        MoveTimeline? timeline = JdiMotionRecordingData.FindHandMotionTimeline(package, coachId);
        if (timeline == null)
            return new MotionClassifierGenerationResult(recordings.Count, 0, [], [new("", $"No hand-motion timeline exists for coach {coachId}.")]);

        List<JdiMotionMoveWindow> moveWindows = JdiMotionRecordingData.BuildMoveWindows(package, timeline);
        MotionTrainingSelectionDocument trainingSelection = await new JsonMotionTrainingSelectionRepository()
            .LoadAsync(packageRoot, cancellationToken);
        Dictionary<string, List<MotionExample>> examplesByMove = JdiMotionRecordingData.BuildExamplesByMove(
            recordings,
            moveWindows,
            coachId,
            trainingSelection);
        List<MotionClassifierGenerationIssue> issues = [];

        JdiMotionClassifierStorage.EnsureVersionFolders(packageRoot);

        string songName = string.IsNullOrWhiteSpace(package.Metadata.MapName)
            ? package.Metadata.SongID.ToString("N")
            : package.Metadata.MapName;

        MotionClassifierGenerator generator = new();
        MotionClassifierGenerationOptions generationOptions = options ?? new MotionClassifierGenerationOptions();
        List<GeneratedMotionClassifier> generated = [];

        foreach ((string moveId, List<MotionExample> examples) in examplesByMove.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!JdiMotionRecordingData.IsSafeFileName(moveId))
            {
                issues.Add(new MotionClassifierGenerationIssue(moveId, "Move ID cannot be written as a classifier file name."));
                continue;
            }

            IReadOnlyDictionary<MotionClassifierFormatVersion, byte[]> variants;
            try
            {
                variants = generator.BuildAllVersions(new MotionClassifierBuildRequest
                {
                    SongName = songName,
                    MoveName = moveId,
                    Examples = examples,
                    Options = generationOptions
                });
            }
            catch (Exception ex)
            {
                issues.Add(new MotionClassifierGenerationIssue(moveId, ex.Message));
                continue;
            }

            string fileName = moveId + ".msm";
            foreach ((MotionClassifierFormatVersion version, byte[] classifier) in variants)
            {
                string variantPath = JdiMotionClassifierStorage.GetVersionPath(packageRoot, fileName, version);
                await File.WriteAllBytesAsync(variantPath, classifier, cancellationToken);
            }

            generated.Add(new GeneratedMotionClassifier(
                moveId,
                JdiMotionClassifierStorage.GetVersion7Path(packageRoot, fileName),
                examples.Count));
        }

        foreach (JdiMotionMoveWindow move in moveWindows)
        {
            if (!examplesByMove.ContainsKey(move.MoveId))
                issues.Add(new MotionClassifierGenerationIssue(move.MoveId, "No recording samples overlapped this move."));
        }

        return new MotionClassifierGenerationResult(
            recordings.Count(r => r.CoachId == coachId),
            moveWindows.Count,
            generated,
            issues);
    }
}