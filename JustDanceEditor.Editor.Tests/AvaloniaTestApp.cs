using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(JustDanceEditor.Editor.Tests.AvaloniaTestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JustDanceEditor.Editor.Tests;

public static class AvaloniaTestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

public sealed class TestApplication : Application;
