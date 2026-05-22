using System;
using System.IO;

namespace JustDanceEditor.Editor.Services;

internal static class LinuxAudioEnvironment
{
    public static bool IsWsl()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        string? wslInterop = Environment.GetEnvironmentVariable("WSL_INTEROP");
        if (!string.IsNullOrWhiteSpace(wslInterop))
            return true;

        return ContainsMicrosoftMarker("/proc/sys/kernel/osrelease")
            || ContainsMicrosoftMarker("/proc/version");
    }

    private static bool ContainsMicrosoftMarker(string path)
    {
        try
        {
            return File.Exists(path)
                && File.ReadAllText(path).Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
