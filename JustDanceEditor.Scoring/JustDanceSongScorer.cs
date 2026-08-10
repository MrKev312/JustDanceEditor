namespace JustDanceEditor.Scoring;

public sealed class JustDanceSongScorer
{
    private const float MoveScoreBad = 0.0f;
    private const float MoveScoreOkCharity = 0.1f;
    private const float MoveScoreOk = 0.25f;
    private const float MoveScoreGood = 0.5f;
    private const float MoveScorePerfect = 0.75f;
    private const float MoveScoreMax = 1.0f;

    private const float JdNowWindowMargin = 0.075f;
    private const float JdNowWindowStep = 0.025f;
    private const float JdNowPhoneScoreBoost = 0.1f;
    private const float JdNowOkRatingFloor = 0.01f;
    private const float JdNowDefaultDirectionImpactFactor = 0.05f;
    private const float JdNowInitialMaxPositiveAccel = 2.0f;
    private const double JdNowTransformRotationRadians = Math.PI / 6.0;
    private const float JdNowTransformNonlinearStart = 1.0f;
    private const float JdNowTransformNonlinearRange = 1.25f;
    private const float JdNowTransformNonlinearScale = 0.5111111402511597f;
    private const float JdNowCharityEnergyFactorFloor = 0.6f;
    private const float JdNowPerfectMalusEnergyFactorCeil = 0.4f;
    private const float JdNowShakeDetectedMaxScoreRatio = 0.4f;
    private const float JdNowDirectionMalusMultiplier = 0.5f;
    private const float JdNowNoMovePenaltyEnergyAmountCeil = 0.25f;

    public SongScoringResult ScoreSong(SongScoringRequest request)
    {
        SongScoringOptions options = request.Options;
        MoveScoringOptions moveSpaceOptions = options.MoveSpaceOptions with
        {
            DefaultLowThreshold = 1.0f,
            DefaultHighThreshold = 3.0f,
            DefaultAutoCorrelationThreshold = 0.7f,
            DefaultDirectionImpactFactor = JdNowDefaultDirectionImpactFactor,
            FeedMode = MotionSampleFeedMode.ProgressRatioDirect
        };

        MoveSpaceScorer moveSpaceScorer = new();
        MoveSpaceScoreResult[] moveSpaces = request.TimelineSamples.Count == 0
            ? ScorePerMoveWindows(request.Moves, moveSpaceOptions, moveSpaceScorer)
            : ScoreNativeTimelineWindows(request, moveSpaceOptions, moveSpaceScorer);

        return BuildSongScore(request.Moves, moveSpaces, options);
    }

    private static SongScoringResult BuildSongScore(
        IReadOnlyList<SongMoveScoringInput> moves,
        IReadOnlyList<MoveSpaceScoreResult> moveSpaces,
        SongScoringOptions options)
    {
        float moveValue = CalculateJdNowMoveValue(moves, options);
        float totalScoreRatio = 0.0f;
        int onFireLevel = 0;
        List<SongMoveScore> scoredMoves = new(moves.Count);

        for (int moveIndex = 0; moveIndex < moves.Count; moveIndex++)
        {
            SongMoveScoringInput move = moves[moveIndex];
            MoveSpaceScoreResult moveSpace = moveSpaces[moveIndex];
            float finalMoveScore = SanitizeRatio(ComputeJdNowFinalMoveScore(moveSpace));
            DanceMoveFeedback feedback = GetJdNowFeedback(move.IsGoldMove, finalMoveScore);

            float onFireFactor = onFireLevel >= options.OnFireThreshold ? options.OnFireFactor : 1.0f;
            float addedScoreRatio = feedback switch
            {
                DanceMoveFeedback.Miss or DanceMoveFeedback.MissYeah => 0.0f,
                DanceMoveFeedback.Yeah => moveValue * options.GoldMoveValue,
                _ => moveValue * finalMoveScore * onFireFactor
            };
            addedScoreRatio = SanitizeFinite(addedScoreRatio, 0.0f);

            float addedScore = addedScoreRatio * options.SongScoreMaxScore;
            totalScoreRatio += addedScoreRatio;
            float totalScore = totalScoreRatio * options.SongScoreMaxScore;

            scoredMoves.Add(new SongMoveScore(
                move.MoveName,
                move.IsGoldMove,
                feedback,
                moveSpace,
                moveSpace.PercentageScore,
                finalMoveScore,
                moveSpace.EnergyAmount,
                moveSpace.EnergyFactor,
                addedScore,
                totalScore));

            onFireLevel = Math.Clamp(onFireLevel + GetJdNowOnFireModificator(feedback), 0, options.OnFireMax);
        }

        return new SongScoringResult(
            SongScoringMethod.JustDance,
            scoredMoves,
            totalScoreRatio * options.SongScoreMaxScore,
            totalScoreRatio);
    }

    private static MoveSpaceScoreResult[] ScoreNativeTimelineWindows(
        SongScoringRequest request,
        MoveScoringOptions moveSpaceOptions,
        MoveSpaceScorer moveSpaceScorer)
    {
        TimelineWindow[] windows = [.. request.Moves.Select(static move => new TimelineWindow(move))];

        MoveSpaceScoreResult[] moveSpaces = new MoveSpaceScoreResult[request.Moves.Count];
        float windowPointer = 0.5f;
        float maxPositiveAccel = JdNowInitialMaxPositiveAccel;
        int previousMoveIndex = -1;

        foreach (MotionSample rawSample in request.TimelineSamples)
        {
            MotionSample transformedSample = TransformSample(rawSample, ref maxPositiveAccel);
            for (int windowIndex = 0; windowIndex < windows.Length; windowIndex++)
                windows[windowIndex].AddSample(transformedSample);

            int currentMoveIndex = FindNativeMoveIndexAtTime(windows, transformedSample.Time);
            if (previousMoveIndex >= 0
                && previousMoveIndex != currentMoveIndex
                && !windows[previousMoveIndex].Scored
                && windows[previousMoveIndex].WindowEndTime <= transformedSample.Time
                && transformedSample.Time != windows[previousMoveIndex].WindowEndTime)
            {
                moveSpaces[previousMoveIndex] = ScoreTimelineWindow(
                    request.Moves[previousMoveIndex],
                    windows[previousMoveIndex],
                    moveSpaceOptions,
                    moveSpaceScorer,
                    ref windowPointer);
            }

            previousMoveIndex = currentMoveIndex;
        }

        for (int moveIndex = 0; moveIndex < request.Moves.Count; moveIndex++)
        {
            if (!windows[moveIndex].Scored)
            {
                moveSpaces[moveIndex] = ScoreTimelineWindow(
                    request.Moves[moveIndex],
                    windows[moveIndex],
                    moveSpaceOptions,
                    moveSpaceScorer,
                    ref windowPointer);
            }
        }

        return moveSpaces;
    }

    private static int FindNativeMoveIndexAtTime(IReadOnlyList<TimelineWindow> windows, float time)
    {
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].WindowStartTime <= time && time <= windows[i].WindowEndTime)
                return i;
        }

        return -1;
    }

    private static MoveSpaceScoreResult ScoreTimelineWindow(
        SongMoveScoringInput move,
        TimelineWindow window,
        MoveScoringOptions moveSpaceOptions,
        MoveSpaceScorer moveSpaceScorer,
        ref float windowPointer)
    {
        window.Scored = true;
        return ScorePointedSubwindows(
            move,
            window.NativeDuration,
            window.Subwindows,
            moveSpaceOptions,
            moveSpaceScorer,
            ref windowPointer);
    }

    private static MoveSpaceScoreResult[] ScorePerMoveWindows(
        IReadOnlyList<SongMoveScoringInput> moves,
        MoveScoringOptions moveSpaceOptions,
        MoveSpaceScorer moveSpaceScorer)
    {
        MoveSpaceScoreResult[] moveSpaces = new MoveSpaceScoreResult[moves.Count];
        float maxPositiveAccel = JdNowInitialMaxPositiveAccel;
        float windowPointer = 0.5f;

        for (int moveIndex = 0; moveIndex < moves.Count; moveIndex++)
        {
            SongMoveScoringInput move = moves[moveIndex];
            List<MotionSample> transformedSamples = TransformSamples(move.Samples, ref maxPositiveAccel);
            List<SubwindowSamples> subwindows = BuildRelativeSubwindows(move, transformedSamples);
            moveSpaces[moveIndex] = ScorePointedSubwindows(
                move,
                ToNativeSeconds(move.Duration),
                subwindows,
                moveSpaceOptions,
                moveSpaceScorer,
                ref windowPointer);
        }

        return moveSpaces;
    }

    private static MoveSpaceScoreResult ScorePointedSubwindows(
        SongMoveScoringInput move,
        float scoringDuration,
        IReadOnlyList<SubwindowSamples> subwindows,
        MoveScoringOptions options,
        MoveSpaceScorer scorer,
        ref float windowPointer)
    {
        if (subwindows.Count == 0)
            return ScoreEmptyMove(move, scoringDuration, options, scorer);

        MoveSpaceScoreResult[] scores = new MoveSpaceScoreResult[subwindows.Count];
        for (int i = 0; i < subwindows.Count; i++)
        {
            scores[i] = scorer.ScoreMove(new MoveScoreRequest
            {
                MoveName = move.MoveName,
                ClassifierBytes = move.ClassifierBytes,
                Duration = scoringDuration,
                Samples = subwindows[i].Samples,
                Options = options
            });
        }

        int selectedIndex = Math.Clamp((int)(windowPointer * scores.Length), 0, scores.Length - 1);
        int bestIndex = selectedIndex;
        float bestFinalScore = float.NegativeInfinity;
        for (int i = 0; i < scores.Length; i++)
        {
            float finalScore = ComputeJdNowFinalMoveScore(scores[i]);
            if (finalScore > bestFinalScore)
            {
                bestFinalScore = finalScore;
                bestIndex = i;
            }
        }

        if (bestIndex != selectedIndex)
        {
            float targetPointer = (float)bestIndex / scores.Length;
            windowPointer = Math.Clamp((targetPointer + (windowPointer * 9.0f)) / 10.0f, 0.0f, 1.0f);
        }

        return scores[selectedIndex];
    }

    private static MoveSpaceScoreResult ScoreEmptyMove(
        SongMoveScoringInput move,
        float scoringDuration,
        MoveScoringOptions options,
        MoveSpaceScorer scorer)
    {
        return scorer.ScoreMove(new MoveScoreRequest
        {
            MoveName = move.MoveName,
            ClassifierBytes = move.ClassifierBytes,
            Duration = scoringDuration,
            Samples = [],
            Options = options
        });
    }

    private static List<SubwindowSamples> BuildRelativeSubwindows(
        SongMoveScoringInput move,
        IReadOnlyList<MotionSample> transformedSamples)
    {
        float nativeDuration = ToNativeSeconds(move.Duration);
        List<SubwindowSamples> subwindows = [];
        for (float windowStart = -JdNowWindowMargin;
             windowStart + nativeDuration <= JdNowWindowMargin + nativeDuration;
             windowStart += JdNowWindowStep)
        {
            float windowEnd = windowStart + nativeDuration;
            SubwindowSamples subwindow = new();
            foreach (MotionSample sample in transformedSamples)
            {
                if (sample.Time >= windowStart && sample.Time <= windowEnd)
                    subwindow.Samples.Add(sample with { Time = subwindow.ProgressRatioAt(sample.Time) });
            }

            subwindows.Add(subwindow);
        }

        return subwindows;
    }

    private static List<MotionSample> TransformSamples(
        IReadOnlyList<MotionSample> samples,
        ref float maxPositiveAccel)
    {
        List<MotionSample> transformed = new(samples.Count);
        for (int i = 0; i < samples.Count; i++)
            transformed.Add(TransformSample(samples[i], ref maxPositiveAccel));
        return transformed;
    }

    private static MotionSample TransformSample(MotionSample sample, ref float maxPositiveAccel)
    {
        float cos = (float)Math.Cos(JdNowTransformRotationRadians);
        float sin = (float)Math.Sin(JdNowTransformRotationRadians);

        maxPositiveAccel = MathF.Max(maxPositiveAccel, sample.AccX);
        maxPositiveAccel = MathF.Max(maxPositiveAccel, sample.AccY);
        maxPositiveAccel = MathF.Max(maxPositiveAccel, sample.AccZ);

        float x = (cos * sample.AccX) - ((-sample.AccY) * sin);
        float y = -sample.AccZ;
        float z = ((-sample.AccY) * cos) + (sin * sample.AccX);

        if (maxPositiveAccel < 3.4f)
        {
            x = ApplyJdNowNonlinearAcceleration(x);
            y = ApplyJdNowNonlinearAcceleration(y);
            z = ApplyJdNowNonlinearAcceleration(z);
        }

        return new MotionSample(
            sample.Time,
            Math.Clamp(x, -3.4f, 3.4f),
            Math.Clamp(y, -3.4f, 3.4f),
            Math.Clamp(z, -3.4f, 3.4f));
    }

    private static float ApplyJdNowNonlinearAcceleration(float value)
    {
        float absolute = MathF.Abs(value);
        if (absolute <= JdNowTransformNonlinearStart)
            return value;

        float scale = ((absolute - JdNowTransformNonlinearStart) * JdNowTransformNonlinearScale
            / JdNowTransformNonlinearRange)
            + JdNowTransformNonlinearStart;
        return scale * value;
    }

    private static float ComputeJdNowFinalMoveScore(MoveSpaceScoreResult moveSpace)
    {
        float ratioScore = SanitizeRatio(moveSpace.RatioScore);
        float energyAmount = SanitizeFinite(moveSpace.EnergyAmount, 0.0f);
        float energyFactor = SanitizeFinite(moveSpace.EnergyFactor, 0.0f);
        float directionImpact = SanitizeFinite(moveSpace.DirectionTendencyImpactOnScoreRatio, 0.0f);

        float score = Math.Min(ratioScore + (ratioScore * JdNowPhoneScoreBoost), MoveScoreMax);
        bool charityBonusIsAllowed = true;

        if (score < MoveScoreOk)
        {
            score = MoveScoreBad;
        }
        else if (energyAmount < JdNowNoMovePenaltyEnergyAmountCeil)
        {
            score = MoveScoreBad;
            charityBonusIsAllowed = false;
        }
        else if (moveSpace.AutoCorrelationTime > 0.0f)
        {
            score = Math.Min(score, JdNowShakeDetectedMaxScoreRatio);
        }
        else if (!moveSpace.DirectionTendencyIgnored)
        {
            if (directionImpact < 0.0f)
            {
                score += directionImpact * JdNowDirectionMalusMultiplier;
                if (score < MoveScoreOk)
                    score = MoveScoreBad;
            }
            else if (directionImpact > 0.0f)
            {
                score = Math.Min(score + (directionImpact * JdNowPhoneScoreBoost), MoveScoreMax);
            }
        }

        if (score < MoveScoreOkCharity)
        {
            if (charityBonusIsAllowed && energyFactor >= JdNowCharityEnergyFactorFloor)
                score = MoveScoreOkCharity;
        }
        else if (score >= MoveScorePerfect && energyFactor < JdNowPerfectMalusEnergyFactorCeil)
        {
            score = MoveScorePerfect - 0.01f;
        }

        return SanitizeRatio(score);
    }

    private static float CalculateJdNowMoveValue(
        IReadOnlyList<SongMoveScoringInput> moves,
        SongScoringOptions options)
    {
        int standardMoveCount = 0;
        int goldMoveCount = 0;
        foreach (SongMoveScoringInput move in moves)
        {
            if (move.IsGoldMove)
                goldMoveCount++;
            else
                standardMoveCount++;
        }

        float maximumScore =
            ((standardMoveCount - 5) * options.OnFireFactor)
            + (goldMoveCount * options.GoldMoveValue)
            + 5.0f;

        return standardMoveCount > 5 && maximumScore != 0.0f
            ? 1.0f / maximumScore
            : 0.0f;
    }

    private static DanceMoveFeedback GetJdNowFeedback(bool isGoldMove, float score)
    {
        if (score < JdNowOkRatingFloor)
            return isGoldMove ? DanceMoveFeedback.MissYeah : DanceMoveFeedback.Miss;
        if (isGoldMove && score >= MoveScoreGood)
            return DanceMoveFeedback.Yeah;
        if (score >= MoveScorePerfect)
            return DanceMoveFeedback.Perfect;
        if (score >= MoveScoreGood)
            return DanceMoveFeedback.Good;
        return isGoldMove ? DanceMoveFeedback.MissYeah : DanceMoveFeedback.Ok;
    }

    private static int GetJdNowOnFireModificator(DanceMoveFeedback feedback)
    {
        return feedback switch
        {
            DanceMoveFeedback.Miss or DanceMoveFeedback.MissYeah => -4,
            DanceMoveFeedback.Ok => -2,
            DanceMoveFeedback.Good => 1,
            DanceMoveFeedback.Perfect or DanceMoveFeedback.Yeah => 2,
            _ => 0
        };
    }

    private static float ToNativeSeconds(float seconds)
    {
        return (float)(int)(seconds * 1000.0f) / 1000.0f;
    }

    private static float SanitizeRatio(float value)
        => Math.Clamp(SanitizeFinite(value, 0.0f), 0.0f, MoveScoreMax);

    private static float SanitizeFinite(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    private sealed class TimelineWindow
    {
        private readonly float _startTime;
        private readonly float _endTime;

        public TimelineWindow(SongMoveScoringInput move)
        {
            float nativeStartTime = ToNativeSeconds(move.StartTime);
            NativeDuration = ToNativeSeconds(move.Duration);
            _startTime = nativeStartTime - JdNowWindowMargin;
            _endTime = nativeStartTime + NativeDuration + JdNowWindowMargin;
            WindowStartTime = _startTime;
            WindowEndTime = _endTime;

            for (float windowStart = _startTime;
                 windowStart + NativeDuration <= _endTime;
                 windowStart += JdNowWindowStep)
            {
                Subwindows.Add(new SubwindowSamples(windowStart, windowStart + NativeDuration));
            }
        }

        public float NativeDuration { get; }
        public float WindowStartTime { get; }
        public float WindowEndTime { get; }
        public bool Scored { get; set; }

        public List<SubwindowSamples> Subwindows { get; } = [];

        public void AddSample(MotionSample sample)
        {
            if (sample.Time < _startTime || sample.Time > _endTime)
                return;

            foreach (SubwindowSamples subwindow in Subwindows)
            {
                if (subwindow.Contains(sample.Time))
                    subwindow.Samples.Add(sample with { Time = subwindow.ProgressRatioAt(sample.Time) });
            }
        }
    }

    private sealed class SubwindowSamples
    {
        public SubwindowSamples()
        {
        }

        public SubwindowSamples(float startTime, float endTime)
        {
            StartTime = startTime;
            EndTime = endTime;
        }

        public float StartTime { get; }
        public float EndTime { get; }
        public List<MotionSample> Samples { get; } = [];

        public bool Contains(float time)
        {
            return time >= StartTime && time <= EndTime;
        }

        public float ProgressRatioAt(float time)
        {
            return (time - StartTime) / (EndTime - StartTime);
        }
    }
}
