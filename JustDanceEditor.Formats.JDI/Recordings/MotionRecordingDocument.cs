using System.Text.Json.Serialization;

namespace JustDanceEditor.Formats.JDI.Recordings;

public sealed class MotionRecordingDocument
{
    public int FormatVersion { get; set; } = 1;
    public Guid RecordingId { get; set; }
    public int CoachId { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? SongId { get; set; }
    public string? MapName { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public double TimelineStartSeconds { get; set; }
    public double TimelineEndSeconds { get; set; }
    public List<RecordedMotionSample> Samples { get; set; } = [];

    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromSeconds(Math.Max(0, TimelineEndSeconds - TimelineStartSeconds));
}

public sealed class RecordedMotionSample
{
    public double MapTime { get; set; }
    public float AccX { get; set; }
    public float AccY { get; set; }
    public float AccZ { get; set; }
    public float GyroX { get; set; }
    public float GyroY { get; set; }
    public float GyroZ { get; set; }
    public ulong? SensorTimestampMicroseconds { get; set; }
}
