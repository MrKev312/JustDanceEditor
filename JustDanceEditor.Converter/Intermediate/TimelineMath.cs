using JustDanceEditor.Formats.UbiArt.Tapes;

namespace JustDanceEditor.Converter.Intermediate;

internal sealed class TimelineMath
{
    private readonly Structure structure;
    private readonly int[] markers;
    private readonly double defaultMsPerBeat;

    public TimelineMath(Structure structure)
    {
        this.structure = structure ?? new Structure();
        markers = this.structure.markers ?? [];
        defaultMsPerBeat = EstimateMsPerBeat();
    }

    public double ToBeat(int timelineValue)
    {
        if (markers.Length == 0)
            return timelineValue / 48d;

        int index = Array.BinarySearch(markers, timelineValue);
        if (index >= 0)
            return structure.startBeat + index;

        int nextIndex = ~index;
        if (nextIndex <= 0)
        {
            double deltaBeats = (timelineValue - markers[0]) / (defaultMsPerBeat * 48d);
            return structure.startBeat + deltaBeats;
        }

        if (nextIndex >= markers.Length)
        {
            double deltaBeats = (timelineValue - markers[^1]) / (defaultMsPerBeat * 48d);
            return structure.startBeat + markers.Length - 1 + deltaBeats;
        }

        int prevIndex = nextIndex - 1;
        int prevMarker = markers[prevIndex];
        int nextMarker = markers[nextIndex];
        double fraction = (double)(timelineValue - prevMarker) / (nextMarker - prevMarker);
        return structure.startBeat + prevIndex + fraction;
    }

    public double EstimateMsPerBeat()
    {
        if (markers.Length < 2)
            return 500;

        double total = 0;
        for (int i = 1; i < markers.Length; i++)
            total += (markers[i] - markers[i - 1]) / 48d;
        return total / (markers.Length - 1);
    }

    public double EstimateBpm()
    {
        double ms = defaultMsPerBeat;
        return ms > 0 ? 60000d / ms : 120d;
    }
}
