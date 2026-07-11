using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.Views.Timeline;

internal static class TimelineTextInputDialog
{
    public static async Task<string?> ShowAsync(Control host, string prompt)
    {
        Window dialog = new()
        {
            Title = prompt,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 320,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        TextBox input = new() { Width = 440 };
        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        StackPanel content = new() { Margin = new Thickness(6), Children = { input, footer } };
        dialog.Content = content;

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = input.Text;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        Window owner = TopLevel.GetTopLevel(host) as Window
            ?? throw new InvalidOperationException("No owner window available");
        await dialog.ShowDialog(owner);
        return result;
    }
}
