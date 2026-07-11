namespace JustDanceEditor.Scoring;

public sealed class LegacyMoveSpaceSongScorer
{
    private const float LegacyEnergyGate = 0.2f;

    public SongScoringResult ScoreSong(SongScoringRequest request)
    {
        SongScoringOptions options = request.Options;
        MoveSpaceScoreResult[] moveSpaceScores = ScoreMoveSpaces(request, options.MoveSpaceOptions with
        {
            DefaultAutoCorrelationThreshold = -1.0f,
            DefaultDirectionImpactFactor = -1.0f,
            FeedMode = MotionSampleFeedMode.LegacyToolInterpolation
        });

        (float goldScoreValue, float moveScoreValue) = GetScoreValues(request.Moves, options);
        List<SongMoveScore> scoredMoves = new(request.Moves.Count);
        float totalScore = 0.0f;

        for (int i = 0; i < request.Moves.Count; i++)
        {
            SongMoveScoringInput move = request.Moves[i];
            MoveSpaceScoreResult moveSpace = moveSpaceScores[i];
            float addedScore = 0.0f;
            DanceMoveFeedback feedback;

            if (moveSpace.EnergyAmount > LegacyEnergyGate)
            {
                addedScore = GetLegacyScore(move.IsGoldMove, moveScoreValue, goldScoreValue, moveSpace.PercentageScore);
                feedback = GetLegacyFeedback(move.IsGoldMove, moveSpace.PercentageScore);
            }
            else
            {
                feedback = move.IsGoldMove ? DanceMoveFeedback.MissYeah : DanceMoveFeedback.Miss;
            }

            totalScore += addedScore;
            scoredMoves.Add(new SongMoveScore(
                move.MoveName,
                move.IsGoldMove,
                feedback,
                moveSpace,
                moveSpace.PercentageScore,
                moveSpace.RatioScore,
                moveSpace.EnergyAmount,
                moveSpace.EnergyFactor,
                addedScore,
                totalScore));
        }

        return new SongScoringResult(
            SongScoringMethod.LegacyMoveSpace,
            scoredMoves,
            totalScore,
            options.SongScoreMaxScore == 0.0f ? 0.0f : totalScore / options.SongScoreMaxScore);
    }

    private static MoveSpaceScoreResult[] ScoreMoveSpaces(SongScoringRequest request, MoveScoringOptions moveOptions)
    {
        MoveSpaceScorer scorer = new();
        return request.Moves
            .AsParallel()
            .AsOrdered()
            .Select(move => scorer.ScoreMove(new MoveScoreRequest
            {
                MoveName = move.MoveName,
                ClassifierBytes = move.ClassifierBytes,
                Duration = move.Duration,
                Samples = move.Samples,
                Options = moveOptions
            }))
            .ToArray();
    }

    private static (float GoldScoreValue, float MoveScoreValue) GetScoreValues(
        IReadOnlyList<SongMoveScoringInput> moves,
        SongScoringOptions options)
    {
        int goldCount = 0;
        int moveCount = 0;
        foreach (SongMoveScoringInput move in moves)
        {
            if (move.IsGoldMove)
                goldCount++;
            else
                moveCount++;
        }

        float denominator = (options.LegacyToolGoldMoveValue * goldCount) + moveCount;
        if (denominator == 0.0f)
            return (0.0f, 0.0f);

        float moveValue = options.SongScoreMaxScore / denominator;
        return (moveValue * options.LegacyToolGoldMoveValue, moveValue);
    }

    private static float GetLegacyScore(bool isGoldMove, float moveScoreValue, float goldScoreValue, float percentage)
    {
        if (percentage <= 25.0f)
            return 0.0f;

        float scoreValue = isGoldMove && percentage > 70.0f
            ? goldScoreValue
            : moveScoreValue;
        return scoreValue * (percentage / 100.0f);
    }

    private static DanceMoveFeedback GetLegacyFeedback(bool isGoldMove, float percentage)
    {
        if (isGoldMove && percentage > 70.0f)
            return DanceMoveFeedback.Yeah;
        if (isGoldMove && percentage < 70.0f)
            return DanceMoveFeedback.MissYeah;
        if (percentage < 25.0f)
            return DanceMoveFeedback.Miss;
        if (percentage < 50.0f)
            return DanceMoveFeedback.Ok;
        if (percentage < 70.0f)
            return DanceMoveFeedback.Good;
        if (percentage < 80.0f)
            return DanceMoveFeedback.Super;
        return DanceMoveFeedback.Perfect;
    }
}