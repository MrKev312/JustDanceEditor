using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace JustDanceEditor.Editor.ViewModels.Timeline;

public class MoveClipViewModel : ClipViewModel, IHasSharedColorSource, IHasDynamicOptions
{
    private readonly int _fallbackDurationFrames;

    private MoveClip MoveClip => (MoveClip)RawClip;

    public override bool IsResizable => true;

    [Inspectable("Move Id", "Move")]
    public string MoveId
    {
        get => MoveClip.MoveId;
        set
        {
            value ??= string.Empty;
            if (string.Equals(MoveClip.MoveId, value, StringComparison.Ordinal))
                return;

            double oldDurationBeats = DurationBeats;
            Color oldBackgroundColor = BackgroundColor;

            MoveClip.MoveId = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(MoveId)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));

            AttachDefinition(value);

            if (Math.Abs(DurationBeats - oldDurationBeats) > 1e-9)
            {
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(DurationBeats)));
                NotifyClipDataChanged(nameof(DurationBeats));
            }

            if (BackgroundColor != oldBackgroundColor)
            {
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(BackgroundColor)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(RenderColor)));
                NotifyClipDataChanged(nameof(BackgroundColor));
            }

            NotifyClipDataChanged(nameof(MoveId));
            NotifyClipDataChanged(nameof(Name));
        }
    }

    public bool IsFullBody { get; }

    /// <summary>
    /// The shared source-of-truth for this move's properties (color, default duration)
    /// </summary>
    public MoveDefinitionViewModel? Definition { get; private set; }

    [Inspectable("Gold Move", "Move")]
    public bool IsGoldMove
    {
        get => MoveClip.IsGoldMove;
        set
        {
            if (MoveClip.IsGoldMove == value)
                return;

            MoveClip.IsGoldMove = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsGoldMove)));
            NotifyClipDataChanged(nameof(IsGoldMove));
        }
    }

    public override string Name
    {
        get => MoveId;
        set => MoveId = value;
    }

    public MoveClipViewModel(MoveClip clip, string? rootPath = null, TimelineEditorViewModel? parentTimeline = null, bool isFullBody = false, int fallbackDurationFrames = 24)
        : base(clip, Colors.LightGray, clip.MoveId, rootPath, parentTimeline)
    {
        _fallbackDurationFrames = fallbackDurationFrames;
        IsFullBody = isFullBody;
        AttachDefinition(MoveId);
    }

    protected override int GetDurationFrames() => Definition != null ? (int)Definition.DefaultDuration : _fallbackDurationFrames;

    protected override void SetDurationFrames(int frames)
    {
        if (Definition != null)
        {
            if (Math.Abs(Definition.DefaultDuration - frames) > 1e-9)
                Definition.DefaultDuration = frames;
        }
    }

    protected override void OnDurationBeatsChanged()
    {
        if (_parentTimeline == null)
            return;

        List<MoveClipViewModel> siblings = [.. _parentTimeline.Tracks
            .SelectMany(t => t.Clips)
            .OfType<MoveClipViewModel>()
            .Where(c => !ReferenceEquals(c, this) && c.MoveId == MoveId && c.IsFullBody == IsFullBody)];

        foreach (MoveClipViewModel sibling in siblings)
        {
            if (Math.Abs(sibling.DurationBeats - DurationBeats) > 1e-9)
                sibling.OnPropertyChanged(new PropertyChangedEventArgs(nameof(DurationBeats)));
        }
    }

    private void AttachDefinition(string moveId)
    {
        if (Definition != null)
            Definition.PropertyChanged -= OnDefinitionPropertyChanged;

        Definition = null;

        if (_parentTimeline == null)
            return;

        try
        {
            Definition = _parentTimeline.GetOrRegisterMove(moveId, IsFullBody);
            Definition.PropertyChanged += OnDefinitionPropertyChanged;
        }
        catch
        {
            Definition = null;
        }
    }

    private void OnDefinitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Definition == null)
            return;

        if (e.PropertyName == nameof(MoveDefinitionViewModel.Color))
        {
            NotifyClipDataChanged(nameof(BackgroundColor));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(BackgroundColor)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(RenderColor)));
        }

        if (e.PropertyName == nameof(MoveDefinitionViewModel.DefaultDuration))
        {
            NotifyClipDataChanged(nameof(DurationBeats));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(DurationBeats)));
        }

        if (e.PropertyName == nameof(MoveDefinitionViewModel.HasAsset))
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsAssetMissing)));
        }
    }

    public override Color RenderColor => Definition != null ? NormalizeOpaque(Definition.Color) : base.RenderColor;

    /// <summary>
    /// True if the move definition exists but its source animation file could not be
    /// located in the intermediate package root. Renderers use this to paint stripes.
    /// </summary>
    public bool IsAssetMissing => Definition != null && !Definition.HasAsset;

    [Inspectable("Color", "Appearance")]
    public override Color BackgroundColor
    {
        get => Definition != null ? NormalizeOpaque(Definition.Color) : base.BackgroundColor;
        set
        {
            if (Definition != null)
            {
                Color normalized = NormalizeOpaque(value);
                if (Definition.Color == normalized)
                    return;

                Definition.Color = normalized;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(BackgroundColor)));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(RenderColor)));
                NotifyClipDataChanged(nameof(BackgroundColor));
            }
            else
            {
                base.BackgroundColor = value;
            }
        }
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