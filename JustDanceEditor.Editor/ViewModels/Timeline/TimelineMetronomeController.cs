using JustDanceEditor.Formats.JDI.Timelines;

using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineMetronomeController(TimelineEditorViewModel timeline)
{
    public void ApplyEnabled(bool enabled)
    {
        timeline.Playback.IsMetronomeEnabled = enabled;
        if (enabled)
            UpdateTiming();
    }

    public void UpdateTiming()
    {
        TimelineStructureDocument structure = timeline.Package.TimelineStructure;

        double zeroBeatTime = 0;
        double bpm = 120;
        int beatsPerMeasure = 4;

        if (structure.Markers.Count >= 2)
        {
            double beatDurationSeconds = (structure.Markers[1] - structure.Markers[0]) / 48000.0;
            if (beatDurationSeconds > 0)
                bpm = 60.0 / beatDurationSeconds;

            zeroBeatTime = structure.GetPlaybackSecondsAtBeat(0);
        }

        if (structure.Signatures.Count > 0)
            beatsPerMeasure = structure.Signatures[0].Beats;

        timeline.Playback.UpdateMetronome(
            zeroBeatTime,
            bpm,
            beatsPerMeasure,
            structure.Sections.Select(static section => (double)section.StartBeat));
    }
}
