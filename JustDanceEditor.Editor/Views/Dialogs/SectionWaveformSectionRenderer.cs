using Avalonia;
using Avalonia.Media;

using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace JustDanceEditor.Editor.Views.Dialogs;

internal static class SectionWaveformSectionRenderer
{
    private static readonly Pen SectionBorderPen = new(new SolidColorBrush(Colors.White, 0.8), 2);
    private static readonly Pen SelectedBorderPen = new(new SolidColorBrush(Colors.Yellow), 3);

    public static void DrawRegions(
        DrawingContext context,
        ObservableCollection<SectionEntry>? sections,
        IReadOnlyDictionary<SongSectionType, Color> colors,
        double bpm,
        double zeroBeatTime,
        int endBeat,
        double audioDuration,
        double width,
        double height,
        double visibleStart,
        double visibleEnd,
        double visibleDuration)
    {
        if (sections is not { Count: > 0 } || bpm <= 0)
            return;

        double beatDuration = 60.0 / bpm;
        for (int index = 0; index < sections.Count; index++)
        {
            SectionEntry section = sections[index];
            double startTime = zeroBeatTime + (section.StartBeat * beatDuration);
            double endTime = index + 1 < sections.Count
                ? zeroBeatTime + (sections[index + 1].StartBeat * beatDuration)
                : endBeat != 0 ? zeroBeatTime + (endBeat * beatDuration) : audioDuration;
            double drawStart = Math.Max(startTime, visibleStart);
            double drawEnd = Math.Min(endTime, visibleEnd);
            if (drawStart >= drawEnd)
                continue;

            double x1 = (drawStart - visibleStart) / visibleDuration * width;
            double x2 = (drawEnd - visibleStart) / visibleDuration * width;
            Color color = colors.GetValueOrDefault(section.SectionType, Colors.Gray);
            context.FillRectangle(new SolidColorBrush(color, 0.2), new Rect(x1, 0, x2 - x1, height));
        }
    }

    public static void DrawBoundaries(
        DrawingContext context,
        ObservableCollection<SectionEntry>? sections,
        SectionEntry? selectedSection,
        IReadOnlyDictionary<SongSectionType, Color> colors,
        double bpm,
        double zeroBeatTime,
        double width,
        double height,
        double visibleStart,
        double visibleDuration)
    {
        if (sections == null || bpm <= 0)
            return;

        double beatDuration = 60.0 / bpm;
        foreach (SectionEntry section in sections)
        {
            double time = zeroBeatTime + (section.StartBeat * beatDuration);
            double x = (time - visibleStart) / visibleDuration * width;
            if (x < -20 || x > width + 20)
                continue;

            bool selected = section == selectedSection;
            context.DrawLine(selected ? SelectedBorderPen : SectionBorderPen, new Point(x, 0), new Point(x, height));
            Color color = colors.GetValueOrDefault(section.SectionType, Colors.Gray);
            FormattedText label = new(
                section.SectionType.ToString(),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.Bold),
                10,
                selected ? Brushes.Yellow : new SolidColorBrush(color));
            context.DrawText(label, new Point(Math.Max(x + 3, 2), 2));
        }
    }
}
