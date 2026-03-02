using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Timeline;

/// <summary>
/// Shared static helpers for beat/measure calculations used by all timeline controls
/// (<see cref="AudioBarControl"/>, <see cref="TimeRulerControl"/>, <see cref="TimelineTrackPanel"/>).
/// </summary>
public static class TimelineRenderHelper
{
    /// <summary>
    /// Returns true if <paramref name="beat"/> falls on a 4-beat group boundary relative
    /// to the enclosing section start.
    /// </summary>
    public static bool IsBaseGroupBeat(double beat, List<double> sectionStarts)
    {
        const int baseGroup = 4;
        double sectionStart = 0;
        for (int i = sectionStarts.Count - 1; i >= 0; i--)
        {
            if (beat >= sectionStarts[i] - 0.01)
            {
                sectionStart = sectionStarts[i];
                break;
            }
        }

        double off = beat - sectionStart;
        return off >= -0.01 && Math.Abs(off % baseGroup) < 0.01;
    }

    /// <summary>
    /// Returns the active signature's colour-block size (sig.Beats × 4) at a given beat position.
    /// </summary>
    public static int GetActiveBlockSize(double pos, List<SignatureSegment> sortedSigs)
    {
        int beats = 4;
        foreach (SignatureSegment sig in sortedSigs)
        {
            if (sig.Marker <= pos + 0.01)
                beats = sig.Beats;
            else
                break;
        }

        return Math.Max(beats, 1) * 4;
    }

    /// <summary>
    /// Returns the beat position of the next signature change after <paramref name="pos"/>.
    /// Returns <see cref="double.MaxValue"/> if there is none.
    /// </summary>
    public static double GetNextSigChange(double pos, List<SignatureSegment> sortedSigs)
    {
        foreach (SignatureSegment sig in sortedSigs)
        {
            if (sig.Marker > pos + 0.01)
                return sig.Marker;
        }

        return double.MaxValue;
    }

    /// <summary>
    /// Counts all colour-groups (full and partial) in the beat range
    /// [<paramref name="sStart"/>, <paramref name="sEnd"/>], accounting for signature changes.
    /// </summary>
    public static int CountAllGroups(double sStart, double sEnd, List<SignatureSegment> sortedSigs)
    {
        int count = 0;
        double pos = sStart;
        while (pos < sEnd - 0.01)
        {
            int blockSize = GetActiveBlockSize(pos, sortedSigs);
            double gEnd = pos + blockSize;

            double nextSig = GetNextSigChange(pos, sortedSigs);
            if (nextSig < gEnd - 0.01 && nextSig < sEnd - 0.01)
                gEnd = nextSig;
            else if (gEnd > sEnd)
                gEnd = sEnd;

            count++;
            pos = gEnd;
        }

        return count;
    }
}
