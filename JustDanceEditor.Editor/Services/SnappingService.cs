using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Services;

public static class SnappingService
{
    public static double ChooseBestStart(double unconstrainedStart, double duration, TimelineEditorViewModel vm, IEnumerable<ClipViewModel> allClips)
    {
        if (vm == null)
            return unconstrainedStart;

        var candidates = new List<double>();

        if (vm.SnapToGrid)
        {
            double grid = vm.SnapGridSize <= 0 ? 1.0 : vm.SnapGridSize;
            candidates.Add(Math.Floor(unconstrainedStart / grid) * grid);
            candidates.Add(Math.Round(unconstrainedStart / grid) * grid);
            candidates.Add(Math.Ceiling(unconstrainedStart / grid) * grid);

            double end = unconstrainedStart + duration;
            candidates.Add((Math.Floor(end / grid) * grid) - duration);
            candidates.Add((Math.Round(end / grid) * grid) - duration);
            candidates.Add((Math.Ceiling(end / grid) * grid) - duration);
        }

        if (vm.SnapToCurrentTimeMarker)
        {
            candidates.Add(vm.CurrentBeat);
            candidates.Add(vm.CurrentBeat - duration);
        }

        if (vm.SnapToClips && allClips != null)
        {
            try
            {
                foreach (ClipViewModel other in allClips)
                {
                    if (other == null)
                        continue;
                    double oStart = other.StartBeat;
                    double oEnd = other.StartBeat + other.DurationBeats;
                    candidates.Add(oStart);
                    candidates.Add(oEnd);
                    candidates.Add(oStart - duration);
                    candidates.Add(oEnd - duration);
                }
            }
            catch { }
        }

        if (candidates.Count == 0)
            return unconstrainedStart;

        double best = unconstrainedStart;
        double bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            double d = Math.Abs(c - unconstrainedStart);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        if (vm.SnapThreshold > 0 && Math.Abs(best - unconstrainedStart) <= vm.SnapThreshold)
            return best;

        return unconstrainedStart;
    }

    public static double ChooseBestEnd(double unconstrainedEnd, double start, TimelineEditorViewModel vm, IEnumerable<ClipViewModel> allClips)
    {
        if (vm == null)
            return unconstrainedEnd;

        var candidates = new List<double>();

        if (vm.SnapToGrid)
        {
            double grid = vm.SnapGridSize <= 0 ? 1.0 : vm.SnapGridSize;
            candidates.Add(Math.Floor(unconstrainedEnd / grid) * grid);
            candidates.Add(Math.Round(unconstrainedEnd / grid) * grid);
            candidates.Add(Math.Ceiling(unconstrainedEnd / grid) * grid);
        }

        if (vm.SnapToCurrentTimeMarker)
        {
            candidates.Add(vm.CurrentBeat);
        }

        if (vm.SnapToClips && allClips != null)
        {
            try
            {
                foreach (ClipViewModel other in allClips)
                {
                    if (other == null)
                        continue;
                    double oStart = other.StartBeat;
                    double oEnd = other.StartBeat + other.DurationBeats;
                    candidates.Add(oStart);
                    candidates.Add(oEnd);
                }
            }
            catch { }
        }

        if (candidates.Count == 0)
            return unconstrainedEnd;

        double best = unconstrainedEnd;
        double bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            double d = Math.Abs(c - unconstrainedEnd);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        if (vm.SnapThreshold > 0 && Math.Abs(best - unconstrainedEnd) <= vm.SnapThreshold)
            return best;

        return unconstrainedEnd;
    }

    // New helper for left-edge resizing where the clip's end is preserved — only consider start-alignment candidates
    public static double ChooseBestStartPreserveEnd(double unconstrainedStart, double fixedEnd, TimelineEditorViewModel vm, IEnumerable<ClipViewModel> allClips)
    {
        if (vm == null)
            return unconstrainedStart;

        var candidates = new List<double>();

        if (vm.SnapToGrid)
        {
            double grid = vm.SnapGridSize <= 0 ? 1.0 : vm.SnapGridSize;
            candidates.Add(Math.Floor(unconstrainedStart / grid) * grid);
            candidates.Add(Math.Round(unconstrainedStart / grid) * grid);
            candidates.Add(Math.Ceiling(unconstrainedStart / grid) * grid);
        }

        if (vm.SnapToCurrentTimeMarker)
        {
            candidates.Add(vm.CurrentBeat);
        }

        if (vm.SnapToClips && allClips != null)
        {
            try
            {
                foreach (ClipViewModel other in allClips)
                {
                    if (other == null)
                        continue;
                    double oStart = other.StartBeat;
                    double oEnd = other.StartBeat + other.DurationBeats;
                    // align start to other starts/ends
                    candidates.Add(oStart);
                    candidates.Add(oEnd);
                }
            }
            catch { }
        }

        if (candidates.Count == 0)
            return unconstrainedStart;

        double best = unconstrainedStart;
        double bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            double d = Math.Abs(c - unconstrainedStart);
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }

        if (vm.SnapThreshold > 0 && Math.Abs(best - unconstrainedStart) <= vm.SnapThreshold)
            return best;

        return unconstrainedStart;
    }
}
