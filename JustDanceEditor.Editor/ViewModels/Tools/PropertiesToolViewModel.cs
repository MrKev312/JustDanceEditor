using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

using JustDanceEditor.Editor.Attributes;
using JustDanceEditor.Editor.Services;
using JustDanceEditor.Editor.ViewModels;
using JustDanceEditor.Editor.ViewModels.Timeline;
using JustDanceEditor.Formats.JDI.Timelines;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace JustDanceEditor.Editor.ViewModels.Tools;

[RunCommand("Properties", "View/Tools")]
public partial class PropertiesToolViewModel : TimelineToolViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<PropertyCategoryViewModel> Categories { get; set; } = [];

    [ObservableProperty]
    public partial object? SelectedObject { get; set; }

    public PropertiesToolViewModel()
    {
        TimelineContext?.PropertyChanged += Context_PropertyChanged;
    }

    protected override void OnTimelineAttached(TimelineEditorViewModel? timeline)
    {
        RefreshProperties();
    }

    protected override void OnTimelineDetached(TimelineEditorViewModel? timeline)
    {
        // Dispose and clear properties when the timeline is detached
        RefreshProperties();
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineContextService.SelectedObjects))
        {
            RefreshProperties();
        }
    }

    private void RefreshProperties()
    {
        // Dispose existing PropertyItemViewModels to unregister messenger handlers and avoid leaks
        foreach (PropertyCategoryViewModel? cat in Categories.ToList())
        {
            foreach (PropertyItemViewModel? prop in cat.Properties.ToList())
            {
                if (prop is IDisposable d)
                    d.Dispose();
            }
        }

        Categories.Clear();
        SelectedObject = null;

        List<object>? selection = TimelineContext?.SelectedObjects;

        if (selection != null && selection.Count > 0 && ActiveTimeline != null)
        {
            SelectedObject = selection.Count == 1 ? selection[0] : null;

            // 1. Identify common properties
            object first = selection[0];
            var templateProps = first.GetType().GetProperties()
                .Select(p => new { Property = p, Attribute = p.GetCustomAttribute<InspectableAttribute>() })
                .Where(x => x.Attribute != null)
                .OrderBy(x => x.Attribute!.Category);

            var groups = templateProps.GroupBy(x => x.Attribute!.Category);

            foreach (var group in groups)
            {
                PropertyCategoryViewModel categoryVm = new(group.Key);
                foreach (var item in group)
                {
                    // Verify this property exists and has the same attribute on all selected objects
                    bool consistent = selection.All(o =>
                    {
                        PropertyInfo? p = o.GetType().GetProperty(item.Property.Name);
                        return p != null && p.GetCustomAttribute<InspectableAttribute>() != null;
                    });

                    if (consistent)
                    {
                        PropertyItemViewModel propVm;
                        if (item.Property.Name == "BackgroundColor" && first is IHasSharedColorSource colorSource)
                        {
                            // Gather redirect targets from all selected clips that implement the interface.
                            List<object> redirectTargets = [.. selection
                                .OfType<IHasSharedColorSource>()
                                .Select(c => c.GetColorEditTarget("BackgroundColor", ActiveTimeline!))
                                .Where(t => t.HasValue)
                                .Select(t => t!.Value.Target)
                                .Distinct()];
                            if (redirectTargets.Count == 0)
                                continue;

                            string redirectProp = colorSource.GetColorEditTarget("BackgroundColor", ActiveTimeline!)!.Value.PropertyName;
                            propVm = new([.. redirectTargets], redirectProp, item.Attribute!, ActiveTimeline!.UndoService, ActiveTimeline.TimelineStructure, ActiveTimeline.Tracks, ActiveTimeline);
                        }
                        else
                        {
                            // Skip BackgroundColor for clips that don't provide a shared color source —
                            // they are not intended to expose per-clip color editing in the Properties pane.
                            if (item.Property.Name is "BackgroundColor" or "Color" && first is ClipViewModel)
                                continue;

                            propVm = new(
                                selection,
                                item.Property.Name,
                                item.Attribute!,
                                ActiveTimeline!.UndoService,
                                ActiveTimeline.TimelineStructure,
                                ActiveTimeline.Tracks,
                                ActiveTimeline);

                            // Populate dynamic options via IHasDynamicOptions
                            if (first is IHasDynamicOptions dynOpts)
                            {
                                IEnumerable<object>? options = dynOpts.GetDynamicOptions(item.Property.Name, ActiveTimeline!);
                                if (options != null)
                                {
                                    propVm.Options = options.ToList();
                                    propVm.IsEditable = dynOpts.IsDynamicPropertyEditable(item.Property.Name);
                                }
                            }
                        }

                        categoryVm.Properties.Add(propVm);
                    }
                }

                if (categoryVm.Properties.Count > 0)
                    Categories.Add(categoryVm);
            }
        }
    }
}

public class PropertyCategoryViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<PropertyItemViewModel> Properties { get; } = [];
}

public partial class PropertyItemViewModel : ObservableObject, IDisposable
{
    private readonly List<object> _targets;
    private readonly PropertyInfo? _propertyInfoTemplate;
    private readonly string _propertyName;
    private readonly IUndoService _undoService;
    private readonly TimelineStructureDocument _timelineStructure;
    private readonly ObservableCollection<TrackViewModel> _tracks;
    private readonly TimelineEditorViewModel? _timelineEditor;
    private bool _isColorPickerActive = false;
    private object? _colorPickerInitialValue;

    /// <summary>
    /// True while the Value setter is iterating through targets. Suppresses
    /// <see cref="Target_PropertyChanged"/> to avoid intermediate binding
    /// reads that see an inconsistent mix of old/new values across targets.
    /// </summary>
    private bool _isBatchSetting;

    // Direct subscriptions for INotifyPropertyChanged targets (unsubscribed in Dispose)
    private readonly List<INotifyPropertyChanged> _inpcSubscriptions = [];

    private void Target_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isBatchSetting)
            return;

        if (e.PropertyName == _propertyName)
        {
            // Refresh bound value when underlying property changed
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(ColorValue)));
        }
    }

    private record ColorSnapshot(bool IsLyrics, List<(ClipViewModel Clip, Color Color)> ClipColors, string? OldMetadata, List<Color>? FinalColors = null);
    private record MoveDefinitionsSnapshot(List<(MoveDefinitionViewModel Def, Color Color)> DefColors);

    public string Name { get; }
    public bool IsReadOnly { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOptions))]
    [NotifyPropertyChangedFor(nameof(ShowTextBox))]
    public partial System.Collections.IEnumerable? Options { get; set; }

    [ObservableProperty]
    public partial bool IsEditable { get; set; } = true;

    public PropertyItemViewModel(
        List<object> targets,
        string propertyName,
        InspectableAttribute attribute,
        IUndoService undoService,
        TimelineStructureDocument timelineStructure,
        ObservableCollection<TrackViewModel> tracks,
        TimelineEditorViewModel? timelineEditor)
    {
        _targets = targets;
        _propertyName = propertyName;
        _propertyInfoTemplate = targets[0].GetType().GetProperty(propertyName);
        _undoService = undoService;
        _timelineStructure = timelineStructure;
        _tracks = tracks;
        _timelineEditor = timelineEditor;
        Name = attribute.DisplayName;
        IsReadOnly = attribute.IsReadOnly;

        // Listen to external changes on the target objects via weak messaging to avoid leaking references
        // Register a single weak messenger handler to avoid multiple subscriptions when there are multiple targets
        if (_targets.Any(t => t is INotifyPropertyChanged))
        {
            // Subscribe directly to PropertyChanged on targets to get immediate notifications
            foreach (object t in _targets)
            {
                if (t is INotifyPropertyChanged inpc)
                {
                    inpc.PropertyChanged += Target_PropertyChanged;
                    _inpcSubscriptions.Add(inpc);
                }
            }

            // Also listen for ClipDataChangedMessage on the default channel (no token) for clip-originated changes
            WeakReferenceMessenger.Default.Register<PropertyItemViewModel, Messaging.ClipDataChangedMessage>(this, (r, m) =>
            {
                if (m.PropertyName == _propertyName && _targets.Any(t => ReferenceEquals(m.Source, t)))
                {
                    // Notify recipient to refresh bound Value
                    r.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Value)));
                }
            });

            // Ensure we unregister when this view model is disposed
            // (Unregister and inpc unsubscriptions will be handled in Dispose())
        }
    }

    /// <summary>
    /// Notifies that the color picker has opened.
    /// </summary>
    public void OnColorPickerOpened()
    {
        _isColorPickerActive = true;
        _colorPickerInitialValue = null;

        // Redirect targets are MoveDefinitionViewModel objects (from IHasSharedColorSource)
        if (_targets.Count > 0 && _targets[0] is MoveDefinitionViewModel)
        {
            List<(MoveDefinitionViewModel Def, Color Color)> list =
                [.. _targets.OfType<MoveDefinitionViewModel>().Select(d => (d, d.Color))];
            _colorPickerInitialValue = new MoveDefinitionsSnapshot(list);
        }
        // If editing a ClipViewModel's BackgroundColor, capture a full snapshot
        else if (_targets.Count > 0 && _targets[0] is ClipViewModel cv)
        {
            // Lyrics: capture all lyrics clips and metadata
            if (cv is KaraokeClipViewModel)
            {
                // For lyrics, target the timeline-level lyrics definition. The property item
                // will be created bound to that definition, so we don't capture per-clip state here.
                List<(ClipViewModel c, Color BackgroundColor)> selected = [.. _targets.OfType<ClipViewModel>().Select(c => (c, c.BackgroundColor))];
                ColorSnapshot snapshot = new(false, selected, null, null);
                _colorPickerInitialValue = snapshot;
            }
            else if (cv is MoveClipViewModel)
            {
                // For moves, capture the unique move definitions (not per-clip colors)
                List<MoveDefinitionViewModel?> defs = _targets.OfType<MoveClipViewModel>().Select(m => m.Definition).Where(d => d != null).Distinct().ToList()!;
                List<(MoveDefinitionViewModel Def, Color Color)> list = [.. defs.Select(d => (d!, d!.Color))];
                MoveDefinitionsSnapshot snapshot = new(list);
                _colorPickerInitialValue = snapshot;
            }
            else
            {
                List<(ClipViewModel c, Color BackgroundColor)> selected = [.. _targets.OfType<ClipViewModel>().Select(c => (c, c.BackgroundColor))];
                ColorSnapshot snapshot = new(false, selected, null, null);
                _colorPickerInitialValue = snapshot;
            }
        }
        else
        {
            // Fallback: store per-target initial values
            _colorPickerInitialValue = _targets.Select(t => (t, GetValue(t))).ToList();
        }
    }

    /// <summary>
    /// Notifies that the color picker has closed. This pushes the change to the undo stack.
    /// </summary>
    public void OnColorPickerClosed()
    {
        // Keep _isColorPickerActive true until we've recorded the grouped undo to avoid the ColorValue binding
        // triggering an extra undo when the binding finalizes after the picker loses focus.
        try
        {
            // If we have a MoveDefinitionsSnapshot (for moves), push undo/redo at the definition level
            if (_colorPickerInitialValue is MoveDefinitionsSnapshot moveSnap)
            {
                List<Color> initial = [.. moveSnap.DefColors.Select(d => d.Color)];
                List<MoveDefinitionViewModel> defs = [.. moveSnap.DefColors.Select(d => d.Def)];
                List<Color> final = [.. defs.Select(d => new Color(255, d.Color.R, d.Color.G, d.Color.B))];

                bool anyChanged = initial.Where((v, i) => !Equals(v, final[i])).Any();

                if (anyChanged)
                {
                    string initialHex = initial.Count > 0 ? ClipViewModel.ColorToRgbaHex(initial[0]) : "null";
                    string finalHex = final.Count > 0 ? ClipViewModel.ColorToRgbaHex(final[0]) : "null";
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < defs.Count; i++)
                                defs[i].Color = initial[i];
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < defs.Count; i++)
                                defs[i].Color = final[i];
                        }
                    );
                }

                _colorPickerInitialValue = null;
                return;
            }

            // If the picker was editing the lyrics color at the timeline level, record a single
            // undo that updates the timeline metadata only. We no longer set per-clip BackgroundColor
            // during undo/redo to avoid duplicate or conflicting undo entries.

            // If we have a ColorSnapshot (non-lyrics), push a single composite undo/redo
            if (_colorPickerInitialValue is ColorSnapshot snap)
            {
                // Non-lyrics: use the captured list to create undo/redo per captured clip
                List<Color> initialList = [.. snap.ClipColors.Select(cc => cc.Color)];
                List<ClipViewModel> clips = [.. snap.ClipColors.Select(cc => cc.Clip)];
                List<Color> finalList = [.. clips.Select(c => c.BackgroundColor)];

                bool anyChanged = initialList.Where((v, i) => !Equals(v, finalList[i])).Any();

                if (anyChanged)
                {
                    string initialHex = initialList.Count > 0 ? ClipViewModel.ColorToRgbaHex(initialList[0]) : "null";
                    string finalHex = finalList.Count > 0 ? ClipViewModel.ColorToRgbaHex(finalList[0]) : "null";
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < clips.Count; i++)
                                clips[i].BackgroundColor = initialList[i];
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < clips.Count; i++)
                                clips[i].BackgroundColor = finalList[i];
                        }
                    );
                }

                _colorPickerInitialValue = null;
                return;
            }

            // Fallback behavior when we captured per-target initial values
            if (_colorPickerInitialValue is List<(object Target, object? Value)> perTarget)
            {
                List<object?> initialList = [.. perTarget.Select(p => p.Value)];
                List<object> targets = [.. perTarget.Select(p => p.Target)];
                List<object?> finalList = [.. targets.Select(GetValue)];

                bool anyChanged = initialList.Where((v, i) => !Equals(v, finalList[i])).Any();

                if (anyChanged)
                {
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < targets.Count; i++)
                                SetValue(targets[i], initialList[i]);
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < targets.Count; i++)
                                SetValue(targets[i], finalList[i]);
                        }
                    );
                }

                _colorPickerInitialValue = null;
                return;
            }

            // Legacy scalar fallback (single target, non-snapshot)
            object? finalValue = GetValue(_targets[0]);
            if (!Equals(_colorPickerInitialValue, finalValue))
            {
                object? capturedInitial = _colorPickerInitialValue;
                _undoService.Record(
                    undo: () => SetValue(_targets[0], capturedInitial),
                    redo: () => SetValue(_targets[0], finalValue)
                );
            }

            _colorPickerInitialValue = null;
        }
        finally
        {
            _isColorPickerActive = false;
        }
    }

    public object? Value
    {
        get
        {
            // Return value if all are same, else null
            object? firstVal = GetValue(_targets[0]);
            for (int i = 1; i < _targets.Count; i++)
            {
                if (!Equals(GetValue(_targets[i]), firstVal))
                    return null;
            }

            return firstVal;
        }
        set
        {
            if (value == null)
                return; // Don't set nulls explicitly (e.g. from empty selection)

            List<object?> oldValues = [.. _targets.Select(GetValue)];

            // Only push undo if not in color picker mode and the value actually changed
            if (!_isColorPickerActive)
            {
                bool anyDifferent = oldValues.Any(v => !Equals(v, value));

                if (anyDifferent)
                {
                    _undoService.Record(
                        undo: () =>
                        {
                            for (int i = 0; i < _targets.Count; i++)
                                SetValue(_targets[i], oldValues[i]);
                        },
                        redo: () =>
                        {
                            for (int i = 0; i < _targets.Count; i++)
                                SetValue(_targets[i], value);
                        }
                    );
                }
            }

            _isBatchSetting = true;
            try
            {
                foreach (object target in _targets)
                {
                    SetValue(target, value);
                }
            }
            finally
            {
                _isBatchSetting = false;
            }

            OnPropertyChanged(nameof(Value));
        }
    }

    private object? GetValue(object target) => target.GetType().GetProperty(_propertyName)?.GetValue(target);
    private void SetValue(object target, object? val) => target.GetType().GetProperty(_propertyName)?.SetValue(target, val);

    public Type PropertyType => _propertyInfoTemplate!.PropertyType;

    // Binding Helpers
    public bool IsColor => PropertyType == typeof(Color);
    public bool IsBool => PropertyType == typeof(bool);
    public bool IsStringOrNumber => PropertyType == typeof(string) || IsNumber;
    public bool IsNumber => PropertyType == typeof(double) || PropertyType == typeof(int) || PropertyType == typeof(float);
    public bool HasOptions => Options != null;
    public bool ShowTextBox => IsStringOrNumber && !HasOptions;

    public string StringValue
    {
        get => Value?.ToString() ?? "";
        set
        {
            if (IsBinding)
                return;
            try
            {
                IsBinding = true;
                if (PropertyType == typeof(string))
                    Value = value;
                else if (PropertyType == typeof(double) && double.TryParse(value, out double d))
                    Value = d;
                else if (PropertyType == typeof(int) && int.TryParse(value, out int i))
                    Value = i;
                else if (PropertyType == typeof(float) && float.TryParse(value, out float f))
                    Value = f;
            }
            finally
            {
                IsBinding = false;
            }
        }
    }

    public bool BoolValue
    {
        get => Value is bool b && b;
        set => Value = value;
    }

    public Color ColorValue
    {
        get => Value is Color c ? c : Colors.Transparent;
        set => Value = value;
    }

    public object? SelectedOption
    {
        get
        {
            if (Options == null)
                return null;
            string currentVal = StringValue;
            foreach (object? opt in Options)
            {
                if (opt is PictogramOptionViewModel p && p.Name == currentVal)
                    return opt;
                if (opt is string s && s == currentVal)
                    return opt;
            }

            return null;
        }
        set
        {
            if (value is PictogramOptionViewModel p)
                StringValue = p.Name;
            else if (value != null)
                StringValue = value.ToString() ?? "";
        }
    }

    public string HexString
    {
        get => Value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}" : "";
        set
        {
            // Try parsing as RGBA hex first (our format), then fall back to standard parsing
            if (!string.IsNullOrEmpty(value))
            {
                // Check if the target is a KaraokeClip (for which we use RGBA format)
                bool isLyricsClip = _targets.Count > 0 && _targets[0] is KaraokeClipViewModel;

                if (isLyricsClip)
                {
                    // Use RGBA parsing for lyrics
                    ColorValue = ClipViewModel.ParseRgbaHex(value);
                }
                else
                {
                    // For other clips, use standard ARGB parsing with full alpha
                    if (Color.TryParse(value, out Color c))
                    {
                        ColorValue = new Color(255, c.R, c.G, c.B);
                    }
                }
            }
        }
    }

    private bool IsBinding = false;

    public void Dispose()
    {
        // Unregister messenger handler for clip data changes
        try
        {
            WeakReferenceMessenger.Default.Unregister<Messaging.ClipDataChangedMessage>(this);
        }
        catch { }

        // Unsubscribe any direct PropertyChanged subscriptions
        foreach (INotifyPropertyChanged inpc in _inpcSubscriptions)
        {
            try
            {
                inpc.PropertyChanged -= Target_PropertyChanged;
            }
            catch { }
        }

        _inpcSubscriptions.Clear();

        GC.SuppressFinalize(this);
    }

    // Refresh typed properties when Value changes
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Value))
        {
            OnPropertyChanged(nameof(StringValue));
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(ColorValue));
            OnPropertyChanged(nameof(HexString));
            OnPropertyChanged(nameof(SelectedOption));
        }
    }
}