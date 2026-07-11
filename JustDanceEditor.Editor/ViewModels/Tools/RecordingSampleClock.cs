using JustDanceEditor.Editor.Services.Motion;
using JustDanceEditor.Editor.ViewModels.Timeline;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingSampleClock
{
    private ulong? _sensorTimestampAnchor;
    private double _timelineSecondsAnchor;
    private double _lastMapTimeSeconds;

    public void Reset()
    {
        _sensorTimestampAnchor = null;
        _timelineSecondsAnchor = 0.0;
        _lastMapTimeSeconds = double.NegativeInfinity;
    }

    public double GetMapTimeSeconds(TimelineEditorViewModel timeline, MotionSensorSample sample)
    {
        double playbackSeconds = timeline.Playback.CurrentTime.TotalSeconds;
        ulong? sensorTimestamp = sample.SensorTimestampMicroseconds;
        if (!sensorTimestamp.HasValue)
            return KeepMonotonic(playbackSeconds);

        if (!_sensorTimestampAnchor.HasValue || sensorTimestamp.Value < _sensorTimestampAnchor.Value)
        {
            _sensorTimestampAnchor = sensorTimestamp.Value;
            _timelineSecondsAnchor = playbackSeconds;
            return KeepMonotonic(playbackSeconds);
        }

        double sensorDeltaSeconds = (sensorTimestamp.Value - _sensorTimestampAnchor.Value) / 1_000_000.0;
        double sampleSeconds = _timelineSecondsAnchor + sensorDeltaSeconds;
        return KeepMonotonic(sampleSeconds);
    }

    private double KeepMonotonic(double mapTimeSeconds)
    {
        if (double.IsNaN(mapTimeSeconds) || double.IsInfinity(mapTimeSeconds))
            mapTimeSeconds = 0.0;

        if (mapTimeSeconds < 0.0)
            mapTimeSeconds = 0.0;

        if (mapTimeSeconds < _lastMapTimeSeconds)
            return _lastMapTimeSeconds;

        _lastMapTimeSeconds = mapTimeSeconds;
        return mapTimeSeconds;
    }
}