using JustDanceEditor.Editor.ViewModels.Timeline;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Services;

public static class SnappingService
{
    // Pixel-based threshold to give a consistent feel regardless of zoom level
    public const double SnapThresholdPixels = 10.0;
    private const double EPS = 1e-9;

    public static double FindSnapBeat(double targetBeat, TimelineEditorViewModel vm, IEnumerable<ClipViewModel>? excludedClips = null)
    {
        if (vm == null)
            return targetBeat;

        double ppb = vm.PixelsPerBeat;
        if (ppb <= 0.0)
            return targetBeat;

        double thresholdBeats = SnapThresholdPixels / ppb;

        double best = targetBeat;
        double bestDist = double.MaxValue;
        int bestPriority = int.MaxValue; // 0=Playhead,1=Grid,2=Clip

        HashSet<ClipViewModel>? excluded = excludedClips != null ? [.. excludedClips] : null;

        // 1) Playhead
        if (vm.SnapToCurrentTimeMarker)
        {
            double candidate = vm.CurrentBeat;
            double d = Math.Abs(candidate - targetBeat);
            if (d + EPS < bestDist || (Math.Abs(d - bestDist) <= EPS && 0 < bestPriority))
            {
                bestDist = d;
                best = candidate;
                bestPriority = 0;
            }
        }

        // 2) Grid
        if (vm.SnapToGrid)
        {
            double grid = vm.SnapGridSize <= 0 ? 1.0 : vm.SnapGridSize;
            double candidate = Math.Round(targetBeat / grid) * grid;
            double d = Math.Abs(candidate - targetBeat);
            if (d + EPS < bestDist || (Math.Abs(d - bestDist) <= EPS && 1 < bestPriority))
            {
                bestDist = d;
                best = candidate;
                bestPriority = 1;
            }
        }

        // 3) Clips
        if (vm.SnapToClips)
        {
            foreach (ClipViewModel clip in vm.Tracks.SelectMany(static track => track.Clips).ToArray())
            {
                if (excluded != null && excluded.Contains(clip))
                    continue;

                double s = clip.StartBeat;
                double e = clip.StartBeat + clip.DurationBeats;

                double ds = Math.Abs(s - targetBeat);
                if (ds + EPS < bestDist || (Math.Abs(ds - bestDist) <= EPS && 2 < bestPriority))
                {
                    bestDist = ds;
                    best = s;
                    bestPriority = 2;
                }

                double de = Math.Abs(e - targetBeat);
                if (de + EPS < bestDist || (Math.Abs(de - bestDist) <= EPS && 2 < bestPriority))
                {
                    bestDist = de;
                    best = e;
                    bestPriority = 2;
                }
            }
        }

        if (bestDist < thresholdBeats + EPS)
            return best;

        return targetBeat;
    }
}
