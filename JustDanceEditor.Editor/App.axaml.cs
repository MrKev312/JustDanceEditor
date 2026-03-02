using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.Views;

using LibVLCSharp.Shared;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Linq;

namespace JustDanceEditor.Editor;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    /// <summary>Convenience accessor — keeps existing code-behind references working.</summary>
    public LibVLC LibVLC => Services.GetRequiredService<LibVLC>();

    /// <summary>Convenience accessor — keeps existing code-behind references working.</summary>
    public ITimelineContextService TimelineContext => Services.GetRequiredService<ITimelineContextService>();

    /// <summary>Convenience accessor — keeps existing code-behind references working.</summary>
    public IDialogService DialogService => Services.GetRequiredService<IDialogService>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ── Build DI container ──────────────────────────────────────────────────
        ServiceCollection sc = new();

        sc.AddSingleton<LibVLC>(_ => new LibVLC(
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
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        DataAnnotationsValidationPlugin[] dataValidationPluginsToRemove =
            [.. BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>()];

        // remove each entry found
        foreach (DataAnnotationsValidationPlugin? plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
