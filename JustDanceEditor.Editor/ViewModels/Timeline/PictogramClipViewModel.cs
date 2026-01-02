using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class PictogramClipViewModel : ClipViewModel
{
    [Inspectable("Pictogram Id", "Pictogram")]
    [ObservableProperty]
    public partial string PictogramId { get; set; } = string.Empty;

    public PictogramClipViewModel(PictogramClip clip, double duration, Color color, string pictogramId, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null)
        : base(clip, duration, color, pictogramId, rootPath, parentTimeline)
    {
        PictogramId = clip.PictogramId ?? string.Empty;
        if (!string.IsNullOrEmpty(PictogramId) && rootPath != null)
            ImagePath = System.IO.Path.Combine(rootPath, "assets", "pictograms", $"{PictogramId}.webp");

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && RawClip is PictogramClip p)
            {
                p.Duration = (int)(DurationBeats * 24);
                NotifyClipDataChanged(nameof(DurationBeats));
            }
        };
    }

    partial void OnPictogramIdChanged(string value)
    {
        if (RawClip is PictogramClip p)
        {
            p.PictogramId = value;
            if (!string.IsNullOrEmpty(value) && _rootPath != null)
                ImagePath = System.IO.Path.Combine(_rootPath, "assets", "pictograms", $"{value}.webp");

            NotifyClipDataChanged(nameof(PictogramId));
        }
    }

    /// <summary>
    /// Show a small dialog to choose a pictogram and duration. Returns (pictogramId, frames) or null if cancelled.
    /// </summary>
    public static async Task<(string pictogramId, int frames)?> ShowCreateDialogAsync(Window? owner, TimelineEditorViewModel vm)
    {
        Window ownerWindow = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null) ?? throw new InvalidOperationException("No owner window available");

        Window win = new()
        {
            Title = "Add Pictogram",
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

        List<KeyValuePair<string, string>> pictos = [.. (vm.AvailablePictograms ?? []).Select(id => new KeyValuePair<string, string>(id, System.IO.Path.Combine(vm.RootPath ?? string.Empty, "assets", "pictograms", id + ".webp")))];

        ComboBox combo = new() { Width = 260 };
        if (combo.Items is System.Collections.IList listPic)
        {
            foreach (KeyValuePair<string, string> kv in pictos)
                listPic.Add(kv);
            if (listPic.Count > 0)
                combo.SelectedIndex = 0;
        }

        combo.ItemTemplate = new FuncDataTemplate<KeyValuePair<string, string>>((kv, ns) =>
        {
            StackPanel sp = new() { Orientation = Orientation.Horizontal };
            Image img = new() { Width = 40, Height = 40 };
            try
            {
                if (!string.IsNullOrEmpty(kv.Value) && System.IO.File.Exists(kv.Value))
                    img.Source = new Avalonia.Media.Imaging.Bitmap(kv.Value);
            }
            catch { }

            sp.Children.Add(img);
            sp.Children.Add(new TextBlock { Text = kv.Key, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }, true);

        grid.Children.Add(new TextBlock { Text = "Pictogram:", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(combo);
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 1);

        NumericUpDown durationBox = new() { Minimum = 0.0M, Maximum = 1000.0M, Value = 1.0M, Width = 120 };
        grid.Children.Add(new TextBlock { Text = "Duration (beats):", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 1);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(durationBox);
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
        cancel.Click += (s, ev) =>
        {
            combo.SelectedItem = null;
            win.Close();
        };

        await win.ShowDialog(ownerWindow);

        if (confirmed && combo.SelectedItem is KeyValuePair<string, string> pickedKv)
        {
            int frames = (int)((double)(durationBox.Value ?? 1.0M) * 24.0);
            return (pickedKv.Key, frames);
        }

        return null;
    }
}