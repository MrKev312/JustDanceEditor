using JustDanceEditor.Formats.JDI.Timelines;
using JustDanceEditor.Scoring;

namespace JustDanceEditor.Formats.JDI.Recordings;

internal sealed record JdiMotionMoveWindow(
    int Index,
    long TimelineClipId,
    string MoveId,
    int MoveOccurrence,
    bool IsGoldMove,
    double StartBeatLabel,
    double EndBeatLabel,
    double StartSeconds,
    double DurationSeconds)
{
    public double EndSeconds => StartSeconds + DurationSeconds;
}

internal static class JdiMotionRecordingData
{
    public static MoveTimeline? FindHandMotionTimeline(IntermediateSongPackage package, int coachId)
        => package.CoachTimelines.FirstOrDefault(t => t.CoachId == coachId);

    public static List<JdiMotionMoveWindow> BuildMoveWindows(IntermediateSongPackage package, MoveTimeline timeline)
    {
        TimelineStructureDocument structure = package.TimelineStructure;
        List<JdiMotionMoveWindow> result = [];
        Dictionary<string, int> occurrences = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;

        foreach (MoveClip clip in timeline.Clips.OrderBy(c => c.StartTime))
        {
            if (string.IsNullOrWhiteSpace(clip.MoveId))
                continue;

            int occurrence = occurrences.GetValueOrDefault(clip.MoveId);
            occurrences[clip.MoveId] = occurrence + 1;

            int durationFrames = ResolveDurationFrames(package, clip.MoveId);
            double startBeatLabel = clip.StartTime / 24.0;
            double endBeatLabel = startBeatLabel + (durationFrames / 24.0);
            double startSeconds = GetSecondsAtBeatLabel(structure, startBeatLabel);
            double endSeconds = GetSecondsAtBeatLabel(structure, endBeatLabel);
            double durationSeconds = Math.Max(0, endSeconds - startSeconds);
            if (durationSeconds <= 0)
                continue;

            result.Add(new JdiMotionMoveWindow(index++, clip.Id, clip.MoveId, occurrence, clip.IsGoldMove, startBeatLabel, endBeatLabel, startSeconds, durationSeconds));
        }

        return result;
    }

    public static Dictionary<string, List<MotionExample>> BuildExamplesByMove(
        IReadOnlyList<MotionRecordingDocument> recordings,
        IReadOnlyList<JdiMotionMoveWindow> moveWindows,
        int coachId,
        MotionTrainingSelectionDocument? trainingSelection = null)
    {
        Dictionary<string, List<MotionExample>> examplesByMove = new(StringComparer.OrdinalIgnoreCase);

        foreach (MotionRecordingDocument recording in recordings.Where(r => r.CoachId == coachId))
        {
            if (recording.Samples.Count == 0)
                continue;

            foreach (JdiMotionMoveWindow move in moveWindows)
            {
                if (trainingSelection?.IsExcluded(
                        recording.RecordingId,
                        coachId,
                        move.TimelineClipId,
                        move.MoveId,
                        move.MoveOccurrence) == true)
                    continue;

                List<MotionSample> samples = BuildSmoothedSamples(recording.Samples, move.StartSeconds, move.DurationSeconds);
                if (samples.Count == 0)
                    continue;

                if (!examplesByMove.TryGetValue(move.MoveId, out List<MotionExample>? examples))
                {
                    examples = [];
                    examplesByMove.Add(move.MoveId, examples);
                }

                examples.Add(new MotionExample((float)move.DurationSeconds, samples));
            }
        }

        return examplesByMove;
    }

    public static List<MotionSample> BuildSmoothedSamples(
        IReadOnlyList<RecordedMotionSample> samples,
        double startTime,
        double duration)
    {
        List<RecordedMotionSample> chunks = FindSamplesAtTime(samples, startTime, duration, extend: true);
        List<MotionSample> result = [];
        List<MotionSample> group = [];
        double previousTime = -1000.0;

        for (int i = 0; i < chunks.Count; i++)
        {
            RecordedMotionSample sample = chunks[i];
            float time = ToRecordedSeconds(sample.MapTime);
            if (group.Count > 0 && (Math.Abs(group[0].Time - time) > float.Epsilon || i == chunks.Count - 1))
            {
                FlushGroup(result, group, previousTime, startTime, duration);
                previousTime = group[0].Time;
                group.Clear();
            }

            group.Add(new MotionSample(time, sample.AccX, sample.AccY, sample.AccZ));
        }

        if (group.Count > 0)
            FlushGroup(result, group, previousTime, startTime, duration);

        return result;
    }

    public static bool IsSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !value.Contains('/')
            && !value.Contains('\\')
            && value != "."
            && value != "..";
    }

    private static double GetSecondsAtBeatLabel(TimelineStructureDocument structure, double beatLabel)
        => structure.GetPlaybackSecondsAtBeat(beatLabel);

    private static void FlushGroup(
        List<MotionSample> result,
        List<MotionSample> group,
        double previousTime,
        double startTime,
        double duration)
    {
        if (previousTime != -1000.0)
        {
            double timeStep = (group[0].Time - previousTime) / group.Count;
            for (int j = 0; j < group.Count; j++)
            {
                double smoothTime = previousTime + ((j + 1) * timeStep);
                if (smoothTime >= startTime && smoothTime < startTime + duration)
                    result.Add(group[j] with { Time = (float)(smoothTime - startTime) });
            }
        }
        else
        {
            MotionSample last = group[^1];
            if (last.Time >= startTime && last.Time < startTime + duration)
                result.Add(last with { Time = (float)(last.Time - startTime) });
        }
    }

    private static List<RecordedMotionSample> FindSamplesAtTime(
        IReadOnlyList<RecordedMotionSample> samples,
        double startTime,
        double duration,
        bool extend)
    {
        List<RecordedMotionSample> result = [];
        double stopTime = startTime + duration;
        int counter = 0;
        int lastIndex = 0;

        for (int i = 0; i < samples.Count; i++)
        {
            RecordedMotionSample sample = samples[i];
            double sampleTime = ToRecordedSeconds(sample.MapTime);
            if (sampleTime >= startTime && sampleTime < stopTime)
            {
                if (extend)
                {
                    counter++;
                    if (counter == 1 && i > 0)
                        result.Add(samples[i - 1]);
                }

                result.Add(sample);
                lastIndex = i;
            }
        }

        if (extend && lastIndex != 0 && lastIndex < samples.Count - 1)
            result.Add(samples[lastIndex + 1]);

        return result;
    }

    private static int ResolveDurationFrames(IntermediateSongPackage package, string moveId)
    {
        if (package.HandCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? handDefinition)
            && handDefinition.Duration > 0)
        {
            return handDefinition.Duration;
        }

        if (package.FullBodyCoachMoves.TryGetValue(moveId, out CoachMoveDefinition? fullBodyDefinition)
            && fullBodyDefinition.Duration > 0)
        {
            return fullBodyDefinition.Duration;
        }

        return 24;
    }

    private static float ToRecordedSeconds(double mapTime)
    {
        if (mapTime <= 0)
            return (float)mapTime;

        uint milliseconds = (uint)(mapTime * 1000.0);
        return milliseconds * 0.001f;
    }
}
