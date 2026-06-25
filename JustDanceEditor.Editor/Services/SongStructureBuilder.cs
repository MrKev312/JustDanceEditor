using JustDanceEditor.Editor.ViewModels.Dialogs;
using JustDanceEditor.Formats.JDI.Timelines;

using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.Services;

/// <summary>
/// Shared utility for building JDI timeline structures from user-provided song parameters.
/// Used by both new song creation and edit-song workflows.
/// </summary>
public static class SongStructureBuilder
{
    private const int SampleRate = 48000;

    /// <summary>
    /// Builds the marker array from BPM and beat range.
    /// Markers[i] = sample position of beat (startBeat + i).
    /// </summary>
    public static List<int> BuildMarkers(double bpm, int startBeat, int endBeat)
    {
        double beatDurationSeconds = 60.0 / bpm;
        int count = endBeat - startBeat + 1;
        List<int> markers = [with(count)];
        for (int i = 0; i < count; i++)
            markers.Add((int)(i * beatDurationSeconds * SampleRate));
        return markers;
    }

    /// <summary>
    /// Converts an ordered set of SectionEntry objects into SectionSegment objects.
    /// </summary>
    public static List<SectionSegment> BuildSections(IEnumerable<SectionEntry> sections) =>
        [.. sections
            .OrderBy(s => s.StartBeat)
            .Select(s => new SectionSegment { SectionType = s.SectionType, StartBeat = s.StartBeat })];

    /// <summary>
    /// Builds a single default signature covering the whole song.
    /// </summary>
    public static List<SignatureSegment> BuildDefaultSignatures(int beatsPerMeasure) =>
    [
        new SignatureSegment
        {
            Beats = beatsPerMeasure,
            Marker = 0,
            Comment = "Default"
        }
    ];
}