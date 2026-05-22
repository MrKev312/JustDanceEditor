using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Rendering;

using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Editor.Services;

internal static class DisplayRefreshRateProvider
{
    private const int FallbackRefreshRateHz = 60;
    private const int MinimumRefreshRateHz = 24;
    private const int MaximumRefreshRateHz = 500;

    public static TimeSpan GetRefreshInterval()
        => TimeSpan.FromSeconds(1d / GetRefreshRateHz());

    private static int GetRefreshRateHz()
    {
        if (OperatingSystem.IsWindows() && TryGetWindowsRefreshRate(out int windowsRefreshRate))
            return windowsRefreshRate;

        if (TryGetAvaloniaRenderTimerRefreshRate(out int renderTimerRefreshRate))
            return renderTimerRefreshRate;

        return FallbackRefreshRateHz;
    }

    private static bool TryGetAvaloniaRenderTimerRefreshRate(out int refreshRate)
    {
        refreshRate = 0;

        try
        {
            Type? locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base");
            object? locator = locatorType?.GetProperty("Current", BindingFlags.Static | BindingFlags.Public)?.GetValue(null)
                ?? locatorType?.GetProperty("CurrentMutable", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            object? renderTimer = locator?.GetType().GetMethod("GetService", [typeof(Type)])?.Invoke(locator, [typeof(IRenderTimer)]);
            if (renderTimer == null)
                return false;

            PropertyInfo? framesPerSecondProperty = renderTimer.GetType().GetProperty("FramesPerSecond", BindingFlags.Instance | BindingFlags.Public);
            object? value = framesPerSecondProperty?.GetValue(renderTimer);
            return TryNormalizeRefreshRate(value, out refreshRate);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetWindowsRefreshRate(out int refreshRate)
    {
        refreshRate = 0;

        nint monitor = nint.Zero;
        nint windowHandle = GetMainWindowHandle();
        if (windowHandle != nint.Zero)
            monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);

        string? deviceName = null;
        if (monitor != nint.Zero)
        {
            MonitorInfoEx monitorInfo = new()
            {
                Size = Marshal.SizeOf<MonitorInfoEx>()
            };

            if (GetMonitorInfo(monitor, ref monitorInfo) && !string.IsNullOrWhiteSpace(monitorInfo.DeviceName))
                deviceName = monitorInfo.DeviceName;
        }

        if (TryGetWindowsDeviceRefreshRate(deviceName, out refreshRate))
            return true;

        return TryGetWindowsDeviceRefreshRate(null, out refreshRate);
    }

    private static nint GetMainWindowHandle()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return nint.Zero;

        IPlatformHandle? platformHandle = desktop.MainWindow?.TryGetPlatformHandle();
        return platformHandle != null && string.Equals(platformHandle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase)
            ? platformHandle.Handle
            : nint.Zero;
    }

    private static bool TryGetWindowsDeviceRefreshRate(string? deviceName, out int refreshRate)
    {
        refreshRate = 0;

        DevMode devMode = new()
        {
            Size = (ushort)Marshal.SizeOf<DevMode>()
        };

        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref devMode))
            return false;

        return TryNormalizeRefreshRate(devMode.DisplayFrequency, out refreshRate);
    }

    private static bool TryNormalizeRefreshRate(object? value, out int refreshRate)
    {
        refreshRate = 0;
        if (value == null)
            return false;

        double numericValue = Convert.ToDouble(value);
        if (double.IsNaN(numericValue) || double.IsInfinity(numericValue))
            return false;

        refreshRate = (int)Math.Round(numericValue);
        return refreshRate is >= MinimumRefreshRateHz and <= MaximumRefreshRateHz;
    }

    private const int EnumCurrentSettings = -1;
    private const uint MonitorDefaultToNearest = 2;
    private const int CharacterCountDeviceName = 32;
    private const int CharacterCountFormName = 32;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfoEx lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CharacterCountDeviceName)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CharacterCountDeviceName)]
        public string DeviceName;

        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CharacterCountFormName)]
        public string FormName;

        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint ICMMethod;
        public uint ICMIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }
}
