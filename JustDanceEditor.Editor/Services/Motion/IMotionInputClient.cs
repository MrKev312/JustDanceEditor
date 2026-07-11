using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services.Motion;

public interface IMotionInputClient : IAsyncDisposable
{
    bool IsConnected { get; }
    bool IsStreaming { get; }
    IReadOnlyList<MotionDeviceInfo> Devices { get; }

    event EventHandler? DevicesChanged;
    event EventHandler<MotionSensorSampleEventArgs>? SampleReceived;

    Task ConnectAsync(MotionInputEndpoint endpoint, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task RefreshDevicesAsync(CancellationToken cancellationToken = default);
    Task StartStreamingAsync(MotionDeviceId deviceId, CancellationToken cancellationToken = default);
    Task StopStreamingAsync();
}