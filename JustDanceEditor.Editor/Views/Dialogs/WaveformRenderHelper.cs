using Avalonia;
using Avalonia.Media;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Dialogs;

/// <summary>
/// Shared rendering helpers used by both WaveformBeatSelector and SectionWaveformEditor.
/// </summary>
internal static class WaveformRenderHelper
{
    /// <summary>
    /// Draws alternating light/dark measure backgrounds behind the waveform.
    /// Beats are always grouped in base groups of 4. The <paramref name="signatureBeats"/>
    /// value says how many of those groups form one alternating color block
    /// (block size = signatureBeats × 4).
    /// The pattern resets at each section boundary. If a section's length is not a
    /// multiple of the block size, the trailing partial group is drawn with
    /// <paramref name="errorBrush"/> (red). Both full and partial groups advance the
    /// color alternation for the next section.
    /// </summary>
    /// <param name="sectionStartBeats">
    /// Sorted list of section boundary beat labels (relative to beat 0).
    /// May be empty — a single unbounded section starting at the first visible beat is implied.
    /// </param>
    public static void DrawMeasureBackgrounds(
        DrawingContext context, double width, double height,
        double visibleStart, double visibleDuration,
        double zeroBeatTime, double bpm, int signatureBeats,
        IReadOnlyList<double> sectionStartBeats,
        IBrush evenBrush, IBrush oddBrush, IBrush errorBrush)
    {
        if (bpm <= 0 || signatureBeats <= 0 || visibleDuration <= 0)
            return;

        double beatDuration = 60.0 / bpm;
        int blockSize = signatureBeats * 4;
        double blockDuration = beatDuration * blockSize;
        double visibleEnd = visibleStart + visibleDuration;

        // Build section time intervals from beat labels
        // Each entry: (sectionStartTime, sectionEndTime)
        List<(double start, double end)> sectionIntervals = [];
        if (sectionStartBeats.Count == 0)
        {
            // No sections — one virtual section spanning everything
            sectionIntervals.Add((double.NegativeInfinity, double.PositiveInfinity));
        }
        else
        {
            for (int i = 0; i < sectionStartBeats.Count; i++)
            {
                double sStart = zeroBeatTime + (sectionStartBeats[i] * beatDuration);
                double sEnd = (i + 1 < sectionStartBeats.Count)
                    ? zeroBeatTime + (sectionStartBeats[i + 1] * beatDuration)
                    : double.PositiveInfinity;
                sectionIntervals.Add((sStart, sEnd));
            }
        }

        int colorIndex = 0; // 0 = even (light), 1 = odd (dark)

        foreach ((double sectionStart, double sectionEnd) in sectionIntervals)
        {
            // Skip sections entirely before visible area (still advance colorIndex)
            if (sectionEnd <= visibleStart)
            {
                if (sectionStart != double.NegativeInfinity && sectionEnd != double.PositiveInfinity)
                {
                    double sectionLen = sectionEnd - sectionStart;
                    int totalGroups = (int)Math.Ceiling((sectionLen / blockDuration) - 1e-9);
                    colorIndex += Math.Max(totalGroups, 0);
                }

                continue;
            }

            if (sectionStart >= visibleEnd)
                break;

            // Determine the drawing range within this section that overlaps the visible area
            double drawFrom = Math.Max(sectionStart, visibleStart);
            double drawTo = Math.Min(sectionEnd, visibleEnd);

            // The section's own origin for group counting
            double sectionOrigin = (sectionStart == double.NegativeInfinity) ? visibleStart : sectionStart;

            // Find the first group that overlaps drawFrom
            double groupOffset = drawFrom - sectionOrigin;
            int firstGroupIdx = (groupOffset <= 0) ? 0 : (int)Math.Floor(groupOffset / blockDuration);
            int sectionColorIndex = colorIndex;

            for (int g = firstGroupIdx; ; g++)
            {
                double gStart = sectionOrigin + (g * blockDuration);
                double gEnd = gStart + blockDuration;

                if (gStart >= drawTo || gStart >= sectionEnd)
                    break;

                // Check if this group is a partial (extends beyond the section boundary)
                bool isPartial = gEnd > sectionEnd + (beatDuration * 0.01);
                if (isPartial)
                    gEnd = sectionEnd; // clamp to section boundary

                // Clamp to visible area
                double rectStart = Math.Max(gStart, visibleStart);
                double rectEnd = Math.Min(gEnd, visibleEnd);
                if (rectStart >= rectEnd)
                    continue;

                double x1 = (rectStart - visibleStart) / visibleDuration * width;
                double x2 = (rectEnd - visibleStart) / visibleDuration * width;

                IBrush brush;
                if (isPartial)
                {
                    brush = errorBrush;
                }
                else
                {
                    bool isEven = (sectionColorIndex + g) % 2 == 0;
                    brush = isEven ? evenBrush : oddBrush;
                }

                context.FillRectangle(brush, new Rect(x1, 0, x2 - x1, height));
            }

            // Advance colorIndex for the next section: count ALL groups (full + partial)
            if (sectionEnd != double.PositiveInfinity && sectionStart != double.NegativeInfinity)
            {
                double sectionLen = sectionEnd - sectionOrigin;
                int totalGroups = (int)Math.Ceiling((sectionLen / blockDuration) - 1e-9);
                colorIndex += Math.Max(totalGroups, 0);
            }
        }
    }

    /// <summary>
    /// Determines whether <paramref name="beatIndex"/> is on a 4-beat group boundary
    /// (the base musical grouping) relative to the containing section's start beat.
    /// This is independent of the signature value — signatures control color block size,
    /// while grid lines always mark every 4 beats.
    /// </summary>
    public static bool IsMeasureBeat(int beatIndex, int beatsPerMeasure, IReadOnlyList<double> sectionStartBeats)
    {
        // Grid lines always mark every 4 beats (base group), regardless of signature
        const int baseGroup = 4;

        // Find the section that contains this beat
        double sectionStart = 0;
        for (int i = sectionStartBeats.Count - 1; i >= 0; i--)
        {
            if (beatIndex >= sectionStartBeats[i])
            {
                sectionStart = sectionStartBeats[i];
                break;
            }
        }

        double offset = beatIndex - sectionStart;
        return offset >= 0 && Math.Abs(offset % baseGroup) < 0.01;
    }

    /// <summary>
    /// Draws a horizontal scrollbar at the bottom of the control.
    /// Shows a track and a proportionally-sized thumb indicating the visible range.
    /// </summary>
    public static void DrawScrollbar(
        DrawingContext context, double width, double totalHeight,
        double waveformHeight, double scrollBarHeight,
        double duration, double viewStart, double viewEnd,
        IBrush trackBrush, IBrush thumbBrush)
    {
        if (duration <= 0 || width <= 0)
            return;

        double scrollY = waveformHeight;

        // Track background
        context.FillRectangle(trackBrush, new Rect(0, scrollY, width, scrollBarHeight));

        double visibleDuration = viewEnd - viewStart;
        if (visibleDuration >= duration)
            return; // no scrollbar needed when fully zoomed out

        // Thumb position and size
        double thumbLeft = viewStart / duration * width;
        double thumbWidth = Math.Max(visibleDuration / duration * width, 20); // minimum 20px

        // Clamp thumb
        if (thumbLeft + thumbWidth > width)
            thumbLeft = width - thumbWidth;

        double thumbY = scrollY + 2;
        double thumbH = scrollBarHeight - 4;

        context.FillRectangle(thumbBrush, new Rect(thumbLeft, thumbY, thumbWidth, thumbH));
    }
}
