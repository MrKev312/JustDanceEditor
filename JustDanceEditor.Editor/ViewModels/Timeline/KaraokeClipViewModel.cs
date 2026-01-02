using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class KaraokeClipViewModel : ClipViewModel
{
    [Inspectable("Lyrics", "Karaoke")]
    [ObservableProperty]
    public partial string Lyrics { get; set; } = string.Empty;

    [Inspectable("End of Line", "Karaoke")]
    [ObservableProperty]
    public partial bool IsEndOfLine { get; set; }

    public KaraokeClipViewModel(KaraokeClip clip, double duration, Color color, string lyrics, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, lyrics, rootPath, parentTimeline)
    {
        Lyrics = clip.Lyrics ?? string.Empty;
        IsEndOfLine = clip.IsEndOfLine;
        // keep Name in sync for display
        Name = Lyrics;

        // Keep duration in sync with underlying model when edited
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is KaraokeClip k)
            {
                k.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };

        // If we have a parent timeline, subscribe to timeline PropertyChanged so
        // we can refresh rendering when the lyrics definition color changes.
        if (_parentTimeline != null)
        {
            try
            {
                _parentTimeline.PropertyChanged += OnParentTimelinePropertyChanged;
            }
            catch { }
        }
    }

    partial void OnLyricsChanged(string value)
    {
        if (RawClip is KaraokeClip k)
        {
            k.Lyrics = value;
            Name = value;
            // notify listeners
            NotifyClipDataChanged(nameof(Lyrics));
        }
    }

    partial void OnIsEndOfLineChanged(bool value)
    {
        if (RawClip is KaraokeClip k)
        {
            k.IsEndOfLine = value;
            NotifyClipDataChanged(nameof(IsEndOfLine));
        }
    }

    public override Color RenderColor => _parentTimeline != null ? new Color(255, _parentTimeline.LyricsDefinitionColor.R, _parentTimeline.LyricsDefinitionColor.G, _parentTimeline.LyricsDefinitionColor.B) : base.RenderColor;

    // Shadow BackgroundColor so Properties panel reads/writes the LyricsDefinition color
    [Inspectable("Color", "Appearance")]
    public new Color BackgroundColor
    {
        get => _parentTimeline != null ? new Color(255, _parentTimeline.LyricsDefinitionColor.R, _parentTimeline.LyricsDefinitionColor.G, _parentTimeline.LyricsDefinitionColor.B) : base.BackgroundColor;
        set
        {
            if (_parentTimeline != null)
            {
                Color normalized = new(255, value.R, value.G, value.B);
                _parentTimeline.LyricsDefinitionColor = normalized;
            }
            else
            {
                base.BackgroundColor = value;
            }
        }
    }

    protected override void OnBackgroundColorChangedCore(Color value)
    {
        // Do not update timeline metadata from individual clip changes anymore.
        // Timeline-level lyrics color is the single source-of-truth and will be
        // set via the LyricsDefinition color by the Properties editor.

        // Always invoke base to notify listeners
        base.OnBackgroundColorChangedCore(value);
    }

    private void OnParentTimelinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is (nameof(TimelineEditorViewModel.LyricsDefinitionColor)) or (nameof(TimelineEditorViewModel.LyricsColor)))
        {
            // When the timeline-level lyrics color changes, update rendering
            NotifyClipDataChanged(nameof(BackgroundColor));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(RenderColor)));
        }
    }

    /// <summary>
    /// Show a small dialog to input lyrics text, duration and end-of-line flag. Returns (lyrics, frames, isEndOfLine) or null if cancelled.
    /// </summary>
    public static async Task<(string lyrics, int frames, bool isEndOfLine)?> ShowCreateDialogAsync(Window? owner)
    {
        Window ownerWindow = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null) ?? throw new InvalidOperationException("No owner window available");

        Window win = new()
        {
            Title = "Add Lyrics",
            Width = 420,
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

        TextBox box = new() { Width = 260 };
        grid.Children.Add(new TextBlock { Text = "Lyrics:", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(box);
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 1);

        NumericUpDown durationBox = new() { Minimum = 0.0M, Maximum = 1000.0M, Value = 1.0M, Width = 120 };
        grid.Children.Add(new TextBlock { Text = "Duration (beats):", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 1);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(durationBox);
        Grid.SetRow(grid.Children[^1], 1);
        Grid.SetColumn(grid.Children[^1], 1);

        CheckBox endCheck = new() { Content = "End of line", IsChecked = false };
        grid.Children.Add(endCheck);
        Grid.SetRow(grid.Children[^1], 2);
        Grid.SetColumn(grid.Children[^1], 1);

        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        grid.Children.Add(footer);
        Grid.SetRow(grid.Children[^1], 3 - 1);
        Grid.SetColumn(grid.Children[^1], 0);
        Grid.SetColumnSpan(grid.Children[^1], 2);

        win.Content = grid;

        bool confirmed = false;
        ok.Click += (s, ev) =>
        {
            confirmed = true;
            win.Close();
        };
        cancel.Click += (s, ev) =>
        {
            box.Text = null;
            win.Close();
        };

        await win.ShowDialog(ownerWindow);

        if (confirmed && !string.IsNullOrEmpty(box.Text))
        {
            int frames = (int)((double)(durationBox.Value ?? 1.0M) * 24.0);
            return (box.Text, frames, endCheck.IsChecked == true);
        }

        return null;
    }
}