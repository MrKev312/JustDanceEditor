namespace JustDanceEditor.Formats.JDI.Timelines;

public sealed class TimelineMath
{
    private readonly Structure _structure;
    private readonly int[] _markers;
    private readonly double _defaultMsPerBeat;

    public TimelineMath(Structure structure)
    {
        _structure = structure ?? new Structure();
        _markers = _structure.markers ?? [];
        _defaultMsPerBeat = EstimateMsPerBeat();
    }

    public double ToBeat(int timelineValue)
    {
        if (_markers.Length == 0)
            return timelineValue / 48d;

        int index = Array.BinarySearch(_markers, timelineValue);
        if (index >= 0)
            return _structure.startBeat + index;

        int nextIndex = ~index;
        if (nextIndex <= 0)
        {
            double deltaBeats = (timelineValue - _markers[0]) / (_defaultMsPerBeat * 48d);
            return _structure.startBeat + deltaBeats;
        }

        if (nextIndex >= _markers.Length)
        {
            double deltaBeats = (timelineValue - _markers[^1]) / (_defaultMsPerBeat * 48d);
            return _structure.startBeat + _markers.Length - 1 + deltaBeats;
        }

        int prevIndex = nextIndex - 1;
        int prevMarker = _markers[prevIndex];
        int nextMarker = _markers[nextIndex];
        double fraction = (double)(timelineValue - prevMarker) / (nextMarker - prevMarker);
        return _structure.startBeat + prevIndex + fraction;
    }

    public double EstimateMsPerBeat()
    {
        if (_markers.Length < 2)
            return 500;

        double total = 0;
        for (int i = 1; i < _markers.Length; i++)
            total += (_markers[i] - _markers[i - 1]) / 48d;
        return total / (_markers.Length - 1);
    }

    public double EstimateBpm()
    {
        double ms = _defaultMsPerBeat;
        return ms > 0 ? 60000d / ms : 120d;
    }
}