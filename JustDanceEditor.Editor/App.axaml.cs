using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.Services.Motion;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.Views;
using JustDanceEditor.Formats.JDI.Recordings;

using KevInc.Avalonia;

using Microsoft.Extensions.DependencyInjection;

using System;
namespace JustDanceEditor.Editor;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    /// <summary>Convenience accessor for code-behind integration.</summary>
    public ITimelineContextService TimelineContext => (Services ?? throw new InvalidOperationException("Application services have not been initialized yet.")).GetRequiredService<ITimelineContextService>();

    /// <summary>Convenience accessor for dialog code-behind integration.</summary>
    public IDialogService DialogService => (Services ?? throw new InvalidOperationException("Application services have not been initialized yet.")).GetRequiredService<IDialogService>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        PlatformTheme.Apply(this, includeDockStyles: true);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ServiceCollection sc = new();

        sc.AddSingleton<EditorSettingsService>();
        sc.AddSingleton<TimelineSettingsService>();
        sc.AddSingleton<ITimelineContextService, TimelineContextService>();
        sc.AddSingleton<IDialogService, AvaloniaDialogService>();
        sc.AddSingleton<MotionRecordingScoreHudService>();
        sc.AddSingleton<IMotionRecordingRepository, JsonMotionRecordingRepository>();
        sc.AddTransient<IMotionInputClient, DsuMotionInputClient>();
        sc.AddTransient<JdiMotionClassifierGenerator>();
        sc.AddTransient<JdiMotionRecordingLiveScorer>();
        sc.AddTransient<JdiMotionRecordingAnalyzer>();

        // MainWindowViewModel is transient so each app launch gets a fresh instance
        sc.AddTransient<MainWindowViewModel>();

        Services = sc.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
