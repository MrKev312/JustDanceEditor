using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class MoveClipViewModel : ClipViewModel
{
    [Inspectable("Move Id", "Move")]
    [ObservableProperty]
    public partial string MoveId { get; set; } = string.Empty;

    public bool IsFullBody { get; }

    /// <summary>
    /// The shared source-of-truth for this move's properties (color, default duration)
    /// </summary>
    public MoveDefinitionViewModel? Definition { get; private set; }

    private int _suppressDefinitionColorUpdateCount;

    private void PushSuppressDefinitionColorUpdates() => _suppressDefinitionColorUpdateCount++;
    private void PopSuppressDefinitionColorUpdates()
    {
        if (_suppressDefinitionColorUpdateCount > 0)
            _suppressDefinitionColorUpdateCount--;
    }
    public void BeginSuppressDefinitionColorUpdates() => PushSuppressDefinitionColorUpdates();
    public void EndSuppressDefinitionColorUpdates() => PopSuppressDefinitionColorUpdates();
    private bool IsSuppressingDefinitionColorUpdates => _suppressDefinitionColorUpdateCount > 0;

    [Inspectable("Gold Move", "Move")]
    [ObservableProperty]
    public partial bool IsGoldMove { get; set; }

    public MoveClipViewModel(MoveClip clip, double duration, Color color, string moveId, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null, bool isFullBody = false)
        : base(clip, duration, color, moveId, rootPath, parentTimeline)
    {
        MoveId = clip.MoveId;
        Name = moveId;
        IsFullBody = isFullBody;
        IsGoldMove = clip.IsGoldMove;

        // When duration changes, broadcast so UI can update
        PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats))
            {
                NotifyClipDataChanged(nameof(DurationBeats));

                // Keep duration-sync logic: synchronize duration across sibling moves of the same body type and MoveId
                if (_parentTimeline != null)
                {
                    List<MoveClipViewModel> siblings = [.. _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().Where(c => c.MoveId == MoveId && c.IsFullBody == IsFullBody)];
                    foreach (MoveClipViewModel sibling in siblings)
                    {
                        if (!ReferenceEquals(sibling, this) && System.Math.Abs(sibling.DurationBeats - DurationBeats) > 1e-9)
                            sibling.DurationBeats = DurationBeats;
                    }
                }
            }
        };

        // Establish definition if we have a timeline context
        if (_parentTimeline != null)
        {
            try
            {
                MoveDefinitionViewModel def = _parentTimeline.GetOrRegisterMove(MoveId, IsFullBody);
                Definition?.PropertyChanged -= OnDefinitionPropertyChanged;

                Definition = def;

                // Initialize background with definition color without propagating back to definition
                try
                {
                    PushSuppressDefinitionColorUpdates();
                    if (!Equals(BackgroundColor, def.Color))
                        BackgroundColor = def.Color;
                }
                finally
                {
                    PopSuppressDefinitionColorUpdates();
                }

                // Listen for definition color changes
                Definition.PropertyChanged += OnDefinitionPropertyChanged;
            }
            catch { }
        }
    }

    partial void OnMoveIdChanged(string value)
    {
        if (RawClip is MoveClip m)
        {
            m.MoveId = value;
            Name = value;

            // If we have a parent timeline, update our definition pointer
            if (_parentTimeline != null)
            {
                var def = _parentTimeline.GetOrRegisterMove(value, IsFullBody);
                if (!ReferenceEquals(def, Definition))
                {
                    if (Definition != null)
                        Definition.PropertyChanged -= OnDefinitionPropertyChanged;

                    Definition = def;
                    Definition.PropertyChanged += OnDefinitionPropertyChanged;

                    // adopt the definition color (use suppression and normalize alpha to opaque)
                    var defColorNormalized = new Color(255, def.Color.R, def.Color.G, def.Color.B);
                    try
                    {
                        PushSuppressDefinitionColorUpdates();
                        if (!Equals(this.BackgroundColor, defColorNormalized))
                            this.BackgroundColor = defColorNormalized;
                    }
                    finally
                    {
                        PopSuppressDefinitionColorUpdates();
                    }

                    // adopt the definition duration (update UI length to match new move)
                    double newDurationBeats = def.DefaultDuration / 24.0;
                    if (System.Math.Abs(DurationBeats - newDurationBeats) > 1e-9)
                        DurationBeats = newDurationBeats;
                }
            }

            NotifyClipDataChanged(nameof(MoveId));
        }
    }

    partial void OnIsGoldMoveChanged(bool value)
    {
        if (RawClip is MoveClip m)
        {
            m.IsGoldMove = value;
            NotifyClipDataChanged(nameof(IsGoldMove));
        }
    }

    private void OnDefinitionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoveDefinitionViewModel.Color) && Definition != null)
        {
            // When the definition color changes, just notify listeners that our render color changed
            // Do not set BackgroundColor to avoid storing duplicate color state on clips
            NotifyClipDataChanged(nameof(BackgroundColor));
            // Also raise property changed for a new RenderColor property if needed (we'll add RenderColor override)
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(RenderColor)));
        }
    }

    public override Color RenderColor => Definition != null ? new Color(255, Definition.Color.R, Definition.Color.G, Definition.Color.B) : base.RenderColor;

    // Shadow the base BackgroundColor property so the Properties panel reads/writes the Definition color
    [Inspectable("Color", "Appearance")]
    public new Color BackgroundColor
    {
        get => Definition != null ? new Color(255, Definition.Color.R, Definition.Color.G, Definition.Color.B) : base.BackgroundColor;
        set
        {
            // When user edits BackgroundColor on a MoveClip, redirect to the definition color
            if (Definition != null)
            {
                // Normalize and assign
                Color normalized = new(255, value.R, value.G, value.B);
                Definition.Color = normalized;
            }
            else
            {
                base.BackgroundColor = value;
            }
        }
    }
    protected override void OnBackgroundColorChangedCore(Color value)
    {
        // When a move's color changes via the Properties editor or similar, update the shared Definition so other clips and the Library update automatically
        if (Definition == null)
        {
            base.OnBackgroundColorChangedCore(value);
            return;
        }

        // If we're suppressing updates (e.g., adopting a definition color on MoveId change), do not propagate back to Definition
        if (IsSuppressingDefinitionColorUpdates)
        {
            base.OnBackgroundColorChangedCore(value);
            return;
        }

        // Guard to avoid recursion
        if (Equals(value, Definition.Color))
        {
            base.OnBackgroundColorChangedCore(value);
            return;
        }

        // Normalize color alpha to fully opaque to avoid accidental transparency making items render 'white'
        Color normalized = new(255, value.R, value.G, value.B);
        Definition.Color = normalized;

        // Also notify listeners
        base.OnBackgroundColorChangedCore(value);
    }

    /// <summary>
    /// Show a small dialog to pick a Move, duration and gold flag. Returns (moveId, frames, isGold) or null if cancelled.
    /// </summary>
    public static async Task<(string moveId, int frames, bool isGold)?> ShowCreateDialogAsync(Window? owner, bool isFull, TimelineEditorViewModel vm)
    {
        Window ownerWindow = owner ?? (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime al && al.MainWindow is Window mw ? mw : null) ?? throw new InvalidOperationException("No owner window available");

        Window win = new()
        {
            Title = "Add Move",
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
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        List<string> moves = [.. isFull ? vm.AvailableFullBodyCoachMoves : vm.AvailableHandCoachMoves];
        ComboBox combo = new() { Width = 260 };
        if (combo.Items is System.Collections.IList mlist2)
        {
            foreach (string it in moves)
                mlist2.Add(it);
            if (mlist2.Count > 0)
                combo.SelectedIndex = 0;
        }

        grid.Children.Add(new TextBlock { Text = "Move:", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 0);
        grid.Children.Add(combo);
        Grid.SetRow(grid.Children[^1], 0);
        Grid.SetColumn(grid.Children[^1], 1);

        // Read-only duration display (beats) derived from the selected move's definition
        TextBlock durationLabel = new() { Text = "Duration (beats):", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(durationLabel, 1);
        Grid.SetColumn(durationLabel, 0);
        grid.Children.Add(durationLabel);

        TextBlock durationValue = new() { Text = "1", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(durationValue, 1);
        Grid.SetColumn(durationValue, 1);
        grid.Children.Add(durationValue);

        // Update duration display when selection changes
        combo.SelectionChanged += (s, e) =>
        {
            string? sel = combo.SelectedItem as string;
            if (!string.IsNullOrEmpty(sel))
            {
                try
                {
                    MoveDefinitionViewModel def = vm.GetOrRegisterMove(sel, isFull);
                    double frames = def?.DefaultDuration ?? 24.0;
                    double beats = frames / 24.0;
                    durationValue.Text = beats.ToString("0.##");
                }
                catch
                {
                    durationValue.Text = "1";
                }
            }
            else
            {
                durationValue.Text = string.Empty;
            }
        };

        CheckBox goldCheck = new() { Content = "Gold Move", IsChecked = false };
        grid.Children.Add(goldCheck);
        Grid.SetRow(grid.Children[^1], 2);
        Grid.SetColumn(grid.Children[^1], 1);

        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button ok = new() { Content = "OK", Margin = new Thickness(6) };
        Button cancel = new() { Content = "Cancel", Margin = new Thickness(6) };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        grid.Children.Add(footer);
        Grid.SetRow(grid.Children[^1], 4 - 1);
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

        string? picked = combo.SelectedItem as string;
        if (confirmed && !string.IsNullOrEmpty(picked))
        {
            // Look up the move definition and use its default duration (frames). Fall back to 24 if not found.
            double defFrames = 24.0;
            try
            {
                MoveDefinitionViewModel def = vm.GetOrRegisterMove(picked, isFull);
                if (def != null)
                    defFrames = def.DefaultDuration;
            }
            catch { }

            int frames = (int)defFrames;
            bool isGold = goldCheck.IsChecked == true;
            return (picked, frames, isGold);
        }

        return null;
    }
}