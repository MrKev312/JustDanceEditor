using Avalonia.Threading;

using JustDanceEditor.Editor.Services.Motion;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal sealed class RecordingDeviceConnectionController(
    RecordingsToolViewModel owner,
    IMotionInputClient motionClient)
{
    public void Attach()
    {
        motionClient.DevicesChanged += MotionClient_DevicesChanged;
    }

    public void Detach()
    {
        motionClient.DevicesChanged -= MotionClient_DevicesChanged;
    }

    public async Task ConnectAsync()
    {
        try
        {
            owner.StatusText = "Connecting...";
            await motionClient.ConnectAsync(new MotionInputEndpoint(owner.Host, owner.Port));
            owner.IsConnected = motionClient.IsConnected;
            UpdateDevices();
            owner.StatusText = owner.IsConnected ? "Connected" : "Disconnected";
        }
        catch (Exception ex)
        {
            owner.IsConnected = false;
            owner.StatusText = ex.Message;
        }
    }

    public async Task DisconnectAsync()
    {
        await motionClient.DisconnectAsync();
        owner.IsConnected = false;
        owner.Devices.Clear();
        owner.SelectedDevice = null;
        owner.StatusText = "Disconnected";
    }

    public async Task RefreshDevicesAsync()
    {
        try
        {
            await motionClient.RefreshDevicesAsync();
            UpdateDevices();
        }
        catch (Exception ex)
        {
            owner.StatusText = ex.Message;
        }
    }

    public void UpdateDevices()
    {
        MotionDeviceInfo? oldSelection = owner.SelectedDevice;
        owner.Devices.Clear();

        foreach (MotionDeviceInfo device in motionClient.Devices)
            owner.Devices.Add(device);

        owner.SelectedDevice = owner.Devices.FirstOrDefault(device => oldSelection != null && device.Id == oldSelection.Id)
            ?? owner.Devices.FirstOrDefault(device => device.IsConnected)
            ?? owner.Devices.FirstOrDefault();
    }

    private void MotionClient_DevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(UpdateDevices, DispatcherPriority.Background);
    }
}