using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Recolor Moves by Section", "Timeline")]
public class RecolorMovesBySectionCommand : IRunCommand
{
    public bool CanRun(ITimelineContextService? timelineContext) => timelineContext?.ActiveTimeline != null;

    public void Run(ITimelineContextService? timelineContext)
    {
        var timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        // Build section color map from SongSectionType attributes (same logic as AudioBarControl)
        var sectionColors = new Dictionary<SongSectionType, Color>();
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
            var attr = field?.GetCustomAttribute<ColorAttribute>();
            if (attr != null)
                sectionColors[type] = Color.FromRgb(attr.R, attr.G, attr.B);
            else
                sectionColors[type] = Colors.Gray;
        }

        // Build section spans (start/end) for overlap calculations
        var sections = timeline.TimelineStructure.Sections.OrderBy(s => s.StartBeat).ToList();
        var sectionSpans = new List<(double Start, double End, SongSectionType Type)>();
        for (int i = 0; i < sections.Count; i++)
        {
            double start = sections[i].StartBeat;
            double end = (i + 1 < sections.Count) ? sections[i + 1].StartBeat : timeline.TimelineStructure.EndBeat;
            sectionSpans.Add((start, end, sections[i].SectionType));
        }

        // Gather all movement clips (both hand and full body)
        var allMoveClips = timeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().ToList();
        if (allMoveClips.Count == 0)
            return;

        // For each MoveId, compute total overlap per section type across ALL instances
        var baseSectionForMove = new Dictionary<string, SongSectionType>(StringComparer.OrdinalIgnoreCase);
        var baseColorForMove = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        foreach (var grp in allMoveClips.GroupBy(c => c.MoveId, StringComparer.OrdinalIgnoreCase))
        {
            var totals = new Dictionary<SongSectionType, double>();
            foreach (var clip in grp)
            {
                double clipStart = clip.StartBeat;
                double clipEnd = clip.StartBeat + clip.DurationBeats;
                foreach (var span in sectionSpans)
                {
                    double overlap = Math.Max(0.0, Math.Min(clipEnd, span.End) - Math.Max(clipStart, span.Start));
                    if (overlap <= 0) continue;
                    if (!totals.TryGetValue(span.Type, out var cur)) cur = 0.0;
                    totals[span.Type] = cur + overlap;
                }
            }

            // choose the section type with the maximum overlap; fallback to first section
            SongSectionType chosen = totals.Count > 0 ? totals.OrderByDescending(kv => kv.Value).First().Key : sectionSpans.First().Type;
            baseSectionForMove[grp.Key] = chosen;
            baseColorForMove[grp.Key] = sectionColors.TryGetValue(chosen, out var c) ? c : Colors.Gray;
        }

        // We'll compute colors and apply them across both body types, but determine brightness alternation per body type sequence
        var assigned = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

        // Prepare list of all target clips and original colors for a single undo group
        var targetClips = allMoveClips.OrderBy(c => c.StartBeat).ToList();
        var originalColors = targetClips.Select(c => c.BackgroundColor).ToList();

        // Helper to get bright/dark variant
        static Color Variant(Color baseColor, bool brighten) => brighten ? Brighten(baseColor, 0.20) : Darken(baseColor, 0.20);

        // Process hand runs then full-body runs separately to generate alternation sequences, but share assigned map
        foreach (bool isFullBody in new[] { false, true })
        {
            string? lastMoveId = null;
            bool alternate = false;

            var clips = targetClips.Where(c => c.IsFullBody == isFullBody).OrderBy(c => c.StartBeat);
            foreach (var clip in clips)
            {
                var id = clip.MoveId ?? string.Empty;

                // If we already assigned a final color for this MoveId, just apply it
                if (assigned.TryGetValue(id, out var color))
                {
                    clip.BackgroundColor = color;
                    if (!string.Equals(lastMoveId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        lastMoveId = id;
                        alternate = !alternate;
                    }
                    continue;
                }

                // New run of a move — flip alternation if it's a different move than last
                if (!string.Equals(lastMoveId, id, StringComparison.OrdinalIgnoreCase))
                {
                    alternate = !alternate;
                    lastMoveId = id;
                }

                // Determine base color for this move from the majority section across all instances
                Color baseColor = baseColorForMove.TryGetValue(id, out var b) ? b : Colors.Gray;
                var finalColor = Variant(baseColor, alternate);
                assigned[id] = finalColor;
                clip.BackgroundColor = finalColor;
            }
        }

        // Record a single undo/redo for all changed clips
        var finalColors = targetClips.Select(c => c.BackgroundColor).ToList();
        bool anyChanged = false;
        for (int i = 0; i < targetClips.Count; i++)
        {
            if (!Equals(originalColors[i], finalColors[i])) { anyChanged = true; break; }
        }

        if (anyChanged)
        {
            timeline.UndoService.Record(
                undo: () =>
                {
                    for (int i = 0; i < targetClips.Count; i++)
                        targetClips[i].BackgroundColor = originalColors[i];
                },
                redo: () =>
                {
                    for (int i = 0; i < targetClips.Count; i++)
                        targetClips[i].BackgroundColor = finalColors[i];
                }
            );
        }
    }

    private static Color Brighten(Color c, double amount)
    {
        byte R(byte r) => (byte)Math.Min(255, (int)(r + (255 - r) * amount));
        byte G(byte g) => (byte)Math.Min(255, (int)(g + (255 - g) * amount));
        byte B(byte b) => (byte)Math.Min(255, (int)(b + (255 - b) * amount));
        return new Color(c.A, R(c.R), G(c.G), B(c.B));
    }

    private static Color Darken(Color c, double amount)
    {
        byte R(byte r) => (byte)Math.Max(0, (int)(r * (1 - amount)));
        byte G(byte g) => (byte)Math.Max(0, (int)(g * (1 - amount)));
        byte B(byte b) => (byte)Math.Max(0, (int)(b * (1 - amount)));
        return new Color(c.A, R(c.R), G(c.G), B(c.B));
    }
}