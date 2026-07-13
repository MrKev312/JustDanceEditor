using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using KevInc.Avalonia;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Services;

public sealed class AvaloniaEditorPromptService(IWindowService windows) : IEditorPromptService
{
    public async Task<string?> PromptTextAsync(string title, string label, string initialValue = "")
    {
        Window? owner = windows.MainWindow;
        if (owner == null)
            return null;

        Window dialog = CreateDialog(title, 360, 150);
        TextBox textBox = new() { Text = initialValue, MinWidth = 260 };
        string? result = null;
        Button okButton = new() { Content = "OK", IsDefault = true, MinWidth = 80 };
        Button cancelButton = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        okButton.Click += (_, _) =>
        {
            result = textBox.Text?.Trim();
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Content = CreateSurface(dialog, 12,
            new TextBlock { Text = label },
            textBox,
            CreateButtonRow(okButton, cancelButton));

        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        Window? owner = windows.MainWindow;
        if (owner == null)
            return false;

        Window dialog = CreateDialog(title, 380, 145);
        bool result = false;
        Button yesButton = new() { Content = "Yes", IsDefault = true, MinWidth = 80 };
        Button noButton = new() { Content = "No", IsCancel = true, MinWidth = 80 };
        yesButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        noButton.Click += (_, _) => dialog.Close();
        dialog.Content = CreateSurface(dialog, 16,
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CreateButtonRow(yesButton, noButton));

        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        Window? owner = windows.MainWindow;
        if (owner == null)
            return;

        Window dialog = CreateDialog(title, 420, 180);
        Button okButton = new() { Content = "OK", IsDefault = true, IsCancel = true, MinWidth = 80 };
        okButton.Click += (_, _) => dialog.Close();
        dialog.Content = CreateSurface(dialog, 16,
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CreateButtonRow(okButton));
        await dialog.ShowDialog(owner);
    }

    public Task ShowErrorAsync(string title, string message, Exception? exception = null)
    {
        if (exception != null)
            EditorLog.Unexpected(exception, title);
        return ShowMessageAsync(title, message);
    }

    public async Task<UnsavedChangesChoice> PromptUnsavedChangesAsync(string documentTitle)
    {
        Window? owner = windows.MainWindow;
        if (owner == null)
            return UnsavedChangesChoice.Cancel;

        Window dialog = CreateDialog("Unsaved Changes", 440, 160);
        UnsavedChangesChoice result = UnsavedChangesChoice.Cancel;
        Button saveButton = new() { Content = "Save", Width = 96 };
        Button discardButton = new() { Content = "Don't Save", Width = 96 };
        Button cancelButton = new() { Content = "Cancel", Width = 96, IsCancel = true };
        saveButton.Click += (_, _) => CloseWith(UnsavedChangesChoice.Save);
        discardButton.Click += (_, _) => CloseWith(UnsavedChangesChoice.Discard);
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Content = CreateSurface(dialog, 16,
            new TextBlock
            {
                Text = $"\"{documentTitle}\" has unsaved changes. Do you want to save before closing?",
                TextWrapping = TextWrapping.Wrap
            },
            CreateButtonRow(saveButton, discardButton, cancelButton));

        await dialog.ShowDialog(owner);
        return result;

        void CloseWith(UnsavedChangesChoice choice)
        {
            result = choice;
            dialog.Close();
        }
    }

    private static Window CreateDialog(string title, double width, double height)
    {
        Window dialog = new()
        {
            Title = title,
            Width = width,
            Height = height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        PlatformTheme.ApplyFloatingWindowChrome(dialog);
        return dialog;
    }

    private static StackPanel CreateButtonRow(params Button[] buttons)
    {
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        row.Children.AddRange(buttons);
        return row;
    }

    private static Border CreateSurface(Control owner, double spacing, params Control[] children)
    {
        StackPanel panel = new() { Spacing = spacing };
        panel.Children.AddRange(children);
        return new Border
        {
            Background = FindBrush(owner, "WindowBackgroundBrush", "#D820242A"),
            BorderBrush = FindBrush(owner, "SurfaceBorderBrush", "#66FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = panel
        };
    }

    private static IBrush FindBrush(Control owner, string resourceKey, string fallbackColor)
        => owner.TryFindResource(resourceKey, null, out object? resource) && resource is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse(fallbackColor));
}
