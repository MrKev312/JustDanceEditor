using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public partial class MoveClipViewModel : ClipViewModel, IHasSharedColorSource, IHasDynamicOptions
{
    public override bool IsResizable => true;
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

        // When duration changes, synchronize siblings of the same move across all tracks.
        PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(DurationBeats) && _parentTimeline != null)
            {
                // Keep duration-sync logic: synchronize duration across sibling moves of the same body type and MoveId
                List<MoveClipViewModel> siblings = [.. _parentTimeline.Tracks.SelectMany(t => t.Clips).OfType<MoveClipViewModel>().Where(c => c.MoveId == MoveId && c.IsFullBody == IsFullBody)];
                foreach (MoveClipViewModel sibling in siblings)
                {
                    if (!ReferenceEquals(sibling, this) && Math.Abs(sibling.DurationBeats - DurationBeats) > 1e-9)
                        sibling.DurationBeats = DurationBeats;
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
                    if (Math.Abs(DurationBeats - newDurationBeats) > 1e-9)
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

    // IHasSharedColorSource
    public (object Target, string PropertyName)? GetColorEditTarget(string inspectedProperty, TimelineEditorViewModel timeline)
    {
        if (inspectedProperty != nameof(BackgroundColor) || Definition == null)
            return null;
        return (Definition, nameof(MoveDefinitionViewModel.Color));
    }

    // IHasDynamicOptions
    public IEnumerable<object>? GetDynamicOptions(string propertyName, TimelineEditorViewModel timeline)
    {
        if (propertyName != nameof(MoveId))
            return null;
        return IsFullBody
            ? timeline.AvailableFullBodyCoachMoves.Cast<object>()
            : timeline.AvailableHandCoachMoves.Cast<object>();
    }

    public bool IsDynamicPropertyEditable(string propertyName) => false;
}