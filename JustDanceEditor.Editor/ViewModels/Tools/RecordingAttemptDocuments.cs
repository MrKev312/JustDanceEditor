using JustDanceEditor.Formats.JDI.Recordings;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal static class RecordingAttemptDocuments
{
    public static MotionRecordingDocument CreateSnapshot(MotionRecordingDocument recording, double endSeconds)
    {
        MotionRecordingDocument snapshot = new()
        {
            FormatVersion = recording.FormatVersion,
            RecordingId = recording.RecordingId,
            CoachId = recording.CoachId,
            DeviceId = recording.DeviceId,
            DeviceName = recording.DeviceName,
            SongId = recording.SongId,
            MapName = recording.MapName,
            StartedAtUtc = recording.StartedAtUtc,
            TimelineStartSeconds = recording.TimelineStartSeconds,
            TimelineEndSeconds = endSeconds
        };

        foreach (RecordedMotionSample sample in recording.Samples)
            snapshot.Samples.Add(CloneSample(sample));

        return snapshot;
    }

    private static RecordedMotionSample CloneSample(RecordedMotionSample sample)
        => new()
        {
            MapTime = sample.MapTime,
            AccX = sample.AccX,
            AccY = sample.AccY,
            AccZ = sample.AccZ,
            GyroX = sample.GyroX,
            GyroY = sample.GyroY,
            GyroZ = sample.GyroZ,
            SensorTimestampMicroseconds = sample.SensorTimestampMicroseconds
        };
}