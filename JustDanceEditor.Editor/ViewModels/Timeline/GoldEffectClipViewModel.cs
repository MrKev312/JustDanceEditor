using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class GoldEffectClipViewModel : ClipViewModel
{
    public GoldEffectClipViewModel(GoldEffectClip clip, double duration, Color color, string name, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, name, rootPath, parentTimeline)
    {
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is GoldEffectClip g)
            {
                g.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };
    }

    /// <summary>
    /// Show a small dialog to configure a gold effect. Returns (frames, effectType) or null if cancelled.
    /// </summary>
    public static async Task<(int frames, int effectType)?> ShowCreateDialogAsync(Window? owner)
    {
        Window ownerWindow = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null) ?? throw new InvalidOperationException("No owner window available");

        Window win = new()
        {
            Title = "Add Gold Effect",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        Grid grid = new() { Margin = new Thickness(6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        NumericUpDown durationBox = new() { Minimum = 0.0M, Maximum = 1000.0M, Value = 1.0M, Width = 120 };
        grid.Children.Add(new TextBlock { Text = "Duration (beats):", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(durationBox);
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 1);

        NumericUpDown effectBox = new() { Minimum = 0.0M, Maximum = 1000.0M, Value = 0.0M, Width = 120 };
        grid.Children.Add(new TextBlock { Text = "Effect Type (int):", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 1);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(effectBox);
        Grid.SetRow(grid.Children[^1], 1);
        Grid.SetColumn(grid.Children[^1], 1);

        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        grid.Children.Add(footer);
        Grid.SetRow(grid.Children[^1], 2);
        Grid.SetColumn(grid.Children[^1], 0);
        Grid.SetColumnSpan(grid.Children[^1], 2);

        win.Content = grid;

        bool confirmed = false;
        ok.Click += (s, ev) =>
        {
            confirmed = true;
            win.Close();
        };
        cancel.Click += (s, ev) => win.Close();

        await win.ShowDialog(ownerWindow);

        if (confirmed)
        {
            int frames = (int)((double)(durationBox.Value ?? 1.0M) * 24.0);
            int effectType = (int)(double)(effectBox.Value ?? 0.0M);
            return (frames, effectType);
        }

        return null;
    }
}