using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineStructureEditorController(TimelineEditorViewModel timeline)
{
    public void AddSection(double beat, SongSectionType type)
    {
        if (timeline.TimelineStructure.Sections.Any(s => Math.Abs(s.StartBeat - beat) < 0.5))
            return;

        SectionSegment section = new() { SectionType = type, StartBeat = beat };
        timeline.TimelineStructure.Sections.Add(section);
        SortSections();

        timeline.PushUndo(
            undo: () =>
            {
                timeline.TimelineStructure.Sections.Remove(section);
                SortSections();
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                timeline.TimelineStructure.Sections.Add(section);
                SortSections();
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void RemoveSection(SectionSegment section)
    {
        if (!timeline.TimelineStructure.Sections.Contains(section))
            return;

        int index = timeline.TimelineStructure.Sections.IndexOf(section);
        timeline.TimelineStructure.Sections.Remove(section);

        timeline.PushUndo(
            undo: () =>
            {
                timeline.TimelineStructure.Sections.Insert(Math.Min(index, timeline.TimelineStructure.Sections.Count), section);
                SortSections();
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                timeline.TimelineStructure.Sections.Remove(section);
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void MoveSection(SectionSegment section, double newBeat)
    {
        if (!timeline.TimelineStructure.Sections.Contains(section))
            return;

        if (timeline.TimelineStructure.Sections.Any(s => s != section && Math.Abs(s.StartBeat - newBeat) < 0.5))
            return;

        double oldBeat = section.StartBeat;
        if (Math.Abs(oldBeat - newBeat) < 0.01)
            return;

        section.StartBeat = newBeat;
        SortSections();

        timeline.PushUndo(
            undo: () =>
            {
                section.StartBeat = oldBeat;
                SortSections();
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                section.StartBeat = newBeat;
                SortSections();
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void ChangeSectionType(SectionSegment section, SongSectionType newType)
    {
        SongSectionType oldType = section.SectionType;
        if (oldType == newType)
            return;

        section.SectionType = newType;

        timeline.PushUndo(
            undo: () =>
            {
                section.SectionType = oldType;
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                section.SectionType = newType;
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void AddSignature(double beat, int beats)
    {
        if (timeline.TimelineStructure.Signatures.Any(s => Math.Abs(s.Marker - beat) < 0.5))
            return;

        SignatureSegment sig = new() { Beats = beats, Marker = beat };
        timeline.TimelineStructure.Signatures.Add(sig);
        SortSignatures();

        timeline.PushUndo(
            undo: () =>
            {
                timeline.TimelineStructure.Signatures.Remove(sig);
                SortSignatures();
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                timeline.TimelineStructure.Signatures.Add(sig);
                SortSignatures();
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void RemoveSignature(SignatureSegment sig)
    {
        if (!timeline.TimelineStructure.Signatures.Contains(sig))
            return;

        int index = timeline.TimelineStructure.Signatures.IndexOf(sig);
        timeline.TimelineStructure.Signatures.Remove(sig);

        timeline.PushUndo(
            undo: () =>
            {
                timeline.TimelineStructure.Signatures.Insert(Math.Min(index, timeline.TimelineStructure.Signatures.Count), sig);
                SortSignatures();
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                timeline.TimelineStructure.Signatures.Remove(sig);
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void MoveSignature(SignatureSegment sig, double newBeat)
    {
        if (!timeline.TimelineStructure.Signatures.Contains(sig))
            return;

        if (timeline.TimelineStructure.Signatures.Any(s => s != sig && Math.Abs(s.Marker - newBeat) < 0.5))
            return;

        double oldBeat = sig.Marker;
        if (Math.Abs(oldBeat - newBeat) < 0.01)
            return;

        sig.Marker = newBeat;
        SortSignatures();

        timeline.PushUndo(
            undo: () =>
            {
                sig.Marker = oldBeat;
                SortSignatures();
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                sig.Marker = newBeat;
                SortSignatures();
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    public void ChangeSignatureBeats(SignatureSegment sig, int newBeats)
    {
        int oldBeats = sig.Beats;
        if (oldBeats == newBeats)
            return;

        sig.Beats = newBeats;

        timeline.PushUndo(
            undo: () =>
            {
                sig.Beats = oldBeats;
                timeline.NotifyStructureChanged();
            },
            redo: () =>
            {
                sig.Beats = newBeats;
                timeline.NotifyStructureChanged();
            });

        timeline.NotifyStructureChanged();
    }

    private void SortSections()
    {
        timeline.TimelineStructure.Sections.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
    }

    private void SortSignatures()
    {
        timeline.TimelineStructure.Signatures.Sort((a, b) => a.Marker.CompareTo(b.Marker));
    }
}