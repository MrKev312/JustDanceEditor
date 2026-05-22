using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Core;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Particles;
using JustDanceEditor.Formats.UbiArt.Import.Cinematics.Rendering;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy;
using JustDanceEditor.Formats.UbiArt.Serialization.Legacy.Cinematics;

using System.Collections.Concurrent;

namespace JustDanceEditor.Formats.UbiArt.Import.Cinematics.Timeline;

internal sealed class PropertyClipIndex(
    IReadOnlyDictionary<string, List<PropertyClip>> byActorKey,
    IReadOnlyList<SourceEvaluationClip>? sourceClipPlayerOrder = null)
{
    public static PropertyClipIndex Empty { get; } = new(new Dictionary<string, List<PropertyClip>>(StringComparer.OrdinalIgnoreCase));
    private readonly IReadOnlyList<SourceEvaluationClip> sourceClipPlayerOrder = sourceClipPlayerOrder ?? [];
    private readonly ConcurrentDictionary<long, IReadOnlyDictionary<int, int>> evaluationRanksByFrame = new();

    public bool HasClips(LegacyCinematicActor actor) => byActorKey.ContainsKey(actor.Key);

    public IEnumerable<PropertyClip> GetClips(LegacyCinematicActor actor, double frame)
    {
        if (!byActorKey.TryGetValue(actor.Key, out List<PropertyClip>? clips))
            yield break;

        foreach (PropertyClip clip in EnumerateClipsThroughFrameInEngineOrder(clips, frame))
        {
            if (ShouldIgnoreInheritedVisibilityClip(actor, clip))
                continue;

            if (clip.State.Transform != null || clip.State.Material != null)
                yield return clip;
        }
    }

    public IEnumerable<PropertyClip> GetMaterialGraphicClips(LegacyCinematicActor actor, double frame)
    {
        if (!byActorKey.TryGetValue(actor.Key, out List<PropertyClip>? clips))
            yield break;

        foreach (PropertyClip clip in EnumerateClipsThroughFrameInEngineOrder(clips, frame))
        {
            if (clip.State.MaterialGraphic != null || clip.State.LayerEnable != null)
                yield return clip;
        }
    }

    public double GetDefaultFxActivationFrame(LegacyCinematicActor actor, double frame, double alphaThreshold)
    {
        if (!byActorKey.TryGetValue(actor.Key, out List<PropertyClip>? clips))
            return 0.0;

        double activationFrame = 0.0;
        foreach (PropertyClip clip in EnumerateClipsThroughFrameInEngineOrder(clips, frame))
        {
            if (!LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(clip.TypeId) ||
                clip.State.Material?.Alpha is not { } alphaCurve)
            {
                continue;
            }

            double localFrame = LegacyCinematicRenderEngine.GetClipLocalFrame(clip, frame);
            double alpha = Math.Clamp(LegacyCinematicRenderEngine.EvaluateCurve(alphaCurve, localFrame), 0.0, 1.0);
            if (alpha <= alphaThreshold)
                continue;

            activationFrame = EstimateAlphaActivationFrame(clip, alphaCurve, alphaThreshold);
        }

        return activationFrame;
    }

    public IReadOnlySet<uint> GetActiveFxNameIds(LegacyCinematicActor actor, double frame)
    {
        HashSet<uint>? active = null;
        foreach (ActiveFxClip clip in GetActiveFxClips(actor, frame))
        {
            active ??= [];
            active.Add(clip.NameId);
        }

        return active ?? LegacyCinematicFxIds.EmptySet;
    }

    public IReadOnlyList<ActiveFxClip> GetActiveFxClips(LegacyCinematicActor actor, double frame)
    {
        if (!byActorKey.TryGetValue(actor.Key, out List<PropertyClip>? clips))
            return [];

        List<ActiveFxClip>? active = null;
        foreach (PropertyClip clip in clips)
        {
            if (!LegacyBinarySerializer.IsTypeId<LegacyCinematicFxClipBinary>(clip.TypeId) ||
                !LegacyCinematicFxIds.IsValid(clip.FxNameId) ||
                frame < clip.StartFrame)
            {
                continue;
            }

            bool isInClipRange = frame < GetEndFrame(clip);
            if (!isInClipRange && clip.KillParticlesOnEnd)
                continue;

            active ??= [];
            active.Add(new ActiveFxClip(
                clip.FxNameId,
                clip.StartFrame,
                clip.DurationFrames,
                isInClipRange,
                clip.KillParticlesOnEnd));
        }

        return active ?? [];
    }

    private static bool ShouldApplyClipOverride(PropertyClip? current, PropertyClip candidate, double frame)
    {
        if (current == null)
            return true;

        double candidateEvaluationEnd = GetEvaluationEndFrame(candidate, frame);
        double currentEvaluationEnd = GetEvaluationEndFrame(current, frame);
        if (candidateEvaluationEnd > currentEvaluationEnd + 0.0001)
            return true;

        if (candidateEvaluationEnd < currentEvaluationEnd - 0.0001)
            return false;

        int candidateEnd = GetEndFrame(candidate);
        int currentEnd = GetEndFrame(current);
        if (candidateEnd != currentEnd)
            return candidateEnd > currentEnd;

        return candidate.Order > current.Order;
    }

    private static double GetEvaluationEndFrame(PropertyClip clip, double frame) =>
        Math.Min(frame, GetEndFrame(clip));

    private static double GetEvaluationEndFrame(SourceEvaluationClip clip, double frame) =>
        Math.Min(frame, GetEndFrame(clip));

    private static int GetEndFrame(PropertyClip clip) =>
        clip.StartFrame + Math.Max(clip.DurationFrames, 0);

    private static int GetEndFrame(SourceEvaluationClip clip) =>
        clip.StartFrame + Math.Max(clip.DurationFrames, 0);

    private static bool ShouldIgnoreInheritedVisibilityClip(LegacyCinematicActor actor, PropertyClip clip) =>
        clip.IsResolvedFromAncestor &&
        LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(clip.TypeId) &&
        LegacyCinematicParticleSimulator.HasPersistentBillboardDefaultFx(actor);

    private static double EstimateAlphaActivationFrame(PropertyClip clip, CinematicCurve curve, double alphaThreshold)
    {
        if (clip.DurationFrames <= 0 || curve.Keyframes.Count == 0)
            return clip.StartFrame;

        const int steps = 32;
        double previousLocalFrame = 0.0;
        if (Math.Clamp(LegacyCinematicRenderEngine.EvaluateCurve(curve, 0.0), 0.0, 1.0) > alphaThreshold)
            return clip.StartFrame;

        for (int step = 1; step <= steps; step++)
        {
            double localFrame = clip.DurationFrames * (step / (double)steps);
            double alpha = Math.Clamp(LegacyCinematicRenderEngine.EvaluateCurve(curve, localFrame), 0.0, 1.0);
            if (alpha <= alphaThreshold)
            {
                previousLocalFrame = localFrame;
                continue;
            }

            double low = previousLocalFrame;
            double high = localFrame;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                double mid = (low + high) * 0.5;
                double midAlpha = Math.Clamp(LegacyCinematicRenderEngine.EvaluateCurve(curve, mid), 0.0, 1.0);
                if (midAlpha > alphaThreshold)
                    high = mid;
                else
                    low = mid;
            }

            return clip.StartFrame + high;
        }

        return clip.StartFrame;
    }

    private IEnumerable<PropertyClip> EnumerateClipsThroughFrameInEngineOrder(
        IReadOnlyList<PropertyClip> clips,
        double frame)
    {
        if (sourceClipPlayerOrder.Count == 0)
        {
            return clips
                .Where(clip => clip.StartFrame <= frame)
                .OrderBy(clip => GetEvaluationEndFrame(clip, frame))
                .ThenBy(GetEndFrame)
                .ThenBy(clip => clip.Order);
        }

        IReadOnlyDictionary<int, int> ranks = GetEvaluationRanks(frame);
        return clips
            .Where(clip => clip.StartFrame <= frame)
            .OrderBy(clip => ranks.TryGetValue(clip.Order, out int rank) ? rank : int.MaxValue)
            .ThenBy(clip => clip.Order);
    }

    private IReadOnlyDictionary<int, int> GetEvaluationRanks(double frame)
    {
        long frameKey = BitConverter.DoubleToInt64Bits(frame);
        return evaluationRanksByFrame.GetOrAdd(frameKey, _ => BuildEvaluationRanks(frame));
    }

    private IReadOnlyDictionary<int, int> BuildEvaluationRanks(double frame)
    {
        if (sourceClipPlayerOrder.Count == 0)
            return new Dictionary<int, int>();

        List<SourceEvaluationClip> clips = [];
        foreach (SourceEvaluationClip clip in sourceClipPlayerOrder)
        {
            if (clip.StartFrame <= frame)
                clips.Add(clip);
        }

        ItfSortByClampedEndTime(clips, frame);

        Dictionary<int, int> ranks = new(clips.Count);
        for (int index = 0; index < clips.Count; index++)
        {
            if (clips[index].PropertyOrder is { } propertyOrder)
                ranks[propertyOrder] = index;
        }

        return ranks;
    }

    private static void ItfSortByClampedEndTime(List<SourceEvaluationClip> clips, double frame)
    {
        if (clips.Count <= 1)
            return;

        int depth = clips.Count > 0
            ? (int)(2.0 * (Math.Log(clips.Count) / Math.Log(2.0)))
            : 0;
        ItfIntrosortLoop(clips, 0, clips.Count, depth, frame);
        ItfFinalInsertionSort(clips, 0, clips.Count, frame);
    }

    private static void ItfIntrosortLoop(List<SourceEvaluationClip> clips, int first, int last, int depth, double frame)
    {
        while (last - first > 16)
        {
            if (depth == 0)
            {
                clips.Sort(first, last - first, Comparer<SourceEvaluationClip>.Create((left, right) =>
                    IsBeforeByClampedEndTime(left, right, frame) ? -1 :
                    IsBeforeByClampedEndTime(right, left, frame) ? 1 :
                    0));
                return;
            }

            depth--;
            SourceEvaluationClip pivot = Median(
                clips[first],
                clips[first + ((last - first) / 2)],
                clips[last - 1],
                frame);
            int middle = ItfUnguardedPartition(clips, first, last, pivot, frame);
            ItfIntrosortLoop(clips, middle, last, depth, frame);
            last = middle;
        }
    }

    private static int ItfUnguardedPartition(List<SourceEvaluationClip> clips, int first, int last, SourceEvaluationClip pivot, double frame)
    {
        while (true)
        {
            while (IsBeforeByClampedEndTime(clips[first], pivot, frame))
                first++;

            last--;

            while (IsBeforeByClampedEndTime(pivot, clips[last], frame))
                last--;

            if (first >= last)
                break;

            (clips[first], clips[last]) = (clips[last], clips[first]);
            first++;
        }

        return first;
    }

    private static void ItfFinalInsertionSort(List<SourceEvaluationClip> clips, int first, int last, double frame)
    {
        if (last - first > 16)
        {
            ItfInsertionSort(clips, first, first + 16, frame);
            ItfUnguardedInsertionSort(clips, first + 16, last, frame);
        }
        else
        {
            ItfInsertionSort(clips, first, last, frame);
        }
    }

    private static void ItfInsertionSort(List<SourceEvaluationClip> clips, int first, int last, double frame)
    {
        if (first == last)
            return;

        for (int index = first + 1; index < last; index++)
        {
            SourceEvaluationClip value = clips[index];
            if (IsBeforeByClampedEndTime(value, clips[first], frame))
            {
                for (int shift = index; shift > first; shift--)
                    clips[shift] = clips[shift - 1];
                clips[first] = value;
            }
            else
            {
                ItfUnguardedLinearInsert(clips, index, value, frame);
            }
        }
    }

    private static void ItfUnguardedInsertionSort(List<SourceEvaluationClip> clips, int first, int last, double frame)
    {
        while (first != last)
        {
            SourceEvaluationClip value = clips[first];
            ItfUnguardedLinearInsert(clips, first, value, frame);
            first++;
        }
    }

    private static void ItfUnguardedLinearInsert(List<SourceEvaluationClip> clips, int last, SourceEvaluationClip value, double frame)
    {
        int next = last - 1;
        while (IsBeforeByClampedEndTime(value, clips[next], frame))
        {
            clips[last] = clips[next];
            last = next;
            next--;
        }

        clips[last] = value;
    }

    private static SourceEvaluationClip Median(SourceEvaluationClip a, SourceEvaluationClip b, SourceEvaluationClip c, double frame)
    {
        if (IsBeforeByClampedEndTime(a, b, frame))
            return IsBeforeByClampedEndTime(b, c, frame) ? b : (IsBeforeByClampedEndTime(a, c, frame) ? c : a);

        return IsBeforeByClampedEndTime(a, c, frame) ? a : (IsBeforeByClampedEndTime(b, c, frame) ? c : b);
    }

    private static bool IsBeforeByClampedEndTime(SourceEvaluationClip left, SourceEvaluationClip right, double frame)
    {
        double leftTime = GetEvaluationEndFrame(left, frame);
        double rightTime = GetEvaluationEndFrame(right, frame);
        if (leftTime < rightTime)
            return true;

        if (leftTime > rightTime)
            return false;

        if (ShouldOrderBroadRecursiveClipBeforeNarrow(left, right))
            return true;

        return false;
    }

    private static bool ShouldOrderBroadRecursiveClipBeforeNarrow(SourceEvaluationClip left, SourceEvaluationClip right)
    {
        if (left.PropertyOrder == null ||
            right.PropertyOrder == null ||
            left.TypeId != right.TypeId ||
            left.ResolvedActorCount <= 0 ||
            left.ResolvedActorCount == right.ResolvedActorCount)
        {
            return false;
        }

        if (!LegacyBinarySerializer.IsTypeId<LegacyCinematicAlphaClipBinary>(left.TypeId) &&
            !LegacyBinarySerializer.IsTypeId<LegacyCinematicMaterialColorClipBinary>(left.TypeId))
        {
            return false;
        }

        return left.ResolvedActorCount > right.ResolvedActorCount;
    }
}