using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;

namespace JustDanceEditor.Editor.Views.Timeline;

internal sealed class AudioBarTimelineCache
{
    private static readonly Comparison<SectionSegment> CompareSectionsByStartBeat =
        static (a, b) => a.StartBeat.CompareTo(b.StartBeat);
    private static readonly Comparison<SignatureSegment> CompareSignaturesByMarker =
        static (a, b) => a.Marker.CompareTo(b.Marker);

    private readonly List<SectionSegment> _sortedSections = [];
    private readonly List<SignatureSegment> _sortedSignatures = [];
    private readonly List<double> _sectionStarts = [];
    private bool _sectionsDirty = true;
    private bool _signaturesDirty = true;

    public void MarkSectionsDirty() => _sectionsDirty = true;

    public void MarkSignaturesDirty() => _signaturesDirty = true;

    public IReadOnlyList<SectionSegment> GetSortedSections(IEnumerable<SectionSegment>? sections)
    {
        if (!_sectionsDirty)
            return _sortedSections;

        _sortedSections.Clear();
        _sectionStarts.Clear();
        if (sections != null)
        {
            foreach (SectionSegment section in sections)
                _sortedSections.Add(section);

            _sortedSections.Sort(CompareSectionsByStartBeat);
            foreach (SectionSegment section in _sortedSections)
                _sectionStarts.Add(section.StartBeat);
        }

        _sectionsDirty = false;
        return _sortedSections;
    }

    public List<double> GetSectionStarts(IEnumerable<SectionSegment>? sections)
    {
        if (_sectionsDirty)
            GetSortedSections(sections);

        return _sectionStarts;
    }

    public IReadOnlyList<SignatureSegment> GetSortedSignatures(IEnumerable<SignatureSegment>? signatures)
    {
        if (!_signaturesDirty)
            return _sortedSignatures;

        _sortedSignatures.Clear();
        if (signatures != null)
        {
            foreach (SignatureSegment signature in signatures)
                _sortedSignatures.Add(signature);

            _sortedSignatures.Sort(CompareSignaturesByMarker);
        }

        _signaturesDirty = false;
        return _sortedSignatures;
    }
}