using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;

using Dock.Model.Core;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

internal sealed class TimelineCloseController(TimelineEditorViewModel timeline, Func<string> getBaseTitle)
{
    private bool _allowClose;

    public bool ShouldClose()
    {
        if (timeline.UndoService.IsDirty && !_allowClose)
        {
            Dispatcher.UIThread.InvokeAsync(PromptSaveOnCloseAsync);
            return false;
        }

        timeline.Playback.Dispose();
        return true;
    }

    public async Task<bool> TrySaveAndReportFailureAsync(Window? owner)
    {
        try
        {
            timeline.Save();
            return true;
        }
        catch (Exception ex)
        {
            await ShowSaveErrorAsync(owner, ex);
            return false;
        }
    }

    public async Task<bool> TrySaveAndCloseAfterPromptAsync(Window? owner)
    {
        if (!await TrySaveAndReportFailureAsync(owner))
            return false;

        CloseAfterConfirmedPrompt();
        return true;
    }

    public static async Task ShowSaveErrorAsync(Window? owner, Exception exception)
    {
        Debug.WriteLine($"Failed to save map: {exception}");

        if (owner == null)
            return;

        Window errorWin = new()
        {
            Title = "Error Saving Map",
            Width = 420,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Text = $"Failed to save map:\n{exception.Message}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };

        await errorWin.ShowDialog(owner);
    }

    private void CloseAfterConfirmedPrompt()
    {
        _allowClose = true;
        (timeline.Owner as IDock)?.Factory?.CloseDockable(timeline);
    }

    private async Task PromptSaveOnCloseAsync()
    {
        Window? mainWindow =
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime al
                ? al.MainWindow
                : null;
        if (mainWindow == null)
            return;

        Button saveBtn = new() { Content = "Save", Width = 96, Margin = new Thickness(6) };
        Button discardBtn = new() { Content = "Don't Save", Width = 96, Margin = new Thickness(6) };
        Button cancelBtn = new() { Content = "Cancel", Width = 96, Margin = new Thickness(6) };

        StackPanel buttons = new()
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        buttons.Children.AddRange([saveBtn, discardBtn, cancelBtn]);

        StackPanel body = new() { Margin = new Thickness(20, 16, 20, 12) };
        body.Children.Add(new TextBlock
        {
            Text = $"\"{getBaseTitle()}\" has unsaved changes. Do you want to save before closing?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        body.Children.Add(buttons);

        Window dialog = new()
        {
            Title = "Unsaved Changes",
            Width = 440,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body
        };

        string choice = "cancel";
        saveBtn.Click += (_, _) =>
        {
            choice = "save";
            dialog.Close();
        };
        discardBtn.Click += (_, _) =>
        {
            choice = "discard";
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(mainWindow);

        if (choice == "save")
        {
            await TrySaveAndCloseAfterPromptAsync(mainWindow);
            return;
        }

        if (choice == "discard")
            CloseAfterConfirmedPrompt();
    }
}