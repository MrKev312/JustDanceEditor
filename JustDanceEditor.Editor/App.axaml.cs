using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.Views;

using LibVLCSharp.Shared;

using Microsoft.Extensions.DependencyInjection;

using System;
namespace JustDanceEditor.Editor;

public partial class App : Application
{
    public IServiceProvider? Services { get; private set; }

    /// <summary>Convenience accessor — keeps existing code-behind references working.</summary>
    public LibVLC LibVLC => (Services ?? throw new InvalidOperationException("Application services have not been initialized yet.")).GetRequiredService<LibVLC>();

    /// <summary>Convenience accessor — keeps existing code-behind references working.</summary>
    public ITimelineContextService TimelineContext => (Services ?? throw new InvalidOperationException("Application services have not been initialized yet.")).GetRequiredService<ITimelineContextService>();

    /// <summary>Convenience accessor — keeps existing code-behind references working.</summary>
    public IDialogService DialogService => (Services ?? throw new InvalidOperationException("Application services have not been initialized yet.")).GetRequiredService<IDialogService>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── Build DI container ──────────────────────────────────────────────────
        ServiceCollection sc = new();

        sc.AddSingleton(_ => new LibVLC(
            "--avcodec-hw=any",
            "--no-stats",
            "--no-video-title-show",
            "--network-caching=300",
            "--clock-jitter=0",
            "--no-osd"));

        sc.AddSingleton<TimelineSettingsService>();
        sc.AddSingleton<ITimelineContextService, TimelineContextService>();
        sc.AddSingleton<IDialogService, AvaloniaDialogService>();

        // MainWindowViewModel is transient so each app launch gets a fresh instance
        sc.AddTransient<MainWindowViewModel>();

        Services = sc.BuildServiceProvider();
        // ───────────────────────────────────────────────────────────────────────

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
