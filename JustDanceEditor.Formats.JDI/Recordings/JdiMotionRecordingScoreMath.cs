namespace JustDanceEditor.Formats.JDI.Recordings;

internal static class JdiMotionRecordingScoreMath
{
    public static MotionRecordingMoveFeedback GetFeedback(bool isGoldMove, float percentageScore)
    {
        if (isGoldMove)
            return percentageScore > 70.0f
                ? MotionRecordingMoveFeedback.Yeah
                : MotionRecordingMoveFeedback.X;

        if (percentageScore < 25.0f)
            return MotionRecordingMoveFeedback.X;
        if (percentageScore < 50.0f)
            return MotionRecordingMoveFeedback.Ok;
        if (percentageScore < 70.0f)
            return MotionRecordingMoveFeedback.Good;
        if (percentageScore < 80.0f)
            return MotionRecordingMoveFeedback.Super;
        return MotionRecordingMoveFeedback.Perfect;
    }

    public static float NormalizePercentage(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0.0f;

        return Math.Clamp(value, 0.0f, 100.0f);
    }

    public static float GetAddedScore(
        bool isGoldMove,
        MotionRecordingMoveFeedback feedback,
        float percentageScore,
        float goldScoreValue,
        float moveScoreValue)
    {
        if (feedback == MotionRecordingMoveFeedback.X)
            return 0.0f;

        float scoreValue = isGoldMove && feedback == MotionRecordingMoveFeedback.Yeah
            ? goldScoreValue
            : moveScoreValue;
        return scoreValue * (percentageScore / 100.0f);
    }

    public static (float GoldScoreValue, float MoveScoreValue) GetScoreValues(
        IReadOnlyList<JdiMotionMoveWindow> moveWindows,
        MotionRecordingLiveScoringOptions options)
    {
        int goldCount = 0;
        int moveCount = 0;
        foreach (JdiMotionMoveWindow move in moveWindows)
        {
            if (move.IsGoldMove)
                goldCount++;
            else
                moveCount++;
        }

        float denominator = (options.GoldMoveValue * goldCount) + moveCount;
        if (denominator == 0.0f)
            return (0.0f, 0.0f);

        float moveValue = options.SongScoreMaxScore / denominator;
        return (moveValue * options.GoldMoveValue, moveValue);
    }
}