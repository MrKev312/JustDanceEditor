using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;

using System;
using System.Runtime.InteropServices;

namespace JustDanceEditor.Shared.Avalonia;

internal enum NativeUiPlatform
{
    Windows,
    MacOS,
    Linux
}

internal static class PlatformTheme
{
    public static NativeUiPlatform Current =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? NativeUiPlatform.Windows
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? NativeUiPlatform.MacOS
                : NativeUiPlatform.Linux;

    public static void Apply(Application application, string resourceAssemblyName, bool includeDockStyles)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceAssemblyName);

        string platformName = Current.ToString();
        AddStyle(application, resourceAssemblyName, $"Styles/Platform/{platformName}.axaml");

        if (includeDockStyles)
            AddStyle(application, resourceAssemblyName, $"Styles/Dock/{platformName}.axaml");
    }

    public static void ApplyMainWindowChrome(Window window) => ApplyWindowChrome(window, floatingWindow: false);

    public static void ApplyFloatingWindowChrome(Window window) => ApplyWindowChrome(window, floatingWindow: true);

    private static void AddStyle(Application application, string resourceAssemblyName, string relativePath)
    {
        Uri baseUri = new($"avares://{resourceAssemblyName}/App.axaml");
        Uri source = new($"avares://{resourceAssemblyName}/{relativePath}");
        application.Styles.Add(new StyleInclude(baseUri) { Source = source });
    }

    private static void ApplyWindowChrome(Window window, bool floatingWindow)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.WindowDecorations = WindowDecorations.Full;
        window.WindowStartupLocation = floatingWindow
            ? WindowStartupLocation.CenterOwner
            : window.WindowStartupLocation;

        switch (Current)
        {
            case NativeUiPlatform.Windows:
                window.TransparencyLevelHint =
                [
                    WindowTransparencyLevel.Mica,
                    WindowTransparencyLevel.AcrylicBlur
                ];
                window.Background = Brushes.Transparent;
                window.TransparencyBackgroundFallback = Brush("#2F2F2F");
                window.ExtendClientAreaToDecorationsHint = !floatingWindow;
                window.ExtendClientAreaTitleBarHeightHint = 32;
                window.FontFamily = new FontFamily("Segoe UI, Inter");
                break;

            case NativeUiPlatform.MacOS:
                window.TransparencyLevelHint =
                [
                    WindowTransparencyLevel.Blur,
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.Transparent
                ];
                window.Background = Brushes.Transparent;
                window.TransparencyBackgroundFallback = Brush("#20242C");
                window.ExtendClientAreaToDecorationsHint = !floatingWindow;
                window.ExtendClientAreaTitleBarHeightHint = 38;
                window.FontFamily = new FontFamily(".AppleSystemUIFont, SF Pro Text, Inter");
                break;

            default:
                window.TransparencyLevelHint = [WindowTransparencyLevel.None];
                window.Background = Brush("#20242A");
                window.TransparencyBackgroundFallback = Brush("#20242A");
                window.ExtendClientAreaToDecorationsHint = false;
                window.ExtendClientAreaTitleBarHeightHint = 0;
                window.FontFamily = new FontFamily("Cantarell, Noto Sans, Inter");
                break;
        }
    }

    private static SolidColorBrush Brush(string color) => new(Color.Parse(color));
}
