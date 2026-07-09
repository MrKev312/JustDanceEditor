using JustDanceEditor.Formats.JDI.Recordings;
using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

using System.Buffers.Binary;
using System.Text;

using Xunit;

namespace JustDanceEditor.Formats.JDI.Tests;

public class MotionRecordingTests
{
    [Fact]
    public void JdiMotionRecordingScoreMath_AdjustedPercentageAppliesFinalModifiers()
    {
        MoveSpaceScoreResult clean = CreateMoveSpaceScoreResult(ratioScore: 0.8f, autoCorrelationTime: -1.0f, directionIgnored: false, directionImpact: 0.0f);
        MoveSpaceScoreResult shaky = clean with { AutoCorrelationTime = 0.15f };
        MoveSpaceScoreResult wrongDirection = clean with { DirectionTendencyImpactOnScoreRatio = -0.5f };
        MoveSpaceScoreResult ignoredDirection = wrongDirection with { DirectionTendencyIgnored = true };

        Assert.InRange(JdiMotionRecordingScoreMath.GetAdjustedPercentage(clean), 87.99f, 88.01f);
        Assert.InRange(JdiMotionRecordingScoreMath.GetAdjustedPercentage(shaky), 39.99f, 40.01f);
        Assert.InRange(JdiMotionRecordingScoreMath.GetAdjustedPercentage(wrongDirection), 62.99f, 63.01f);
        Assert.InRange(JdiMotionRecordingScoreMath.GetAdjustedPercentage(ignoredDirection), 87.99f, 88.01f);
    }

    [Fact]
    public void JdiMotionRecordingScoreMath_ProfileEvaluationUsesOfficialGoldAndThresholds()
    {
        MoveSpaceScoreResult clean = CreateMoveSpaceScoreResult(ratioScore: 0.88f, autoCorrelationTime: -1.0f, directionIgnored: false, directionImpact: 0.0f);
        MoveSpaceScoreResult goldPass = clean with { RatioScore = 0.6f, PercentageScore = 60.0f };

        MotionRecordingScoreEvaluation ubiArtClean = JdiMotionRecordingScoreMath.EvaluateMove(
            isGoldMove: false,
            clean,
            goldScoreValue: 500.0f,
            moveScoreValue: 100.0f,
            MotionRecordingScoringProfile.UbiArt);
        MotionRecordingScoreEvaluation ubiArtGold = JdiMotionRecordingScoreMath.EvaluateMove(
            isGoldMove: true,
            goldPass,
            goldScoreValue: 500.0f,
            moveScoreValue: 100.0f,
            MotionRecordingScoringProfile.UbiArt);
        MotionRecordingScoreEvaluation rawGold = JdiMotionRecordingScoreMath.EvaluateMove(
            isGoldMove: true,
            goldPass,
            goldScoreValue: 500.0f,
            moveScoreValue: 100.0f,
            MotionRecordingScoringProfile.Raw);

        Assert.Equal(MotionRecordingMoveFeedback.Perfect, ubiArtClean.Feedback);
        Assert.InRange(ubiArtClean.PercentageScore, 90.63f, 90.65f);
        Assert.Equal(MotionRecordingMoveFeedback.Yeah, ubiArtGold.Feedback);
        Assert.Equal(500.0f, ubiArtGold.AddedScore);
        Assert.Equal(MotionRecordingMoveFeedback.X, rawGold.Feedback);
        Assert.Equal(0.0f, rawGold.AddedScore);
    }

    [Fact]
    public void JdiMotionRecordingScoreMath_JDNextProfileUsesJDNextMoveSpaceDefaults()
    {
        MoveScoringOptions defaults = JdiMotionRecordingScoreMath.ApplyScoringProfileDefaults(
            new MoveScoringOptions(),
            MotionRecordingScoringProfile.JDNext);

        Assert.Equal(1.0f, defaults.DefaultLowThreshold);
        Assert.Equal(3.5f, defaults.DefaultHighThreshold);
        Assert.Equal(0.7f, defaults.DefaultAutoCorrelationThreshold);
        Assert.Equal(1.0f, defaults.DefaultDirectionImpactFactor);
        Assert.Equal(60.0f, defaults.SmoothingFrequency);
    }

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
    public async Task JdiMotionRecordingMoveScorer_ScoresSelectedMoveAgainstClassifierBytes()
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

            GeneratedMotionClassifier classifier = Assert.Single(generation.Classifiers);
            byte[] classifierBytes = await File.ReadAllBytesAsync(classifier.Path, TestContext.Current.CancellationToken);

            JdiMotionRecordingMoveScorer scorer = new();
            MotionRecordingMoveScorePreview preview = scorer.ScoreMove(
                package,
                "move_a",
                [recording],
                classifierBytes,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, preview.RecordingCount);
            Assert.Equal(1, preview.MoveInstanceCount);
            Assert.Equal(1, preview.ScoredMoveCount);
            Assert.Equal(
                preview.ScoredMoveCount,
                preview.XCount + preview.OkCount + preview.GoodCount + preview.SuperCount + preview.PerfectCount + preview.YeahCount);
            Assert.False(float.IsNaN(preview.AveragePercentageScore));
            Assert.False(float.IsNaN(preview.AverageAddedScore));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MotionClassifierHeaderEditor_PatchesBigEndianHeaderWithoutRewritingPayload()
    {
        byte[] source = CreateHeaderOnlyClassifier(isBigEndian: true);

        byte[] patched = MotionClassifierHeaderEditor.UpdateHeader(source, new MotionClassifierHeaderUpdate
        {
            LowThreshold = 1.2f,
            HighThreshold = 4.25f,
            AutoCorrelationThreshold = 0.8f,
            DirectionImpactFactor = 0.3f,
            CustomizationBitField = 1
        });

        MotionClassifierHeader header = MotionClassifierHeaderEditor.ReadHeader(patched);
        Assert.True(header.IsBigEndian);
        Assert.Equal("move_a", header.MoveName);
        Assert.Equal("testmap", header.SongName);
        Assert.Equal(1.2f, header.LowThreshold);
        Assert.Equal(4.25f, header.HighThreshold);
        Assert.Equal(0.8f, header.AutoCorrelationThreshold);
        Assert.Equal(0.3f, header.DirectionImpactFactor);
        Assert.Equal(1U, header.CustomizationBitField);

        Assert.NotEqual(source, patched);
        Assert.Equal(source.AsSpan(HeaderOnlyClassifierPayloadOffset).ToArray(), patched.AsSpan(HeaderOnlyClassifierPayloadOffset).ToArray());
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

    private static MoveSpaceScoreResult CreateMoveSpaceScoreResult(
        float ratioScore,
        float autoCorrelationTime,
        bool directionIgnored,
        float directionImpact)
        => new(
            "move_a",
            StatisticalDistance: 1.0f,
            ratioScore,
            PercentageScore: ratioScore * 100.0f,
            EnergyAmount: 1.0f,
            EnergyFactor: 1.0f,
            autoCorrelationTime,
            directionIgnored,
            directionImpact,
            LowThreshold: 1.0f,
            HighThreshold: 3.0f,
            AutoCorrelationThreshold: 0.7f,
            DirectionImpactFactor: 1.0f);

    private const int HeaderOnlyClassifierPayloadOffset = 240;

    private static byte[] CreateHeaderOnlyClassifier(bool isBigEndian)
    {
        byte[] result = new byte[HeaderOnlyClassifierPayloadOffset + 4];
        WriteUInt32(result, 0, 1, isBigEndian);
        WriteUInt32(result, 4, 7, isBigEndian);
        WriteFixedString(result, 8, "move_a");
        WriteFixedString(result, 72, "testmap");
        WriteFixedString(result, 136, "Acc_Dev_Dir_NP");
        WriteSingle(result, 200, 0.75f, isBigEndian);
        WriteSingle(result, 204, 1.0f, isBigEndian);
        WriteSingle(result, 208, 3.0f, isBigEndian);
        WriteSingle(result, 212, 1.0f, isBigEndian);
        WriteSingle(result, 216, -1.0f, isBigEndian);
        WriteUInt64(result, 220, 0x211C000000000000UL, isBigEndian);
        WriteUInt32(result, 228, 2, isBigEndian);
        WriteInt32(result, 232, 0, isBigEndian);
        WriteUInt32(result, 236, 0, isBigEndian);
        result[240] = 0xBA;
        result[241] = 0xAD;
        result[242] = 0xF0;
        result[243] = 0x0D;
        return result;
    }

    private static void WriteFixedString(byte[] data, int offset, string value)
        => Encoding.UTF8.GetBytes(value, data.AsSpan(offset, 64));

    private static void WriteSingle(byte[] data, int offset, float value, bool isBigEndian)
        => WriteInt32(data, offset, BitConverter.SingleToInt32Bits(value), isBigEndian);

    private static void WriteUInt32(byte[] data, int offset, uint value, bool isBigEndian)
    {
        if (isBigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, sizeof(uint)), value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
    }

    private static void WriteUInt64(byte[] data, int offset, ulong value, bool isBigEndian)
    {
        if (isBigEndian)
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset, sizeof(ulong)), value);
        else
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(offset, sizeof(ulong)), value);
    }

    private static void WriteInt32(byte[] data, int offset, int value, bool isBigEndian)
    {
        if (isBigEndian)
            BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, sizeof(int)), value);
        else
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
    }

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
