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