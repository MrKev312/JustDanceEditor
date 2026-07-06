using System;

namespace JustDanceEditor.Editor.Services.Motion;

public readonly record struct MotionDeviceId(int Slot);

public sealed record MotionInputEndpoint(string Host, int Port)
{
    public static MotionInputEndpoint DefaultDsu { get; } = new("127.0.0.1", 26760);
}

public sealed record MotionDeviceInfo(
    MotionDeviceId Id,
    string DisplayName,
    bool IsConnected,
    byte Battery,
    string MacAddress);

public sealed record MotionSensorSample(
    MotionDeviceId DeviceId,
    ulong? SensorTimestampMicroseconds,
    float AccX,
    float AccY,
    float AccZ,
    float GyroX,
    float GyroY,
    float GyroZ,
    DateTimeOffset ReceivedAt);

public sealed class MotionSensorSampleEventArgs(MotionSensorSample sample) : EventArgs
{
    public MotionSensorSample Sample { get; } = sample;
}