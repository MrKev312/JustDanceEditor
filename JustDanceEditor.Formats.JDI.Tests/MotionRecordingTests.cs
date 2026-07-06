using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

using System.Buffers.Binary;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class MotionRecordingTests
{
    [Fact]
    public async Task JsonMotionRecordingRepository_RoundTripsRecording()
    {
        string root = CreateTempRoot();
        try
        {
            JsonMotionRecordingRepository repository = new();
            MotionRecordingDocument recording = new()
            {
                CoachId = 0,
                DeviceId = "AA:BB:CC:DD:EE:FF",
                DeviceName = "DSU Slot 1",
                SongId = Guid.NewGuid().ToString("D"),
                MapName = "testmap",
                StartedAtUtc = new DateTimeOffset(2026, 7, 6, 12, 30, 0, TimeSpan.Zero),
                TimelineStartSeconds = 1.25,
                TimelineEndSeconds = 3.75,
                Samples =
                {
                    new()
                    {
                        MapTime = 1.25,
                        AccX = 0.1f,
                        AccY = 0.2f,
                        AccZ = 0.3f,
                        GyroX = 1.0f,
                        GyroY = 2.0f,
                        GyroZ = 3.0f,
                        SensorTimestampMicroseconds = 123
                    }
                }
            };

            string path = await repository.SaveAsync(root, recording, TestContext.Current.CancellationToken);
            MotionRecordingDocument loaded = await repository.LoadAsync(path, TestContext.Current.CancellationToken);

            Assert.Contains(Path.Combine("recordings", "coach_00"), path);
            Assert.Equal(0, loaded.CoachId);
            Assert.Equal("DSU Slot 1", loaded.DeviceName);
            Assert.Single(loaded.Samples);
            Assert.Equal(0.2f, loaded.Samples[0].AccY);
            string listedPath = Assert.Single(repository.ListRecordingFiles(root, 0));
            Assert.Equal(path, listedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionClassifierGenerator_WritesMoveClassifierFromRecording()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID);

            JdiMotionClassifierGenerator generator = new();
            MotionClassifierGenerationResult result = await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            GeneratedMotionClassifier classifier = Assert.Single(result.Classifiers);
            Assert.Equal("move_a", classifier.MoveId);
            Assert.True(File.Exists(classifier.Path));
            Assert.True(new FileInfo(classifier.Path).Length > 0);
            Assert.Equal(1, classifier.ExampleCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingLiveScorer_LiveModeScoresRepeatedMoveWithCurrentExamples()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateThreeMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID, endSeconds: 1.6);

            JdiMotionRecordingLiveScorer scorer = new();
            MotionRecordingLiveScoreSession session = await scorer.CreateSessionAsync(
                root,
                package,
                coachId: 0,
                previousRecordings: [],
                cancellationToken: TestContext.Current.CancellationToken);

            MotionRecordingLiveScore firstA = Assert.Single(session.ScoreCompletedMoves(recording, 0.51));
            Assert.Equal("move_a", firstA.MoveId);
            Assert.Equal(1, firstA.ClassifierExampleCount);
            Assert.True(
                firstA.Feedback != MotionRecordingMoveFeedback.X,
                $"Expected first move to score, got {firstA.PercentageScore}, distance {firstA.MoveSpace?.StatisticalDistance}, energy {firstA.MoveSpace?.EnergyAmount}, issue '{firstA.Issue}'.");

            MotionRecordingLiveScore firstB = Assert.Single(session.ScoreCompletedMoves(recording, 1.01));
            Assert.Equal("move_b", firstB.MoveId);
            Assert.Equal(1, firstB.ClassifierExampleCount);

            MotionRecordingLiveScore secondA = Assert.Single(session.ScoreCompletedMoves(recording, 1.51));
            Assert.Equal("move_a", secondA.MoveId);
            Assert.Equal(2, secondA.ClassifierExampleCount);
            Assert.True(secondA.TotalScore > firstA.TotalScore);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingLiveScorer_ExistingModeScoresAgainstWrittenClassifier()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID);

            JdiMotionClassifierGenerator generator = new();
            await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            JdiMotionRecordingLiveScorer scorer = new();
            MotionRecordingLiveScoreSession session = await scorer.CreateSessionAsync(
                root,
                package,
                coachId: 0,
                previousRecordings: [],
                options: new MotionRecordingLiveScoringOptions
                {
                    ClassifierSource = MotionRecordingClassifierSource.ExistingFiles
                },
                cancellationToken: TestContext.Current.CancellationToken);

            MotionRecordingLiveScore score = Assert.Single(session.ScoreCompletedMoves(recording, 0.75));
            Assert.Equal(MotionRecordingClassifierSource.ExistingFiles, score.ClassifierSource);
            Assert.NotNull(score.MoveSpace);
            Assert.Null(score.Issue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingAnalyzer_BuildsStatsAgainstExistingClassifiers()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateThreeMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID, endSeconds: 1.6);

            JdiMotionClassifierGenerator generator = new();
            await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            JdiMotionRecordingAnalyzer analyzer = new();
            MotionRecordingAnalysisResult result = await analyzer.AnalyzeExistingClassifiersAsync(
                root,
                package,
                recording,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(3, result.MoveCount);
            Assert.Equal(3, result.ScoredMoveCount);
            Assert.Equal(0, result.MissingClassifierCount);
            Assert.Equal(0, result.IssueCount);
            Assert.Equal(3, result.Moves.Count);
            Assert.Equal(result.Moves[^1].TotalScore, result.TotalScore);

            MotionRecordingMoveAggregate aggregate = Assert.Single(result.MoveAggregates, a => a.MoveId == "move_a");
            Assert.Equal(2, aggregate.Count);
            Assert.Equal(0, aggregate.WorstMoveIndex);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingLiveScorer_ExistingModeReadsBigEndianClassifier()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID);

            JdiMotionClassifierGenerator generator = new();
            MotionClassifierGenerationResult generation = await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            GeneratedMotionClassifier generated = Assert.Single(generation.Classifiers);
            await File.WriteAllBytesAsync(
                generated.Path,
                ConvertGeneratedClassifierToBigEndian(await File.ReadAllBytesAsync(generated.Path, TestContext.Current.CancellationToken)),
                TestContext.Current.CancellationToken);

            JdiMotionRecordingLiveScorer scorer = new();
            MotionRecordingLiveScoreSession session = await scorer.CreateSessionAsync(
                root,
                package,
                coachId: 0,
                previousRecordings: [],
                options: new MotionRecordingLiveScoringOptions
                {
                    ClassifierSource = MotionRecordingClassifierSource.ExistingFiles
                },
                cancellationToken: TestContext.Current.CancellationToken);

            MotionRecordingLiveScore score = Assert.Single(session.ScoreCompletedMoves(recording, 0.75));
            Assert.NotNull(score.MoveSpace);
            Assert.Null(score.Issue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingLiveScorer_ExistingModeHandlesDuplicateSampleTimes()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID);

            JdiMotionClassifierGenerator generator = new();
            await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            MotionRecordingDocument duplicateTimeRecording = CreateRecording(package.Metadata.SongID);
            for (int i = 1; i < duplicateTimeRecording.Samples.Count; i += 3)
                duplicateTimeRecording.Samples[i].MapTime = duplicateTimeRecording.Samples[i - 1].MapTime;

            JdiMotionRecordingLiveScorer scorer = new();
            MotionRecordingLiveScoreSession session = await scorer.CreateSessionAsync(
                root,
                package,
                coachId: 0,
                previousRecordings: [],
                options: new MotionRecordingLiveScoringOptions
                {
                    ClassifierSource = MotionRecordingClassifierSource.ExistingFiles
                },
                cancellationToken: TestContext.Current.CancellationToken);

            MotionRecordingLiveScore score = Assert.Single(session.ScoreCompletedMoves(duplicateTimeRecording, 0.75));
            AssertFinite(score.PercentageScore);
            AssertFinite(score.AddedScore);
            AssertFinite(score.TotalScore);
            Assert.NotNull(score.MoveSpace);
            AssertFinite(score.MoveSpace.StatisticalDistance);
            AssertFinite(score.MoveSpace.RatioScore);
            AssertFinite(score.MoveSpace.EnergyAmount);
            AssertFinite(score.MoveSpace.EnergyFactor);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingLiveScorer_DoesNotScoreBeforeFirstMoveEnds()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            package.CoachTimelines.Single().Clips.Single().StartTime = 48;
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID, endSeconds: 2.0);

            JdiMotionClassifierGenerator generator = new();
            await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            JdiMotionRecordingLiveScorer scorer = new();
            MotionRecordingLiveScoreSession session = await scorer.CreateSessionAsync(
                root,
                package,
                coachId: 0,
                previousRecordings: [],
                options: new MotionRecordingLiveScoringOptions
                {
                    ClassifierSource = MotionRecordingClassifierSource.ExistingFiles
                },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(session.ScoreCompletedMoves(recording, 0.99));
            MotionRecordingLiveScore score = Assert.Single(session.ScoreCompletedMoves(recording, 1.5));
            Assert.Equal("move_a", score.MoveId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JdiMotionRecordingLiveScorer_UsesTimelineBeatLabelsForMoveWindows()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            package.TimelineStructure.StartBeat = -20;
            package.TimelineStructure.EndBeat = 32;
            package.TimelineStructure.Markers.Clear();
            for (int beatIndex = 0; beatIndex <= 64; beatIndex++)
                package.TimelineStructure.Markers.Add(beatIndex * 24000);

            package.CoachTimelines.Single().Clips.Single().StartTime = 8 * 24;
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID, endSeconds: 15.0);

            JdiMotionClassifierGenerator generator = new();
            await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            JdiMotionRecordingLiveScorer scorer = new();
            MotionRecordingLiveScoreSession session = await scorer.CreateSessionAsync(
                root,
                package,
                coachId: 0,
                previousRecordings: [],
                options: new MotionRecordingLiveScoringOptions
                {
                    ClassifierSource = MotionRecordingClassifierSource.ExistingFiles
                },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Empty(session.ScoreCompletedMoves(recording, 4.51));

            MotionRecordingLiveScore score = Assert.Single(session.ScoreCompletedMoves(recording, 14.51));
            Assert.Equal("move_a", score.MoveId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IntermediateSongPackage CreateOneMovePackage()
    {
        IntermediateSongPackage package = new();
        package.Metadata.SongID = Guid.NewGuid();
        package.Metadata.MapName = "testmap";
        package.Metadata.CoachCount = 1;
        package.TimelineStructure.StartBeat = 0;
        package.TimelineStructure.EndBeat = 16;

        for (int beat = 0; beat <= 16; beat++)
            package.TimelineStructure.Markers.Add(beat * 24000);

        package.HandCoachMoves["move_a"] = new CoachMoveDefinition
        {
            Color = "#CCCCCC",
            Duration = 24,
            MoveType = CoachMoveType.HandTracking
        };

        package.CoachTimelines.Add(new MoveTimeline
        {
            CoachId = 0,
            Clips =
            {
                new MoveClip
                {
                    MoveId = "move_a",
                    StartTime = 0
                }
            }
        });

        return package;
    }

    private static IntermediateSongPackage CreateThreeMovePackage()
    {
        IntermediateSongPackage package = CreateOneMovePackage();
        package.HandCoachMoves["move_b"] = new CoachMoveDefinition
        {
            Color = "#CCCCCC",
            Duration = 24,
            MoveType = CoachMoveType.HandTracking
        };

        MoveTimeline timeline = package.CoachTimelines.Single();
        timeline.Clips.Clear();
        timeline.Clips.Add(new MoveClip
        {
            MoveId = "move_a",
            StartTime = 0
        });
        timeline.Clips.Add(new MoveClip
        {
            MoveId = "move_b",
            StartTime = 24
        });
        timeline.Clips.Add(new MoveClip
        {
            MoveId = "move_a",
            StartTime = 48
        });

        return package;
    }

    private static MotionRecordingDocument CreateRecording(Guid songId, double endSeconds = 0.75)
    {
        MotionRecordingDocument recording = new()
        {
            CoachId = 0,
            SongId = songId.ToString("D"),
            MapName = "testmap",
            StartedAtUtc = DateTimeOffset.UtcNow,
            TimelineStartSeconds = 0,
            TimelineEndSeconds = endSeconds
        };

        int sampleCount = (int)Math.Ceiling(endSeconds * 100.0);
        for (int i = 0; i <= sampleCount; i++)
        {
            float t = i / 100.0f;
            recording.Samples.Add(new RecordedMotionSample
            {
                MapTime = t,
                AccX = MathF.Sin(t * 12.0f),
                AccY = MathF.Cos(t * 8.0f),
                AccZ = 1.0f + (MathF.Sin(t * 5.0f) * 0.25f),
                GyroX = 0.1f,
                GyroY = 0.2f,
                GyroZ = 0.3f
            });
        }

        return recording;
    }

    private static byte[] ConvertGeneratedClassifierToBigEndian(byte[] source)
    {
        byte[] result = [.. source];
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4, 4));

        Reverse(result, 0, sizeof(uint));
        Reverse(result, 4, sizeof(uint));

        int offset = 8 + 64 + 64 + 64;
        Reverse(result, offset, sizeof(float));
        offset += sizeof(float);
        Reverse(result, offset, sizeof(float));
        offset += sizeof(float);
        Reverse(result, offset, sizeof(float));
        offset += sizeof(float);

        if (version >= 7)
        {
            Reverse(result, offset, sizeof(float));
            offset += sizeof(float);
            Reverse(result, offset, sizeof(float));
            offset += sizeof(float);
        }

        Reverse(result, offset, sizeof(ulong));
        offset += sizeof(ulong);
        Reverse(result, offset, sizeof(uint));
        offset += sizeof(uint);

        int scoringAlgorithmType = BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(offset, sizeof(int)));
        Reverse(result, offset, sizeof(int));
        offset += sizeof(int);

        uint energyMeansCount = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(offset, sizeof(uint)));
        Reverse(result, offset, sizeof(uint));
        offset += sizeof(uint);
        Reverse(result, offset, sizeof(uint));
        offset += sizeof(uint);

        int measureCount = Math.Abs(scoringAlgorithmType);
        int covarianceCount = scoringAlgorithmType > 0
            ? measureCount
            : measureCount * (measureCount + 1) / 2;
        int singleCount = checked(measureCount + covarianceCount + (int)energyMeansCount);

        for (int i = 0; i < singleCount; i++)
        {
            Reverse(result, offset, sizeof(float));
            offset += sizeof(float);
        }

        return result;
    }

    private static void Reverse(byte[] data, int offset, int count)
        => Array.Reverse(data, offset, count);

    private static void AssertFinite(float value)
    {
        Assert.False(float.IsNaN(value), "Expected a finite score value, got NaN.");
        Assert.False(float.IsInfinity(value), "Expected a finite score value, got infinity.");
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "jde-recording-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}