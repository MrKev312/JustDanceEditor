using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

using KevInc.Avalonia;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal static class RecordingsToolDialogs
{
    public static async Task<bool> ShowDiscardPromptAsync()
    {
        Window dialog = new()
        {
            Title = "Discard Recording?",
            Width = 420,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        PlatformTheme.ApplyFloatingWindowChrome(dialog);

        bool result = false;
        Button yesButton = new() { Content = "Yes", IsDefault = true, MinWidth = 80 };
        Button noButton = new() { Content = "No", IsCancel = true, MinWidth = 80 };
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        noButton.Click += (_, _) => dialog.Close();

        dialog.Content = CreateDialogSurface(dialog,
            new TextBlock
            {
                Text = "Changing playback will discard the current recording attempt.",
                TextWrapping = TextWrapping.Wrap
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { yesButton, noButton }
            });

        Window? owner = GetMainWindow();
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return result;
    }

    public static async Task<IReadOnlyList<RecordingSelectionItem>?> ShowRecordingSelectionDialogAsync(
        IReadOnlyList<RecordingSelectionItem> recordings)
    {
        Window dialog = new()
        {
            Title = "Generate MSMs",
            Width = 460,
            Height = 430,
            MinWidth = 380,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        PlatformTheme.ApplyFloatingWindowChrome(dialog);

        List<(RecordingSelectionItem Item, CheckBox CheckBox)> rows = [];
        StackPanel listPanel = new() { Spacing = 4 };
        foreach (RecordingSelectionItem item in recordings)
        {
            CheckBox checkBox = new()
            {
                Content = item.DisplayName,
                IsChecked = item.IsSelected,
                MinHeight = 24
            };
            rows.Add((item, checkBox));
            listPanel.Children.Add(checkBox);
        }

        IReadOnlyList<RecordingSelectionItem>? selected = null;
        Button generateButton = new() { Content = "Generate", IsDefault = true, MinWidth = 90 };
        Button cancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        Button allButton = new() { Content = "All", MinWidth = 64 };
        Button noneButton = new() { Content = "None", MinWidth = 64 };

        generateButton.Click += (_, _) =>
        {
            selected = rows
                .Where(static row => row.CheckBox.IsChecked == true)
                .Select(static row => row.Item)
                .ToArray();
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        allButton.Click += (_, _) =>
        {
            foreach ((_, CheckBox checkBox) in rows)
                checkBox.IsChecked = true;
        };
        noneButton.Click += (_, _) =>
        {
            foreach ((_, CheckBox checkBox) in rows)
                checkBox.IsChecked = false;
        };

        dialog.Content = CreateDialogSurface(dialog,
            new TextBlock
            {
                Text = "Select the recordings that should overwrite the existing MSM files.",
                TextWrapping = TextWrapping.Wrap
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Spacing = 8,
                Children = { allButton, noneButton }
            },
            new ScrollViewer
            {
                MaxHeight = 250,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = listPanel
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { generateButton, cancelButton }
            });

        Window? owner = GetMainWindow();
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return selected;
    }

    public static async Task<bool> ShowDeleteConfirmationAsync(string displayName)
    {
        Window dialog = new()
        {
            Title = "Delete Recording?",
            Width = 420,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        PlatformTheme.ApplyFloatingWindowChrome(dialog);

        bool result = false;
        Button deleteButton = new() { Content = "Delete", IsDefault = true, MinWidth = 88 };
        Button cancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        deleteButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = CreateDialogSurface(dialog,
            new TextBlock
            {
                Text = $"Delete {displayName}?",
                TextWrapping = TextWrapping.Wrap
            },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { deleteButton, cancelButton }
            });

        Window? owner = GetMainWindow();
        if (owner != null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return result;
    }

    private static Border CreateDialogSurface(Control owner, params Control[] children)
    {
        StackPanel panel = new()
        {
            Spacing = 16
        };

        foreach (Control child in children)
            panel.Children.Add(child);

        return new Border
        {
            Background = FindBrush(owner, "JdeWindowBackgroundBrush", "#D820242A"),
            BorderBrush = FindBrush(owner, "JdeSurfaceBorderBrush", "#66FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = panel
        };
    }

    private static IBrush FindBrush(Control owner, string resourceKey, string fallbackColor)
    {
        if (owner.TryFindResource(resourceKey, null, out object? resource) && resource is IBrush brush)
            return brush;

        return new SolidColorBrush(Color.Parse(fallbackColor));
    }

    private static Window? GetMainWindow()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}