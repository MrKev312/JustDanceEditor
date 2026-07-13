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
    public void MotionRecordingScoreMath_AdjustedPercentageAppliesFinalModifiers()
    {
        MoveSpaceScoreResult clean = CreateMoveSpaceScoreResult(ratioScore: 0.8f, autoCorrelationTime: -1.0f, directionIgnored: false, directionImpact: 0.0f);
        MoveSpaceScoreResult shaky = clean with { AutoCorrelationTime = 0.15f };
        MoveSpaceScoreResult wrongDirection = clean with { DirectionTendencyImpactOnScoreRatio = -0.5f };
        MoveSpaceScoreResult ignoredDirection = wrongDirection with { DirectionTendencyIgnored = true };

        Assert.InRange(MotionRecordingScoreMath.GetAdjustedPercentage(clean), 87.99f, 88.01f);
        Assert.InRange(MotionRecordingScoreMath.GetAdjustedPercentage(shaky), 39.99f, 40.01f);
        Assert.InRange(MotionRecordingScoreMath.GetAdjustedPercentage(wrongDirection), 62.99f, 63.01f);
        Assert.InRange(MotionRecordingScoreMath.GetAdjustedPercentage(ignoredDirection), 87.99f, 88.01f);
    }

    [Fact]
    public void MotionRecordingScoreMath_ProfileEvaluationUsesOfficialGoldAndThresholds()
    {
        MoveSpaceScoreResult clean = CreateMoveSpaceScoreResult(ratioScore: 0.88f, autoCorrelationTime: -1.0f, directionIgnored: false, directionImpact: 0.0f);
        MoveSpaceScoreResult goldPass = clean with { RatioScore = 0.6f, PercentageScore = 60.0f };

        MotionRecordingScoreEvaluation ubiArtClean = MotionRecordingScoreMath.EvaluateMove(
            isGoldMove: false,
            clean,
            goldScoreValue: 500.0f,
            moveScoreValue: 100.0f,
            MotionRecordingScoringProfile.UbiArt);
        MotionRecordingScoreEvaluation ubiArtGold = MotionRecordingScoreMath.EvaluateMove(
            isGoldMove: true,
            goldPass,
            goldScoreValue: 500.0f,
            moveScoreValue: 100.0f,
            MotionRecordingScoringProfile.UbiArt);
        MotionRecordingScoreEvaluation rawGold = MotionRecordingScoreMath.EvaluateMove(
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
    public void MotionRecordingScoreMath_JDNextProfileUsesJDNextMoveSpaceDefaults()
    {
        MoveScoringOptions defaults = MotionRecordingScoreMath.ApplyScoringProfileDefaults(
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
            Assert.NotEqual(Guid.Empty, loaded.RecordingId);
            Assert.Equal(recording.RecordingId, loaded.RecordingId);
            string listedPath = Assert.Single(repository.ListRecordingFiles(root, 0));
            Assert.Equal(path, listedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task JsonMotionRecordingRepository_AssignsStableIdToLegacyRecording()
    {
        string root = CreateTempRoot();
        try
        {
            string path = Path.Combine(root, "legacy.json");
            await File.WriteAllTextAsync(
                path,
                "{\"formatVersion\":1,\"coachId\":0,\"samples\":[]}",
                TestContext.Current.CancellationToken);

            JsonMotionRecordingRepository repository = new();
            MotionRecordingDocument first = await repository.LoadAsync(path, TestContext.Current.CancellationToken);
            MotionRecordingDocument second = await repository.LoadAsync(path, TestContext.Current.CancellationToken);

            Assert.NotEqual(Guid.Empty, first.RecordingId);
            Assert.Equal(first.RecordingId, second.RecordingId);
            Assert.Contains(first.RecordingId.ToString(), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MotionTrainingSelections_AreStoredSeparatelyAndFollowTimelineClipIdentity()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            MoveClip clip = Assert.Single(package.CoachTimelines.Single().Clips);
            MotionRecordingDocument included = CreateRecording(package.Metadata.SongID, endSeconds: 2.0);
            MotionRecordingDocument excluded = CreateRecording(package.Metadata.SongID, endSeconds: 2.0);

            MotionTrainingSelectionDocument selection = new();
            selection.SetExcluded(
                excluded.RecordingId,
                coachId: 0,
                clip.Id,
                clip.MoveId,
                moveOccurrence: 0,
                isExcluded: true);
            JsonMotionTrainingSelectionRepository selectionRepository = new();
            await selectionRepository.SaveAsync(root, selection, TestContext.Current.CancellationToken);

            clip.StartTime = 24;
            JdiMotionClassifierGenerator generator = new();
            MotionClassifierGenerationResult result = await generator.GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [included, excluded],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, Assert.Single(result.Classifiers).ExampleCount);
            MotionTrainingSelectionDocument reloaded = await selectionRepository.LoadAsync(root, TestContext.Current.CancellationToken);
            Assert.True(reloaded.IsExcluded(excluded.RecordingId, 0, clip.Id, clip.MoveId, 0));
            Assert.False(reloaded.IsExcluded(included.RecordingId, 0, clip.Id, clip.MoveId, 0));
            string recordingJson = System.Text.Json.JsonSerializer.Serialize(excluded);
            Assert.DoesNotContain("exclusion", recordingJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MotionTrainingMatrix_CanCompareSamplesToExistingMsms()
    {
        string root = CreateTempRoot();
        try
        {
            IntermediateSongPackage package = CreateOneMovePackage();
            MotionRecordingDocument recording = CreateRecording(package.Metadata.SongID);
            await new JdiMotionClassifierGenerator().GenerateForCoachAsync(
                root,
                package,
                coachId: 0,
                [recording],
                cancellationToken: TestContext.Current.CancellationToken);

            MotionTrainingMatrixResult result = new JdiMotionTrainingMatrixAnalyzer().Analyze(
                root,
                package,
                coachId: 0,
                [recording],
                new MotionTrainingSelectionDocument(),
                compareToExistingMsms: true,
                cancellationToken: TestContext.Current.CancellationToken);

            MotionTrainingMatrixCell cell = Assert.Single(Assert.Single(result.Rows).Cells);
            Assert.NotNull(cell.PercentageScore);
            Assert.Null(cell.Issue);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(new float[] { 100, 100, 0 }, 100)]
    [InlineData(new float[] { 90, 100, 0 }, 90)]
    [InlineData(new float[] { 80, 100 }, 90)]
    public void MotionTrainingMatrix_UsesMedianAsRecordingConsensus(float[] scores, float expected)
    {
        Assert.Equal(expected, JdiMotionTrainingMatrixAnalyzer.GetConsensusScore(scores));
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

            foreach (MotionClassifierFormatVersion version in JdiMotionClassifierStorage.StoredVersions)
            {
                string variantPath = JdiMotionClassifierStorage.GetVersionPath(root, "move_a.msm", version);
                Assert.True(File.Exists(variantPath));
                Assert.Equal(version, MotionClassifierHeaderEditor.ReadHeader(File.ReadAllBytes(variantPath)).FormatVersion);
            }
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

    [Theory]
    [InlineData(MotionClassifierFormatVersion.Version4)]
    [InlineData(MotionClassifierFormatVersion.Version5)]
    [InlineData(MotionClassifierFormatVersion.Version6)]
    [InlineData(MotionClassifierFormatVersion.Version7)]
    public void MotionClassifierGenerator_WritesNativeVersion4ThroughVersion7(MotionClassifierFormatVersion version)
    {
        const float duration = 0.938f;

        byte[] classifier = CreateGeneratedClassifier(version, duration);
        MotionClassifierHeader header = MotionClassifierHeaderEditor.ReadHeader(classifier);

        bool fixedTenParts = version is MotionClassifierFormatVersion.Version4 or MotionClassifierFormatVersion.Version5;
        int expectedParts = fixedTenParts ? 10 : (int)(duration * 30.0f / 2.49f);
        int expectedHeaderSize = version switch
        {
            MotionClassifierFormatVersion.Version4 => 232,
            MotionClassifierFormatVersion.Version5 or MotionClassifierFormatVersion.Version6 => 236,
            MotionClassifierFormatVersion.Version7 => 244,
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        int expectedLength = expectedHeaderSize + (expectedParts * 5 * sizeof(float) * 2) + (2 * sizeof(float));

        Assert.Equal(version, header.FormatVersion);
        Assert.Equal(fixedTenParts ? MotionClassifierGenerator.TenPartMeasureSetName : MotionClassifierGenerator.MeasureSetName, header.MeasureSetName);
        Assert.Equal(expectedParts * 5, header.ScoringAlgorithmType);
        Assert.Equal(2U, header.EnergyMeansCount);
        Assert.Equal(version == MotionClassifierFormatVersion.Version4 ? 0U : 1U, header.CustomizationBitField);
        Assert.Equal(version == MotionClassifierFormatVersion.Version7 ? 1.0f : -1.0f, header.AutoCorrelationThreshold);
        Assert.Equal(-1.0f, header.DirectionImpactFactor);
        Assert.Equal(expectedLength, classifier.Length);
    }

    [Theory]
    [InlineData(MotionClassifierFormatVersion.Version4)]
    [InlineData(MotionClassifierFormatVersion.Version5)]
    [InlineData(MotionClassifierFormatVersion.Version6)]
    [InlineData(MotionClassifierFormatVersion.Version7)]
    public void MoveSpaceScorer_ScoresNativeVersion4ThroughVersion7(MotionClassifierFormatVersion version)
    {
        const float duration = 0.938f;
        IReadOnlyList<MotionSample> samples = CreateMotionSamples(duration);
        byte[] classifier = CreateGeneratedClassifier(version, duration);

        MoveSpaceScoreResult result = new MoveSpaceScorer().ScoreMove(new MoveScoreRequest
        {
            MoveName = "move_a",
            ClassifierBytes = classifier,
            Duration = duration,
            Samples = samples
        });

        Assert.True(float.IsFinite(result.StatisticalDistance));
        Assert.InRange(result.StatisticalDistance, 0.0f, 0.0001f);
        Assert.InRange(result.RatioScore, 0.9999f, 1.0f);
    }

    [Theory]
    [InlineData(MotionClassifierFormatVersion.Version4)]
    [InlineData(MotionClassifierFormatVersion.Version5)]
    [InlineData(MotionClassifierFormatVersion.Version6)]
    [InlineData(MotionClassifierFormatVersion.Version7)]
    public void MotionClassifierHeaderEditor_UpdatesNativeVersion4ThroughVersion7(MotionClassifierFormatVersion version)
    {
        byte[] classifier = CreateGeneratedClassifier(version, duration: 0.938f);

        byte[] updated = MotionClassifierHeaderEditor.UpdateHeader(classifier, new MotionClassifierHeaderUpdate
        {
            LowThreshold = 0.9f,
            HighThreshold = 3.2f,
            AutoCorrelationThreshold = 0.8f,
            DirectionImpactFactor = 0.4f,
            CustomizationBitField = 3
        });
        MotionClassifierHeader header = MotionClassifierHeaderEditor.ReadHeader(updated);

        Assert.Equal(classifier.Length, updated.Length);
        Assert.Equal(0.9f, header.LowThreshold);
        Assert.Equal(3.2f, header.HighThreshold);
        Assert.Equal(version == MotionClassifierFormatVersion.Version4 ? 0U : 3U, header.CustomizationBitField);
        Assert.Equal(version == MotionClassifierFormatVersion.Version7 ? 0.8f : -1.0f, header.AutoCorrelationThreshold);
        Assert.Equal(version == MotionClassifierFormatVersion.Version7 ? 0.4f : -1.0f, header.DirectionImpactFactor);
    }

    [Fact]
    public void MotionClassifierGenerator_RejectsVersion8Generation()
    {
        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => CreateGeneratedClassifier(MotionClassifierFormatVersion.Version8, duration: 0.938f));

        Assert.Contains("versions 4 through 7", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveSpaceScorer_RejectsVersion8Scoring()
    {
        byte[] classifier = CreateGeneratedClassifier(MotionClassifierFormatVersion.Version7, duration: 0.938f);
        BinaryPrimitives.WriteUInt32LittleEndian(classifier.AsSpan(sizeof(uint), sizeof(uint)), (uint)MotionClassifierFormatVersion.Version8);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => new MoveSpaceScorer().ScoreMove(new MoveScoreRequest
        {
            MoveName = "move_a",
            ClassifierBytes = classifier,
            Duration = 0.938f,
            Samples = CreateMotionSamples(0.938f)
        }));

        Assert.Contains("versions 4 through 7", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MotionClassifierConverter_Version5RoundTripIsByteIdentical()
    {
        byte[] version5 = CreateGeneratedClassifier(MotionClassifierConverter.Version5, duration: 0.938f);

        byte[] version7 = MotionClassifierConverter.ConvertToVersion7(version5);
        MotionClassifierHeader version7Header = MotionClassifierHeaderEditor.ReadHeader(version7);
        Assert.Equal(MotionClassifierConverter.Version7, version7Header.FormatVersion);
        Assert.Equal(MotionClassifierGenerator.MeasureSetName, version7Header.MeasureSetName);
        Assert.Equal(MotionClassifierConverter.DynamicTenPartDuration, version7Header.Duration);
        Assert.Equal(-1.0f, version7Header.AutoCorrelationThreshold);
        Assert.Equal(-1.0f, version7Header.DirectionImpactFactor);

        byte[] roundTripped = MotionClassifierConverter.ConvertToVersion5(version7, 0.938f);
        Assert.Equal(version5, roundTripped);
    }

    [Fact]
    public void MotionClassifierConverter_BigEndianVersion5RoundTripIsByteIdentical()
    {
        byte[] version5 = ConvertVersion5ToBigEndian(CreateGeneratedClassifier(MotionClassifierConverter.Version5, duration: 0.938f));

        byte[] version7 = MotionClassifierConverter.ConvertToVersion7(version5);
        Assert.True(MotionClassifierHeaderEditor.ReadHeader(version7).IsBigEndian);

        byte[] roundTripped = MotionClassifierConverter.ConvertToVersion5(version7, 0.938f);
        Assert.Equal(version5, roundTripped);
    }

    [Fact]
    public void MotionClassifierConverter_Version7ToVersion5ResamplesToTenParts()
    {
        byte[] version7 = CreateGeneratedClassifier(MotionClassifierConverter.Version7, duration: 1.2f);

        byte[] version5 = MotionClassifierConverter.ConvertToVersion5(version7, 0.95f);
        MotionClassifierHeader header = MotionClassifierHeaderEditor.ReadHeader(version5);

        Assert.Equal(MotionClassifierConverter.Version5, header.FormatVersion);
        Assert.Equal(MotionClassifierGenerator.TenPartMeasureSetName, header.MeasureSetName);
        Assert.Equal(0.95f, header.Duration);
        Assert.Equal(50, header.ScoringAlgorithmType);
        Assert.Equal(644, version5.Length);
    }

    [Fact]
    public void MotionClassifierConverter_Version4UpgradesLegacyHeaderToVersion7()
    {
        byte[] version5 = CreateGeneratedClassifier(MotionClassifierConverter.Version5, duration: 0.938f);
        byte[] version4 = new byte[version5.Length - sizeof(uint)];
        version5.AsSpan(0, 220).CopyTo(version4);
        version5.AsSpan(224).CopyTo(version4.AsSpan(220));
        BinaryPrimitives.WriteUInt32LittleEndian(version4.AsSpan(4, sizeof(uint)), 4U);

        byte[] version7 = MotionClassifierConverter.ConvertToVersion7(version4);
        MotionClassifierHeader header = MotionClassifierHeaderEditor.ReadHeader(version7);

        Assert.Equal(MotionClassifierConverter.Version7, header.FormatVersion);
        Assert.Equal(MotionClassifierGenerator.MeasureSetName, header.MeasureSetName);
        Assert.Equal(MotionClassifierConverter.DynamicTenPartDuration, header.Duration);
        Assert.Equal(0U, header.CustomizationBitField);
        Assert.Equal(50, header.ScoringAlgorithmType);
    }

    [Theory]
    [InlineData(MotionClassifierFormatVersion.Version4)]
    [InlineData(MotionClassifierFormatVersion.Version5)]
    [InlineData(MotionClassifierFormatVersion.Version6)]
    [InlineData(MotionClassifierFormatVersion.Version7)]
    public void JdiMotionClassifierStorage_ImportsEveryVersionIntoTheCompleteRange(MotionClassifierFormatVersion sourceVersion)
    {
        string root = CreateTempRoot();
        try
        {
            byte[] source = CreateGeneratedClassifier(sourceVersion, duration: 0.938f);

            JdiMotionClassifierStorage.ImportClassifier(root, "move_a.msm", source);

            foreach (MotionClassifierFormatVersion storedVersion in JdiMotionClassifierStorage.StoredVersions)
            {
                byte[] stored = File.ReadAllBytes(JdiMotionClassifierStorage.GetVersionPath(root, "move_a.msm", storedVersion));
                Assert.Equal(storedVersion, MotionClassifierHeaderEditor.ReadHeader(stored).FormatVersion);
                if (storedVersion == sourceVersion)
                    Assert.Equal(source, stored);
            }
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
                    Id = 101,
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
            Id = 101,
            MoveId = "move_a",
            StartTime = 0
        });
        timeline.Clips.Add(new MoveClip
        {
            Id = 102,
            MoveId = "move_b",
            StartTime = 24
        });
        timeline.Clips.Add(new MoveClip
        {
            Id = 103,
            MoveId = "move_a",
            StartTime = 48
        });

        return package;
    }

    private static MotionRecordingDocument CreateRecording(Guid songId, double endSeconds = 0.75)
    {
        MotionRecordingDocument recording = new()
        {
            RecordingId = Guid.NewGuid(),
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

    private static byte[] CreateGeneratedClassifier(MotionClassifierFormatVersion version, float duration)
    {
        IReadOnlyList<MotionSample> samples = CreateMotionSamples(duration);

        return new MotionClassifierGenerator().BuildClassifier(new MotionClassifierBuildRequest
        {
            SongName = "testmap",
            MoveName = "move_a",
            Examples = [new MotionExample(duration, samples)],
            Options = new MotionClassifierGenerationOptions
            {
                ClassifierFormatVersion = version,
                LowThreshold = 1.2f,
                HighThreshold = 2.5f,
                CustomizationBitField = 1
            }
        });
    }

    private static IReadOnlyList<MotionSample> CreateMotionSamples(float duration)
    {
        List<MotionSample> samples = [];
        for (int i = 0; i <= 60; i++)
        {
            float time = duration * i / 60.0f;
            samples.Add(new MotionSample(time, MathF.Sin(time * 4.0f), MathF.Cos(time * 3.0f), 0.25f + time));
        }

        return samples;
    }

    private static byte[] ConvertVersion5ToBigEndian(byte[] source)
    {
        byte[] result = [.. source];
        foreach (int offset in new[] { 0, 4, 200, 204, 208, 220, 224, 228, 232 })
            result.AsSpan(offset, sizeof(uint)).Reverse();
        result.AsSpan(212, sizeof(ulong)).Reverse();
        for (int offset = 236; offset < result.Length; offset += sizeof(float))
            result.AsSpan(offset, sizeof(float)).Reverse();
        return result;
    }

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