using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services.Motion;

public sealed class DsuMotionInputClient : IMotionInputClient
{
    private const ushort ProtocolVersion = 1001;
    private const uint MessageTypeControllerInfo = 0x100001;
    private const uint MessageTypeControllerData = 0x100002;
    private const int HeaderSize = 16;
    private const int MessageTypeSize = 4;
    private const int PayloadOffset = HeaderSize + MessageTypeSize;

    private readonly uint _clientId = (uint)Random.Shared.Next();
    private readonly Lock _gate = new();
    private readonly Dictionary<int, MotionDeviceInfo> _devices = [];

    private UdpClient? _udp;
    private CancellationTokenSource? _receiveCts;
    private CancellationTokenSource? _streamCts;
    private Task? _receiveTask;
    private Task? _streamKeepAliveTask;
    private MotionDeviceId? _streamingDevice;

    public bool IsConnected { get; private set; }
    public bool IsStreaming => _streamingDevice.HasValue;

    public IReadOnlyList<MotionDeviceInfo> Devices
    {
        get
        {
            lock (_gate)
                return _devices.Values.OrderBy(device => device.Id.Slot).ToArray();
        }
    }

    public event EventHandler? DevicesChanged;
    public event EventHandler<MotionSensorSampleEventArgs>? SampleReceived;

    public async Task ConnectAsync(MotionInputEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await DisconnectAsync();

        IPAddress address = await ResolveAddressAsync(endpoint.Host, cancellationToken);
        IPEndPoint remote = new(address, endpoint.Port);

        UdpClient udp = new(address.AddressFamily);
        udp.Connect(remote);

        _udp = udp;
        _receiveCts = new CancellationTokenSource();
        _receiveTask = Task.Run(() => ReceiveLoopAsync(udp, _receiveCts.Token), CancellationToken.None);
        IsConnected = true;

        await RefreshDevicesAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        await StopStreamingAsync();

        IsConnected = false;
        _receiveCts?.Cancel();
        _udp?.Dispose();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException) when (_receiveCts?.IsCancellationRequested == true || _udp == null)
            {
            }
        }

        _receiveTask = null;
        _receiveCts?.Dispose();
        _receiveCts = null;
        _udp = null;

        lock (_gate)
            _devices.Clear();

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisconnected();

        byte[] payload = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 4);
        payload[4] = 0;
        payload[5] = 1;
        payload[6] = 2;
        payload[7] = 3;

        await SendPacketAsync(MessageTypeControllerInfo, payload, cancellationToken);
        await Task.Delay(250, cancellationToken);
    }

    public async Task StartStreamingAsync(MotionDeviceId deviceId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisconnected();
        await StopStreamingAsync();

        _streamingDevice = deviceId;
        _streamCts = new CancellationTokenSource();
        await SendDataRequestAsync(deviceId, cancellationToken);
        _streamKeepAliveTask = Task.Run(() => KeepStreamingRequestAliveAsync(deviceId, _streamCts.Token), CancellationToken.None);
    }

    public async Task StopStreamingAsync()
    {
        _streamingDevice = null;
        _streamCts?.Cancel();

        if (_streamKeepAliveTask != null)
        {
            try
            {
                await _streamKeepAliveTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _streamKeepAliveTask = null;
        _streamCts?.Dispose();
        _streamCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }

    private async Task KeepStreamingRequestAliveAsync(MotionDeviceId deviceId, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await SendDataRequestAsync(deviceId, cancellationToken);
    }

    private Task SendDataRequestAsync(MotionDeviceId deviceId, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[8];
        payload[0] = 1;
        payload[1] = checked((byte)deviceId.Slot);
        return SendPacketAsync(MessageTypeControllerData, payload, cancellationToken);
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result = await udp.ReceiveAsync(cancellationToken);
                HandlePacket(result.Buffer);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void HandlePacket(byte[] packet)
    {
        if (packet.Length < PayloadOffset)
            return;

        if (!packet.AsSpan(0, 4).SequenceEqual("DSUS"u8))
            return;

        ushort payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(6, 2));
        int packetLength = Math.Min(packet.Length, HeaderSize + payloadLength);
        if (packetLength < PayloadOffset)
            return;

        uint messageType = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16, 4));
        ReadOnlySpan<byte> payload = packet.AsSpan(PayloadOffset, packetLength - PayloadOffset);

        if (messageType == MessageTypeControllerInfo)
        {
            HandleControllerInfo(payload);
            return;
        }

        if (messageType == MessageTypeControllerData)
            HandleControllerData(payload);
    }

    private void HandleControllerInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
            return;

        MotionDeviceInfo device = ParseDeviceInfo(payload, payload[0], payload[1] == 2);
        lock (_gate)
            _devices[device.Id.Slot] = device;

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleControllerData(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 80)
            return;

        int slot = payload[0];
        bool isConnected = payload[1] == 2 && payload[11] != 0;
        MotionDeviceInfo device = ParseDeviceInfo(payload, slot, isConnected);
        lock (_gate)
            _devices[slot] = device;

        DevicesChanged?.Invoke(this, EventArgs.Empty);

        MotionDeviceId? streamingDevice = _streamingDevice;
        if (!streamingDevice.HasValue || streamingDevice.Value.Slot != slot || !isConnected)
            return;

        ulong timestamp = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(48, 8));
        MotionSensorSample sample = new(
            streamingDevice.Value,
            timestamp == 0 ? null : timestamp,
            ReadSingle(payload, 56),
            ReadSingle(payload, 60),
            ReadSingle(payload, 64),
            ReadSingle(payload, 68),
            ReadSingle(payload, 72),
            ReadSingle(payload, 76),
            DateTimeOffset.UtcNow);

        SampleReceived?.Invoke(this, new MotionSensorSampleEventArgs(sample));
    }

    private static MotionDeviceInfo ParseDeviceInfo(ReadOnlySpan<byte> payload, int slot, bool isConnected)
    {
        byte battery = payload.Length > 10 ? payload[10] : (byte)0;
        string mac = payload.Length >= 10 ? FormatMacAddress(payload.Slice(4, 6)) : "";
        string name = isConnected
            ? $"DSU Slot {slot}"
            : $"DSU Slot {slot} (disconnected)";

        return new MotionDeviceInfo(new MotionDeviceId(slot), name, isConnected, battery, mac);
    }

    private async Task SendPacketAsync(uint messageType, byte[] payload, CancellationToken cancellationToken)
    {
        ThrowIfDisconnected();

        byte[] packet = new byte[PayloadOffset + payload.Length];
        Encoding.ASCII.GetBytes("DSUC", packet.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), checked((ushort)(MessageTypeSize + payload.Length)));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), _clientId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16, 4), messageType);
        payload.CopyTo(packet.AsSpan(PayloadOffset));

        uint crc = Crc32.Compute(packet);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), crc);

        UdpClient udp = _udp ?? throw new InvalidOperationException("DSU client is not connected.");
        await udp.SendAsync(packet, cancellationToken);
    }

    private static float ReadSingle(ReadOnlySpan<byte> source, int offset)
    {
        int raw = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
        return BitConverter.Int32BitsToSingle(raw);
    }

    private static string FormatMacAddress(ReadOnlySpan<byte> bytes)
        => string.Join(":", bytes.ToArray().Select(static value => value.ToString("X2")));

    private static async Task<IPAddress> ResolveAddressAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out IPAddress? parsed))
            return parsed;

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        return addresses.FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault()
            ?? throw new InvalidOperationException($"Could not resolve DSU host '{host}'.");
    }

    private void ThrowIfDisconnected()
    {
        if (!IsConnected || _udp == null)
            throw new InvalidOperationException("DSU client is not connected.");
    }

    private static class Crc32
    {
        private const uint Polynomial = 0xEDB88320;
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < bytes.Length; i++)
                crc = Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);

            return ~crc;
        }

        private static uint[] CreateTable()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
                table[i] = value;
            }

            return table;
        }
    }
}