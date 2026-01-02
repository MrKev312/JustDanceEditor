using Avalonia.Media;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

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
        TimelineEditorViewModel? timeline = timelineContext?.ActiveTimeline;
        if (timeline == null)
            return;

        // Build section color map from SongSectionType attributes (same logic as AudioBarControl)
        Dictionary<SongSectionType, Color> sectionColors = [];
        foreach (SongSectionType type in Enum.GetValues<SongSectionType>())
        {
            FieldInfo? field = typeof(SongSectionType).GetField(type.ToString());
            ColorAttribute? attr = field?.GetCustomAttribute<ColorAttribute>();
            if (attr != null)
                sectionColors[type] = Color.FromRgb(attr.R, attr.G, attr.B);
            else
                sectionColors[type] = Colors.Gray;
        }

        // Build section spans (start/end) for overlap calculations
        List<SectionSegment> sections = [.. timeline.TimelineStructure.Sections.OrderBy(s => s.StartBeat)];
        List<(double Start, double End, SongSectionType Type)> sectionSpans = [];
        for (int i = 0; i < sections.Count; i++)
        {
            double start = sections[i].StartBeat;
            double end = (i + 1 < sections.Count) ? sections[i + 1].StartBeat : timeline.TimelineStructure.EndBeat;
            sectionSpans.Add((start, end, sections[i].SectionType));
        }

        // Gather all movement clips (both hand and full body)
        List<MoveClipViewModel> allMoveClips = [.. timeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>()];
        if (allMoveClips.Count == 0)
            return;

        // For each MoveId, compute total overlap per section type across ALL instances
        Dictionary<string, SongSectionType> baseSectionForMove = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Color> baseColorForMove = new(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, MoveClipViewModel> grp in allMoveClips.GroupBy(c => c.MoveId, StringComparer.OrdinalIgnoreCase))
        {
            Dictionary<SongSectionType, double> totals = [];
            foreach (MoveClipViewModel? clip in grp)
            {
                double clipStart = clip.StartBeat;
                double clipEnd = clip.StartBeat + clip.DurationBeats;
                foreach ((double Start, double End, SongSectionType Type) span in sectionSpans)
                {
                    double overlap = Math.Max(0.0, Math.Min(clipEnd, span.End) - Math.Max(clipStart, span.Start));
                    if (overlap <= 0)
                        continue;
                    if (!totals.TryGetValue(span.Type, out double cur))
                        cur = 0.0;
                    totals[span.Type] = cur + overlap;
                }
            }

            // choose the section type with the maximum overlap; fallback to first section
            SongSectionType chosen = totals.Count > 0 ? totals.OrderByDescending(kv => kv.Value).First().Key : sectionSpans.First().Type;
            baseSectionForMove[grp.Key] = chosen;
            baseColorForMove[grp.Key] = sectionColors.TryGetValue(chosen, out Color c) ? c : Colors.Gray;
        }

        // We'll compute colors and apply them across both body types, but determine brightness alternation per body type sequence
        Dictionary<string, Color> assigned = new(StringComparer.OrdinalIgnoreCase);

        // Helper to get bright/dark variant
        static Color Variant(Color baseColor, bool brighten) => brighten ? Brighten(baseColor, 0.20) : Darken(baseColor, 0.20);

        // Process hand runs then full-body runs separately to generate alternation sequences, but share assigned map
        foreach (bool isFullBody in new[] { false, true })
        {
            string? lastMoveId = null;
            bool alternate = false;

            IOrderedEnumerable<MoveClipViewModel> clips = allMoveClips.Where(c => c.IsFullBody == isFullBody).OrderBy(c => c.StartBeat);
            foreach (MoveClipViewModel? clip in clips)
            {
                string id = clip.MoveId ?? string.Empty;

                // If we already assigned a final color for this MoveId, just record it for reuse
                if (assigned.TryGetValue(id, out Color color))
                {
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
                Color baseColor = baseColorForMove.TryGetValue(id, out Color b) ? b : Colors.Gray;
                Color finalColor = Variant(baseColor, alternate);
                assigned[id] = finalColor;
            }
        }

        // Apply assigned colors to MoveDefinitions (both hand and full-body variants) and record original colors for undo
        Dictionary<MoveDefinitionViewModel, Color> defsToOriginal = [];
        foreach (KeyValuePair<string, Color> kv in assigned)
        {
            string id = kv.Key;
            Color normalized = new(255, kv.Value.R, kv.Value.G, kv.Value.B);

            MoveDefinitionViewModel defHand = timeline.GetOrRegisterMove(id, false);
            if (!defsToOriginal.ContainsKey(defHand))
                defsToOriginal[defHand] = defHand.Color;

            MoveDefinitionViewModel defFull = timeline.GetOrRegisterMove(id, true);
            if (!defsToOriginal.ContainsKey(defFull))
                defsToOriginal[defFull] = defFull.Color;
        }

        Dictionary<MoveDefinitionViewModel, Color> finalDefColors = defsToOriginal.Keys.ToDictionary(d => d, d => assigned.TryGetValue(d.Id, out Color col) ? new Color(255, col.R, col.G, col.B) : d.Color);

        bool anyChanged = defsToOriginal.Any(kv => !Equals(kv.Value, finalDefColors[kv.Key]));

        if (anyChanged)
        {
            // Apply final colors immediately so UI shows the change (properties and timeline render from definitions)
            foreach (KeyValuePair<MoveDefinitionViewModel, Color> kv in finalDefColors)
            {
                kv.Key.Color = kv.Value;
            }

            timeline.UndoService.Record(
                undo: () =>
                {
                    foreach (KeyValuePair<MoveDefinitionViewModel, Color> kv in defsToOriginal)
                        kv.Key.Color = kv.Value;
                },
                redo: () =>
                {
                    foreach (KeyValuePair<MoveDefinitionViewModel, Color> kv in finalDefColors)
                        kv.Key.Color = kv.Value;
                }
            );
        }
    }

    private static Color Brighten(Color c, double amount)
    {
        byte R(byte r) => (byte)Math.Min(255, (int)(r + ((255 - r) * amount)));
        byte G(byte g) => (byte)Math.Min(255, (int)(g + ((255 - g) * amount)));
        byte B(byte b) => (byte)Math.Min(255, (int)(b + ((255 - b) * amount)));
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